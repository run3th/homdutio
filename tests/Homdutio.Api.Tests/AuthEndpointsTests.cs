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

    private async Task<string> RegisterAndGetRefreshTokenAsync(string email)
    {
        (await _client.PostAsJsonAsync("/api/auth/register", Credentials(email))).EnsureSuccessStatusCode();

        var login = await _client.PostAsJsonAsync("/api/auth/login", Credentials(email));
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginBody>();
        return body!.RefreshToken;
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });

    private Task<HttpResponseMessage> LogoutAsync(string refreshToken) =>
        _client.PostAsJsonAsync("/api/auth/logout", new { refreshToken });

    private async Task<string> UserIdOfAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Users.AsNoTracking().Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
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
    public async Task Refresh_rotates_token_old_rejected_new_works()
    {
        var email = $"rotate-{Guid.NewGuid():N}@example.test";
        var token1 = await RegisterAndGetRefreshTokenAsync(email);

        var r1 = await RefreshAsync(token1);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var token2 = (await r1.Content.ReadFromJsonAsync<LoginBody>())!.RefreshToken;
        Assert.NotEqual(token1, token2);

        // The rotated token works...
        var r2 = await RefreshAsync(token2);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        // ...and the original (now consumed) token is rejected.
        var r3 = await RefreshAsync(token1);
        Assert.Equal(HttpStatusCode.Unauthorized, r3.StatusCode);
    }

    [Fact]
    public async Task Refresh_with_expired_token_returns_401()
    {
        var email = $"expired-{Guid.NewGuid():N}@example.test";
        var token = await RegisterAndGetRefreshTokenAsync(email);

        // Backdate the persisted row's expiry — the fresh user has exactly one token.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userId = await db.Users.AsNoTracking().Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            var row = await db.RefreshTokens.SingleAsync(r => r.UserId == userId);
            row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var response = await RefreshAsync(token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_replay_of_consumed_token_kills_the_family()
    {
        var email = $"replay-{Guid.NewGuid():N}@example.test";
        var token1 = await RegisterAndGetRefreshTokenAsync(email);

        var r1 = await RefreshAsync(token1);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var token2 = (await r1.Content.ReadFromJsonAsync<LoginBody>())!.RefreshToken;

        // Replaying the already-consumed original token → 401...
        var replay = await RefreshAsync(token1);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // ...and the whole family is now dead: the rotated successor is rejected too.
        var afterReplay = await RefreshAsync(token2);
        Assert.Equal(HttpStatusCode.Unauthorized, afterReplay.StatusCode);

        // Every row in the family carries RevokedAtUtc.
        var userId = await UserIdOfAsync(email);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await db.RefreshTokens.AsNoTracking().Where(r => r.UserId == userId).ToListAsync();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.NotNull(r.RevokedAtUtc));
    }

    [Fact]
    public async Task Logout_revokes_family_then_refresh_returns_401()
    {
        var email = $"logout-{Guid.NewGuid():N}@example.test";
        var token = await RegisterAndGetRefreshTokenAsync(email);

        var logout = await LogoutAsync(token);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        var refresh = await RefreshAsync(token);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Unknown_token_refresh_401_and_logout_200_idempotent()
    {
        var refresh = await RefreshAsync("this-token-was-never-issued");
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        var logout = await LogoutAsync("this-token-was-never-issued");
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
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
        // No display name supplied at registration → backend falls back to the email local-part.
        Assert.Equal(email.Split('@')[0], me.DisplayName);
        // Avatar storage arrives in S-09 Phase 3; until then /me carries a null avatar URL.
        Assert.Null(me.AvatarUrl);
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

    private sealed record MeBody(string? Sub, string? Email, string? DisplayName, string? AvatarUrl);
}
