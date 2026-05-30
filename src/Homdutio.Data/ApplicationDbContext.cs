using Homdutio.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Homdutio.Data;

/// <summary>
/// The application's single EF Core DbContext. For now it carries only the throwaway
/// <see cref="SchemaProbe"/> set — real domain entities (households, tasks, the audit record,
/// ASP.NET Identity) arrive with the slices that need them.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<SchemaProbe> SchemaProbes => Set<SchemaProbe>();
}
