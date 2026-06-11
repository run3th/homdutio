namespace Homdutio.Data.Entities;

/// <summary>
/// The kind of transition recorded by a <see cref="TaskEvent"/> in the append-only audit log (NFR-3).
/// The S-03 loop appends <see cref="Created"/> → <see cref="Claimed"/> → <see cref="MarkedDone"/> →
/// <see cref="Confirmed"/>. S-05's loop-recovery transitions *append* <see cref="Unclaimed"/> (an in-progress
/// task returned to To do, unassigned) and <see cref="SentBack"/> (a Done task returned to In progress with a
/// required reason) without restructuring the log. Persisted as a readable string so the audit trail stays legible.
/// </summary>
public enum TaskEventType
{
    Created,
    Claimed,
    MarkedDone,
    Confirmed,
    Unclaimed,
    SentBack,
}
