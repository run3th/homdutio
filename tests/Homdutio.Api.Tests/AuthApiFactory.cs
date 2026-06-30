using Homdutio.Api.Email;
using Homdutio.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Homdutio.Api.Tests;

/// <summary>
/// Hosts the real API via <see cref="WebApplicationFactory{TEntryPoint}"/> against a uniquely-named
/// throwaway LocalDB database (provider parity with prod) and a deterministic test JWT key/issuer/
/// audience supplied via in-memory configuration. Migrations are applied on first use; the database
/// is dropped on disposal so runs are isolated and repeatable.
/// </summary>
public class AuthApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public AuthApiFactory()
    {
        var databaseName = $"Homdutio_ApiTest_{Guid.NewGuid():N}";
        _connectionString =
            $@"Server=(localdb)\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true";
    }

    /// <summary>Captures the reset link/token instead of sending, so reset tests can read what the endpoint built.</summary>
    public CapturingEmailSender EmailSender { get; } = new();

    /// <summary>Forgot-password rate-limit threshold for the test host. High by default so functional tests never trip it; a derived host lowers it to assert the 429.</summary>
    protected virtual int ForgotPasswordPermitLimit => 1000;

    /// <summary>Invite rate-limit threshold for the test host. High by default so functional tests never trip it; a derived host lowers it to assert the 429.</summary>
    protected virtual int InvitePermitLimit => 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Jwt:Issuer"] = "homdutio-test",
                ["Jwt:Audience"] = "homdutio-test",
                ["Jwt:SigningKey"] = "homdutio-integration-test-signing-key-0123456789",
                ["Jwt:AccessTokenMinutes"] = "120",
                ["RateLimiting:ForgotPassword:PermitLimit"] = ForgotPasswordPermitLimit.ToString(),
                ["RateLimiting:Invite:PermitLimit"] = InvitePermitLimit.ToString(),
            });
        });

        // Swap the real ACS sender for a capturing fake — no email ever leaves the test host.
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(EmailSender);
        });
    }

    /// <summary>Applies migrations to the throwaway database. Idempotent — safe to call per test.</summary>
    public void EnsureDatabaseMigrated()
    {
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // Drop the throwaway database via a standalone context — the host's service provider is already
        // disposed by the time the fixture tears down, so it can't be used here.
        if (disposing)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(_connectionString)
                .Options;
            using var context = new ApplicationDbContext(options);
            context.Database.EnsureDeleted();
        }
    }
}
