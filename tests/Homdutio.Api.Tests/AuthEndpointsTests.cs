using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Homdutio.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homdutio.Api.Tests;

/// <summary>
/// Proves the JWT issue + validate pipeline end-to-end through the real ASP.NET middleware:
/// register → login (signed token) → authorized /me (200), and that /me rejects a missing or
/// malformed token (401).
/// </summary>
public class AuthEndpointsTests : IClassFixture<AuthApiFactory>
{
    private const string Password = "P@ssw0rd!23";
    private readonly HttpClient _client;
    private readonly AuthApiFactory _factory;

    public AuthEndpointsTests(AuthApiFactory factory)
    {
        factory.EnsureDatabaseMigrated();
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static object Credentials(string email) => new { email, password = Password };

    private async Task<string> RegisterAndLoginAsync(string email)
    {
        (await _client.PostAsJsonAsync("/api/auth/register", Credentials(email))).EnsureSuccessStatusCode();

        var login = await _client.PostAsJsonAsync("/api/auth/login", Credentials(email));
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginBody>();
        return body!.AccessToken;
    }

    [Fact]
    public async Task Register_returns_ok()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/register", Credentials($"reg-{Guid.NewGuid():N}@example.test"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_valid_credentials_returns_token()
    {
        var email = $"login-{Guid.NewGuid():N}@example.test";
        (await _client.PostAsJsonAsync("/api/auth/register", Credentials(email))).EnsureSuccessStatusCode();

        var response = await _client.PostAsJsonAsync("/api/auth/login", Credentials(email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginBody>();
        Assert.False(string.IsNullOrEmpty(body!.AccessToken));
    }

    [Fact]
    public async Task Login_issues_refresh_token_and_persists_a_hashed_row()
    {
        var email = $"refresh-{Guid.NewGuid():N}@example.test";
        (await _client.PostAsJsonAsync("/api/auth/register", Credentials(email))).EnsureSuccessStatusCode();

        var response = await _client.PostAsJsonAsync("/api/auth/login", Credentials(email));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginBody>();

        Assert.False(string.IsNullOrEmpty(body!.RefreshToken));

        // The persisted row stores a hash, not the raw token, and is live (not yet consumed/revoked).
        // Scoped to this login's user — the class fixture shares one database across tests.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = await db.Users.AsNoTracking()
            .Where(u => u.Email == email)
            .Select(u => u.Id)
            .SingleAsync();
        var row = await db.RefreshTokens.AsNoTracking().SingleAsync(r => r.UserId == userId);

        Assert.NotEqual(body.RefreshToken, row.TokenHash);
        Assert.Equal(64, row.TokenHash.Length);
        Assert.NotEqual(Guid.Empty, row.FamilyId);
        Assert.True(row.ExpiresAtUtc > DateTime.UtcNow.AddDays(29));
        Assert.Null(row.ConsumedAtUtc);
        Assert.Null(row.RevokedAtUtc);
    }

    [Fact]
    public async Task Me_with_bearer_token_returns_claims()
    {
        var email = $"me-{Guid.NewGuid():N}@example.test";
        var token = await RegisterAndLoginAsync(email);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeBody>();
        Assert.Equal(email, me!.Email);
        Assert.False(string.IsNullOrEmpty(me.Sub));
    }

    [Fact]
    public async Task Me_without_token_returns_401()
    {
        var response = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_with_malformed_token_returns_401()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.valid.jwt");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAtUtc, string RefreshToken);

    private sealed record MeBody(string? Sub, string? Email);
}
