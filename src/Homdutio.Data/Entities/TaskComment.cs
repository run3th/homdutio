namespace Homdutio.Data.Entities;

/// <summary>
/// A flat, immutable comment on a <see cref="HouseholdTask"/> (S-05) — the household discussion thread and
/// the home of the send-back reason. Append-only like <see cref="TaskEvent"/>: there is no edit or delete
/// path in v1. <see cref="Kind"/> distinguishes a free-form member note from a lifecycle send-back reason so
/// the UI can label/lock the latter; a send-back writes its reason as a <see cref="TaskCommentKind.SendBack"/>
/// comment in the same <c>SaveChanges</c> as the transition, so the thread can never show a reason for a
/// transition that didn't persist.
/// </summary>
public class TaskComment
{
    public Guid Id { get; set; }

    public Guid TaskId { get; set; }

    public HouseholdTask? Task { get; set; }

    /// <summary>The user who posted the comment (FK → <c>AspNetUsers.Id</c>, no navigation — see <see cref="TaskEvent.ActorId"/>).</summary>
    public string AuthorId { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public TaskCommentKind Kind { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
