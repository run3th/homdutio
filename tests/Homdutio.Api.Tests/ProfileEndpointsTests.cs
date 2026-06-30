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

    // ---- Avatar upload / serve / remove (Phase 3) --------------------------------------------------

    private static readonly byte[] FakePng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];

    private async Task<HttpResponseMessage> UploadAvatarAsync(string token, byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/profile/me/avatar") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Upload_avatar_stores_bytes_served_anonymously_with_etag_and_surfaced_by_me()
    {
        var token = await RegisterAndLoginAsync($"avatar-{Guid.NewGuid():N}@example.test");

        // No avatar yet → /me carries a null URL.
        Assert.Null((await GetMeAsync(token)).AvatarUrl);

        var upload = await UploadAvatarAsync(token, FakePng, "image/png");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var avatar = await upload.Content.ReadFromJsonAsync<AvatarBody>();
        Assert.False(string.IsNullOrEmpty(avatar!.AvatarUrl));

        // /me now carries the versioned URL.
        var me = await GetMeAsync(token);
        Assert.Equal(avatar.AvatarUrl, me.AvatarUrl);
        Assert.Contains("?v=1", me.AvatarUrl);

        // The bytes are served anonymously (no bearer) with the stored content-type + an ETag.
        var get = await _client.GetAsync(avatar.AvatarUrl);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("image/png", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal(FakePng, await get.Content.ReadAsByteArrayAsync());
        Assert.NotNull(get.Headers.ETag);

        // If-None-Match with the served ETag → 304 (the 4s poll re-uses the cached image).
        var conditional = new HttpRequestMessage(HttpMethod.Get, avatar.AvatarUrl);
        conditional.Headers.IfNoneMatch.Add(get.Headers.ETag!);
        var notModified = await _client.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
    }

    [Fact]
    public async Task Get_avatar_for_user_without_one_returns_404()
    {
        var token = await RegisterAndLoginAsync($"noavatar-{Guid.NewGuid():N}@example.test");
        var sub = (await GetMeAsync(token)).Sub;

        var get = await _client.GetAsync($"/api/users/{sub}/avatar");

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Upload_avatar_rejects_disallowed_content_type()
    {
        var token = await RegisterAndLoginAsync($"badtype-{Guid.NewGuid():N}@example.test");

        var response = await UploadAvatarAsync(token, FakePng, "text/plain");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_avatar_rejects_oversized_payload()
    {
        var token = await RegisterAndLoginAsync($"toobig-{Guid.NewGuid():N}@example.test");

        var tooBig = new byte[1_048_577]; // 1 MiB + 1 byte
        var response = await UploadAvatarAsync(token, tooBig, "image/png");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_avatar_clears_it_and_bumps_version()
    {
        var token = await RegisterAndLoginAsync($"delavatar-{Guid.NewGuid():N}@example.test");
        var sub = (await GetMeAsync(token)).Sub;

        await UploadAvatarAsync(token, FakePng, "image/png");

        var delete = new HttpRequestMessage(HttpMethod.Delete, "/api/profile/me/avatar");
        delete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var deleted = await _client.SendAsync(delete);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // Serving 404s again, and /me reports no avatar.
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/users/{sub}/avatar")).StatusCode);
        Assert.Null((await GetMeAsync(token)).AvatarUrl);
    }

    [Fact]
    public async Task Avatar_mutations_require_a_token()
    {
        var put = await UploadAnonymousAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);

        var delete = await _client.DeleteAsync("/api/profile/me/avatar");
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
    }

    private async Task<HttpResponseMessage> UploadAnonymousAsync()
    {
        var content = new ByteArrayContent(FakePng);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return await _client.PutAsync("/api/profile/me/avatar", content);
    }

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAtUtc, string RefreshToken);

    private sealed record AvatarBody(string? AvatarUrl);

    private sealed record MeBody(string? Sub, string? Email, string? DisplayName, string? AvatarUrl);

    private sealed record ProfileBody(string Id, string DisplayName, string? AvatarUrl);
}
