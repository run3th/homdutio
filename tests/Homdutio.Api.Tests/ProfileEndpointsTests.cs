using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Homdutio.Api.Tests;

/// <summary>
/// Drives the self-service profile surface (S-09) end-to-end through the real middleware:
/// PUT /api/profile/me updates the caller's display name (and /api/auth/me reflects it on the next read),
/// blank/too-long names are rejected with a 400 ValidationProblem, and the route requires a bearer token.
/// </summary>
public class ProfileEndpointsTests : IClassFixture<AuthApiFactory>
{
    private const string Password = "P@ssw0rd!23";
    private readonly HttpClient _client;

    public ProfileEndpointsTests(AuthApiFactory factory)
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

    private async Task<HttpResponseMessage> UpdateProfileAsync(string token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/profile/me") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<MeBody> GetMeAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MeBody>())!;
    }

    [Fact]
    public async Task Update_display_name_persists_and_is_reflected_by_me()
    {
        var token = await RegisterAndLoginAsync($"profile-{Guid.NewGuid():N}@example.test");

        var response = await UpdateProfileAsync(token, new { displayName = "  Molly Weasley  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<ProfileBody>();
        Assert.Equal("Molly Weasley", profile!.DisplayName); // trimmed
        Assert.Null(profile.AvatarUrl);

        var me = await GetMeAsync(token);
        Assert.Equal("Molly Weasley", me.DisplayName);
    }

    [Fact]
    public async Task Update_with_blank_name_returns_400()
    {
        var token = await RegisterAndLoginAsync($"blank-{Guid.NewGuid():N}@example.test");

        var response = await UpdateProfileAsync(token, new { displayName = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_with_too_long_name_returns_400()
    {
        var token = await RegisterAndLoginAsync($"toolong-{Guid.NewGuid():N}@example.test");

        var response = await UpdateProfileAsync(token, new { displayName = new string('x', 61) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_without_token_returns_401()
    {
        var response = await _client.PutAsJsonAsync("/api/profile/me", new { displayName = "No Auth" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAtUtc, string RefreshToken);

    private sealed record MeBody(string? Sub, string? Email, string? DisplayName, string? AvatarUrl);

    private sealed record ProfileBody(string Id, string DisplayName, string? AvatarUrl);
}
