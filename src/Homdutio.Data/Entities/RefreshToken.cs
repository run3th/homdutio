namespace Homdutio.Data.Entities;

/// <summary>
/// A persisted, server-side refresh credential (S-10). The raw token is a high-entropy random string
/// handed to the client once and never stored; the server keeps only its SHA-256 <see cref="TokenHash"/>,
/// so the table is useless to an attacker who reads it. Refresh tokens rotate on every use: presenting a
/// token consumes its row (<see cref="ConsumedAtUtc"/>) and issues a successor sharing the same
/// <see cref="FamilyId"/> — the lineage of one login. Re-presenting an already-consumed token is a replay
/// and revokes the whole family. Like <c>HouseholdInvite</c>, single-use is made atomic by the
/// <see cref="RowVersion"/> optimistic-concurrency token (single-winner consume). <see cref="UserId"/> is a
/// raw <c>AspNetUsers.Id</c> value with no navigation — mapping it as an FK would add a cascade path
/// through AspNetUsers that is neither needed nor wanted here.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }

    /// <summary>The account this token authenticates (raw <c>AspNetUsers.Id</c>, no navigation).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>SHA-256 hash (lowercase hex) of the raw token. The raw token is never persisted.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Shared across every token in one rotation chain (one login). Revoked as a unit on replay/logout.</summary>
    public Guid FamilyId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>When the token stops being valid; refresh past this point is rejected without family revocation.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Set when the token is rotated; non-null means a later presentation is a replay (FR — family kill).</summary>
    public DateTime? ConsumedAtUtc { get; set; }

    /// <summary>Set on logout or replay; non-null means the token (and usually its family) can never refresh again.</summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>SQL Server <c>rowversion</c> — the optimistic-concurrency guard that makes consume single-winner.</summary>
    public byte[] RowVersion { get; set; } = [];
}
