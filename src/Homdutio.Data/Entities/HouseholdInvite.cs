namespace Homdutio.Data.Entities;

/// <summary>
/// A single-use, time-expiring invitation to join a household (S-06, FR-005). The <see cref="Token"/> is an
/// unguessable URL-safe string shared out-of-band; opening it lets a second adult join (FR-006). Single-use
/// is enforced by <see cref="ConsumedAtUtc"/> plus the <see cref="RowVersion"/> optimistic-concurrency token:
/// the accept path sets the consumed fields and inserts the membership in one <c>SaveChanges</c>, so a
/// concurrent second accept fails the version check and is rejected (the token consumes exactly once).
/// Scoped to exactly one <see cref="Household"/> (US-02) via the FK. Like <c>HouseholdTask</c>, the actor
/// columns (<see cref="CreatedById"/>/<see cref="ConsumedById"/>) are raw <c>AspNetUsers.Id</c> values with
/// no navigation — mapping them as FKs would introduce multiple cascade paths through AspNetUsers.
/// </summary>
public class HouseholdInvite
{
    public Guid Id { get; set; }

    /// <summary>The household this invite grants membership to (FK → <see cref="Household"/>).</summary>
    public Guid HouseholdId { get; set; }

    public Household? Household { get; set; }

    /// <summary>The unguessable URL-safe token carried in the <c>/join/&lt;token&gt;</c> link (unique).</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>The member who generated the invite (raw <c>AspNetUsers.Id</c>, no navigation).</summary>
    public string CreatedById { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>When the invite stops being valid; preview/accept past this point are rejected.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Set when the invite is consumed; non-null means the link can never join again (FR-005).</summary>
    public DateTime? ConsumedAtUtc { get; set; }

    /// <summary>The user who consumed the invite, once consumed (raw <c>AspNetUsers.Id</c>, no navigation).</summary>
    public string? ConsumedById { get; set; }

    /// <summary>SQL Server <c>rowversion</c> — the optimistic-concurrency guard that makes consume single-use.</summary>
    public byte[] RowVersion { get; set; } = [];
}
