namespace Homdutio.Data.Entities;

/// <summary>
/// A member's role within a household. The creator is written as <see cref="Admin"/> (S-02); S-06 adds
/// members as <see cref="Member"/>, and S-09 promotes/demotes by updating this value. Persisted as a
/// string (see <c>ApplicationDbContext.OnModelCreating</c>) so rows stay readable and the enum can grow
/// without a numeric remap.
/// </summary>
public enum HouseholdRole
{
    Admin,
    Member,
}
