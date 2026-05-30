using Homdutio.Data;
using Homdutio.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Homdutio.Data.Tests;

/// <summary>
/// Proves the persistence wiring end-to-end against the real SqlServer provider (LocalDB): migrations
/// apply and a <see cref="SchemaProbe"/> survives a write on one context then a read on a fresh one.
/// Each run uses a uniquely-named throwaway database that is dropped on teardown so runs are isolated.
/// </summary>
public class PersistenceSmokeTests : IDisposable
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public PersistenceSmokeTests()
    {
        var dbName = $"Homdutio_Test_{Guid.NewGuid():N}";
        var connectionString =
            $@"Server=(localdb)\MSSQLLocalDB;Database={dbName};Trusted_Connection=True;MultipleActiveResultSets=true";

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;
    }

    [Fact]
    public void Migrations_apply_and_SchemaProbe_round_trips()
    {
        int newId;
        var note = $"round-trip-{Guid.NewGuid():N}";
        var createdAtUtc = DateTime.UtcNow;

        // Apply migrations and write on one context instance.
        using (var write = new ApplicationDbContext(_options))
        {
            write.Database.Migrate();

            var probe = new SchemaProbe { CreatedAtUtc = createdAtUtc, Note = note };
            write.SchemaProbes.Add(probe);
            write.SaveChanges();

            newId = probe.Id;
        }

        Assert.True(newId > 0, "SaveChanges should assign an identity key.");

        // Read back on a fresh context instance to prove it actually persisted.
        using var read = new ApplicationDbContext(_options);
        var loaded = read.SchemaProbes.Single();

        Assert.Equal(newId, loaded.Id);
        Assert.Equal(note, loaded.Note);
    }

    public void Dispose()
    {
        using var ctx = new ApplicationDbContext(_options);
        ctx.Database.EnsureDeleted();
    }
}
