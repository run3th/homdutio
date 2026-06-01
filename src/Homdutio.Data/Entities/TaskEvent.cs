namespace Homdutio.Data.Entities;

/// <summary>
/// One immutable entry in a <see cref="HouseholdTask"/>'s append-only audit log (NFR-3) — the honest
/// who-did-what record that persists for the lifetime of the household, even after the card has closed off
/// the board. Each lifecycle transition appends exactly one event in the same <c>SaveChanges</c> that
/// mutates the task projection, so the log can never diverge from current state. Append-only: there is no
/// update or delete path in v1.
/// </summary>
public class TaskEvent
{
    public Guid Id { get; set; }

    public Guid TaskId { get; set; }

    public HouseholdTask? Task { get; set; }

    public TaskEventType Type { get; set; }

    /// <summary>The user who performed the transition (FK → <c>AspNetUsers.Id</c>).</summary>
    public string ActorId { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    /// <summary>Meaningful only on a <see cref="TaskEventType.Confirmed"/> event; default <c>false</c>.</summary>
    public bool SelfAttested { get; set; }
}
