using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Homdutio.Api.Tests;

/// <summary>
/// Locks the invariants S-02 introduces: create→read round-trip with the caller as Admin,
/// one-household-per-user (second create → 409), name validation, the auth requirement, and the
/// fresh-user 204. Reuses <see cref="AuthApiFactory"/> (host + throwaway DB + JWT) and the
/// register→login→bearer pattern from <see cref="AuthEndpointsTests"/>.
/// </summary>
public class HouseholdEndpointsTests : IClassFixture<AuthApiFactory>
{
    private const string Password = "P@ssw0rd!23";
    private readonly HttpClient _client;

    public HouseholdEndpointsTests(AuthApiFactory factory)
    {
        factory.EnsureDatabaseMigrated();
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

    private HttpRequestMessage Authed(HttpMethod method, string uri, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    [Fact]
    public async Task Create_household_makes_caller_admin_and_get_me_returns_it()
    {
        var token = await RegisterAndLoginAsync($"create-{Guid.NewGuid():N}@example.test");

        var create = await _client.SendAsync(Authed(HttpMethod.Post, "/api/households", token, new { name = "The Burrow" }));

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<HouseholdBody>();
        Assert.Equal("The Burrow", created!.Name);
        Assert.Equal("Admin", created.Role);
        Assert.NotEqual(Guid.Empty, created.Id);

        var me = await _client.SendAsync(Authed(HttpMethod.Get, "/api/households/me", token));

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var mine = await me.Content.ReadFromJsonAsync<HouseholdBody>();
        Assert.Equal(created.Id, mine!.Id);
        Assert.Equal("The Burrow", mine.Name);
        Assert.Equal("Admin", mine.Role);
    }

    [Fact]
    public async Task Second_create_for_same_user_returns_409()
    {
        var token = await RegisterAndLoginAsync($"second-{Guid.NewGuid():N}@example.test");

        (await _client.SendAsync(Authed(HttpMethod.Post, "/api/households", token, new { name = "First" })))
            .EnsureSuccessStatusCode();

        var second = await _client.SendAsync(Authed(HttpMethod.Post, "/api/households", token, new { name = "Second" }));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Blank_name_returns_400()
    {
        var token = await RegisterAndLoginAsync($"blank-{Guid.NewGuid():N}@example.test");

        var response = await _client.SendAsync(Authed(HttpMethod.Post, "/api/households", token, new { name = "   " }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Fresh_user_get_me_returns_204()
    {
        var token = await RegisterAndLoginAsync($"fresh-{Guid.NewGuid():N}@example.test");

        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/households/me", token));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_post_returns_401()
    {
        var response = await _client.PostAsJsonAsync("/api/households", new { name = "Nope" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_get_me_returns_401()
    {
        var response = await _client.GetAsync("/api/households/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAtUtc);

    private sealed record HouseholdBody(Guid Id, string Name, string Role);
}
