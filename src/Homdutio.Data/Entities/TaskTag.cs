namespace Homdutio.Data.Entities;

/// <summary>
/// A free-text tag on a <see cref="HouseholdTask"/> (the multi-value generalization of the old single
/// <c>Category</c>). Modeled as a one-to-many child row like <see cref="TaskComment"/>: its own Guid PK, a
/// cascade FK back to the task, and the tag <see cref="Value"/>. <see cref="HouseholdId"/> is denormalized
/// (copied from the owning task) so the per-household suggestion query — which must include tags from
/// closed tasks the board no longer returns — can run as a single indexed <c>DISTINCT</c> with no join.
/// Tags are rewritten wholesale on edit (delete-all + re-insert), so there is no per-tag update path.
/// </summary>
public class TaskTag
{
    public Guid Id { get; set; }

    public Guid TaskId { get; set; }

    public HouseholdTask? Task { get; set; }

    /// <summary>Denormalized from the owning task so suggestions query by household without a join.</summary>
    public Guid HouseholdId { get; set; }

    public string Value { get; set; } = string.Empty;
}
