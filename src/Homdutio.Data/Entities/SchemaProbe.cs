namespace Homdutio.Data.Entities;

/// <summary>
/// Throwaway probe entity. It exists only so the initial migration is non-empty and a real
/// write/read round-trip can be verified against the SQL Server provider (LocalDB + Azure SQL).
/// Remove this entity (and its migration) once the first real domain entities land.
/// </summary>
public class SchemaProbe
{
    public int Id { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string Note { get; set; } = string.Empty;
}
