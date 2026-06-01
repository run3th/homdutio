namespace Homdutio.Data.Entities;

/// <summary>
/// A household — the first real domain entity (S-02). Carries identity (name + creation timestamp) and
/// owns its <see cref="HouseholdMember"/> rows. There is no task data yet (S-03); this slice only
/// establishes membership identity and the server-derived scoping every later slice inherits.
/// </summary>
public class Household
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<HouseholdMember> Members { get; set; } = new List<HouseholdMember>();
}
