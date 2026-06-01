namespace Homdutio.Data.Entities;

/// <summary>
/// The lifecycle status of a <see cref="HouseholdTask"/> (S-03). A task moves
/// <see cref="ToDo"/> → <see cref="InProgress"/> → <see cref="Done"/> as it is claimed and marked done.
/// Closure is a separate concept — <c>HouseholdTask.ClosedAtUtc</c> being non-null, set at admin confirm —
/// **not** a status value; the status stays at <see cref="Done"/> after confirmation. Persisted as a
/// readable string (see <c>ApplicationDbContext.OnModelCreating</c>) so rows stay legible and the enum can
/// grow without a numeric remap. Named with the <c>HouseholdTask</c> prefix to avoid colliding with
/// <see cref="System.Threading.Tasks.Task"/> across the async API.
/// </summary>
public enum HouseholdTaskStatus
{
    ToDo,
    InProgress,
    Done,
}
