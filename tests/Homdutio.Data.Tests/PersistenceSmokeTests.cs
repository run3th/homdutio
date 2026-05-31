using Homdutio.Data;
using Homdutio.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Homdutio.Data.Tests;

/// <summary>
/// Proves the persistence wiring end-to-end against the real SqlServer provider (LocalDB): the
/// Identity migration applies and an <see cref="ApplicationUser"/> survives a write on one context
/// then a read on a fresh one. Each run uses a uniquely-named throwaway database that is dropped on
/// teardown so runs are isolated.
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
    public void Migrations_apply_and_ApplicationUser_round_trips()
    {
        string newId;
        var email = $"smoke-{Guid.NewGuid():N}@example.test";

        // Apply migrations and write on one context instance.
        using (var write = new ApplicationDbContext(_options))
        {
            write.Database.Migrate();

            var user = new ApplicationUser { UserName = email, Email = email };
            write.Users.Add(user);
            write.SaveChanges();

            newId = user.Id;
        }

        Assert.False(string.IsNullOrEmpty(newId), "Identity should assign a key.");

        // Read back on a fresh context instance to prove it actually persisted.
        using var read = new ApplicationDbContext(_options);
        var loaded = read.Users.Single();

        Assert.Equal(newId, loaded.Id);
        Assert.Equal(email, loaded.Email);
    }

    public void Dispose()
    {
        using var ctx = new ApplicationDbContext(_options);
        ctx.Database.EnsureDeleted();
    }
}
