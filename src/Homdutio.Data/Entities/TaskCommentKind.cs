namespace Homdutio.Data.Entities;

/// <summary>
/// What a <see cref="TaskComment"/> is: a free-form member note (<see cref="Member"/>) or the required
/// reason an admin attached when sending a Done task back to In progress (<see cref="SentBack"/>, S-05).
/// The send-back reason is just a comment of this kind — there is no separate column on <see cref="TaskEvent"/>.
/// Persisted as a readable string so rows stay legible and the enum can grow without a numeric remap.
/// </summary>
public enum TaskCommentKind
{
    Member,
    SendBack,
}
