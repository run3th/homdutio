namespace Homdutio.Data.Entities;

/// <summary>
/// A household task — the north-star entity (S-03). This row is the current-state **projection** (status +
/// claimer/confirmer + timestamps) that makes board rendering one cheap query; the durable who-did-what
/// record is the append-only <see cref="TaskEvent"/> chain that outlives the visible card. Closure is the
/// non-null <see cref="ClosedAtUtc"/> (set at admin confirm), never a delete and never a status value — a
/// closed task simply stops coming back from <c>GET /api/tasks</c>. Named <c>HouseholdTask</c> (not bare
/// <c>Task</c>) to avoid colliding with <see cref="System.Threading.Tasks.Task"/> across the async API.
/// </summary>
public class HouseholdTask
{
    public Guid Id { get; set; }

    /// <summary>The scoping key — every task belongs to exactly one household (FK → <see cref="Household"/>).</summary>
    public Guid HouseholdId { get; set; }

    public Household? Household { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Category { get; set; }

    public HouseholdTaskStatus Status { get; set; }

    /// <summary>
    /// The manual, household-shared position of this task *within its current status column* (FR-021 — the
    /// only priority surface in v1). The board query partitions by <see cref="Status"/> first, so values may
    /// repeat across columns harmlessly. Maintained as a contiguous <c>0..n-1</c> per column: set to the
    /// bottom of a column on create and on each claim/done transition, and reassigned wholesale on reorder.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>The user who created the task (FK → <c>AspNetUsers.Id</c>).</summary>
    public string CreatedById { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>The user who claimed the task, once claimed (FK → <c>AspNetUsers.Id</c>).</summary>
    public string? ClaimedById { get; set; }

    public DateTime? ClaimedAtUtc { get; set; }

    public DateTime? DoneAtUtc { get; set; }

    /// <summary>The admin who confirmed the task, once confirmed (FK → <c>AspNetUsers.Id</c>).</summary>
    public string? ConfirmedById { get; set; }

    /// <summary>Set at confirm time; closure is this being non-null, not a status value.</summary>
    public DateTime? ClosedAtUtc { get; set; }

    /// <summary>True when the confirmer was also the claimer (computed server-side at confirm, FR-016).</summary>
    public bool SelfAttested { get; set; }

    public ICollection<TaskEvent> Events { get; set; } = new List<TaskEvent>();

    /// <summary>The task's free-text tags (S-12) — the multi-value generalization of <see cref="Category"/>.</summary>
    public ICollection<TaskTag> Tags { get; set; } = new List<TaskTag>();
}
