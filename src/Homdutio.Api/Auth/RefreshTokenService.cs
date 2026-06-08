using System.Security.Cryptography;
using Homdutio.Data;
using Homdutio.Data.Entities;
using Microsoft.Extensions.Options;

namespace Homdutio.Api.Auth;

/// <summary>
/// Owns the server-side lifecycle of refresh tokens (S-10): mint a high-entropy raw token, persist only
/// its SHA-256 hash, and (from Phase 2) validate + rotate on use, detect replay, and revoke a family.
/// Depends on <see cref="ApplicationDbContext"/>, so it is registered <b>scoped</b> — unlike the singleton
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
        var rawToken = GenerateRawToken();
        var now = DateTime.UtcNow;
        var expiresAtUtc = now.AddDays(_options.RefreshTokenDays);

        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = Hash(rawToken),
            FamilyId = familyId ?? Guid.NewGuid(),
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc,
        });

        await _db.SaveChangesAsync(ct);

        return new IssuedRefreshToken(rawToken, expiresAtUtc);
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
