using System.Security.Cryptography;
using Homdutio.Data;
using Homdutio.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homdutio.Api.Auth;

/// <summary>
/// Owns the server-side lifecycle of refresh tokens (S-10): mint a high-entropy raw token, persist only
/// its SHA-256 hash, validate + rotate on use, detect replay, and revoke a family. Depends on
/// <see cref="ApplicationDbContext"/>, so it is registered <b>scoped</b> — unlike the singleton
/// <see cref="JwtTokenService"/>. Token <i>minting</i> (stateless) and refresh <i>persistence</i> (scoped)
/// are deliberately kept in separate services so a singleton never captures a scoped DbContext.
/// </summary>
public sealed class RefreshTokenService
{
    private readonly ApplicationDbContext _db;
    private readonly JwtOptions _options;

    public RefreshTokenService(ApplicationDbContext db, IOptions<JwtOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    /// <summary>
    /// Issues a new refresh token. Pass <paramref name="familyId"/> to continue an existing rotation chain
    /// (on refresh); omit it to start a new family (on login). Persists the token's hash and returns the
    /// <b>raw</b> token to the caller exactly once — it is never stored and cannot be recovered afterwards.
    /// </summary>
    public async Task<IssuedRefreshToken> IssueAsync(string userId, Guid? familyId = null, CancellationToken ct = default)
    {
        var token = BuildToken(userId, familyId ?? Guid.NewGuid(), out var rawToken);
        _db.RefreshTokens.Add(token);
        await _db.SaveChangesAsync(ct);
        return new IssuedRefreshToken(rawToken, token.ExpiresAtUtc);
    }

    /// <summary>
    /// Validates a presented refresh token and, on success, rotates it: marks the row consumed and issues a
    /// successor in the same family in one atomic save (the <see cref="RefreshToken.RowVersion"/> guard makes
    /// the consume single-winner). Re-presenting an already-consumed token is a replay → the whole family is
    /// revoked. Expired/revoked/unknown tokens are rejected without touching the family.
    /// </summary>
    public async Task<RefreshRotation> ValidateAndRotateAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(rawToken))
        {
            return new RefreshRotation(RefreshOutcome.NotFound);
        }

        var hash = Hash(rawToken);
        var row = await _db.RefreshTokens.SingleOrDefaultAsync(r => r.TokenHash == hash, ct);

        if (row is null)
        {
            return new RefreshRotation(RefreshOutcome.NotFound);
        }

        if (row.RevokedAtUtc is not null)
        {
            return new RefreshRotation(RefreshOutcome.Revoked);
        }

        if (row.ConsumedAtUtc is not null)
        {
            // Replay: a token that was already rotated is presented again. The whole lineage descended from
            // this login is now suspect → revoke every live token sharing its family.
            await RevokeFamilyAsync(row.FamilyId, ct);
            return new RefreshRotation(RefreshOutcome.Replay);
        }

        if (row.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return new RefreshRotation(RefreshOutcome.Expired);
        }

        row.ConsumedAtUtc = DateTime.UtcNow;
        var successor = BuildToken(row.UserId, row.FamilyId, out var successorRaw);
        _db.RefreshTokens.Add(successor);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent refresh consumed this exact row first (e.g. a double-fired startup). That is a
            // benign race, not a malicious replay — reject this loser WITHOUT killing the family. The
            // successor add never persisted (the whole SaveChanges rolled back).
            return new RefreshRotation(RefreshOutcome.Revoked);
        }

        return new RefreshRotation(RefreshOutcome.Success, row.UserId, successorRaw, successor.ExpiresAtUtc);
    }

    /// <summary>
    /// Logout: revoke every live token in the presented token's family. Idempotent and existence-safe — an
    /// unknown/garbage/empty token is a silent no-op, so the caller can never tell whether a token existed.
    /// </summary>
    public async Task RevokeFamilyAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(rawToken))
        {
            return;
        }

        var hash = Hash(rawToken);
        var familyId = await _db.RefreshTokens
            .Where(r => r.TokenHash == hash)
            .Select(r => (Guid?)r.FamilyId)
            .SingleOrDefaultAsync(ct);

        if (familyId is not null)
        {
            await RevokeFamilyAsync(familyId.Value, ct);
        }
    }

    /// <summary>Stamps <see cref="RefreshToken.RevokedAtUtc"/> on every not-yet-revoked row in the family.</summary>
    private async Task RevokeFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var family = await _db.RefreshTokens
            .Where(r => r.FamilyId == familyId && r.RevokedAtUtc == null)
            .ToListAsync(ct);

        if (family.Count == 0)
        {
            return;
        }

        foreach (var token in family)
        {
            token.RevokedAtUtc = now;
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Builds (but does not persist) a fresh token row + its raw secret for the given family.</summary>
    private RefreshToken BuildToken(string userId, Guid familyId, out string rawToken)
    {
        rawToken = GenerateRawToken();
        var now = DateTime.UtcNow;
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = Hash(rawToken),
            FamilyId = familyId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(_options.RefreshTokenDays),
        };
    }

    /// <summary>A 256-bit cryptographically-random token, URL-safe base64 (no padding).</summary>
    private static string GenerateRawToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>SHA-256 of the raw token as lowercase hex — what we persist and look up by.</summary>
    internal static string Hash(string rawToken)
    {
        var digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}

/// <summary>The raw refresh token handed back to the caller once, plus its expiry.</summary>
public sealed record IssuedRefreshToken(string RawToken, DateTime ExpiresAtUtc);

/// <summary>Distinct refresh outcomes. Only <see cref="RefreshOutcome.Success"/> carries a rotated token.</summary>
public enum RefreshOutcome
{
    Success,
    NotFound,
    Expired,
    Revoked,
    Replay,
}

/// <summary>
/// The result of <see cref="RefreshTokenService.ValidateAndRotateAsync"/>. On success, carries the
/// rotated raw refresh token + its expiry and the owning user id; otherwise the failure outcome only.
/// </summary>
public sealed record RefreshRotation(
    RefreshOutcome Outcome,
    string? UserId = null,
    string? RawToken = null,
    DateTime? ExpiresAtUtc = null);
