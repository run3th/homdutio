namespace Homdutio.Data.Entities;

/// <summary>
/// The join row linking an <see cref="ApplicationUser"/> to a <see cref="Household"/> with a role and a
/// join timestamp. A unique index on <see cref="UserId"/> enforces the v1 invariant of one household per
/// user (FR-007). S-06 inserts a row to add a member; S-09 updates <see cref="Role"/> to promote/demote.
/// </summary>
public class HouseholdMember
{
    public Guid Id { get; set; }

    public Guid HouseholdId { get; set; }

    public Household? Household { get; set; }

    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    public HouseholdRole Role { get; set; }

    public DateTime JoinedAtUtc { get; set; }
}
