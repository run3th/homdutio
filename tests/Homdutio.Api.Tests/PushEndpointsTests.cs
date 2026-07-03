using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Homdutio.Api.Tests;

/// <summary>
/// Drives the sub-scoped Web Push registry (<c>/api/push</c>) end-to-end through the real middleware:
/// subscribe persists one row per endpoint and upserts on re-subscribe; the device list and delete are
/// own-only (a caller never sees or removes another user's subscriptions); every route requires a bearer
/// token; and <c>GET /key</c> reports disabled when no VAPID key is configured (the test host's default —
/// the no-op sender is registered). The route-coverage exemption for these routes is locked by
/// <see cref="RouteIsolationCoverageTests"/>.
/// </summary>
public class PushEndpointsTests : IClassFixture<AuthApiFactory>
{
    private const string Password = "P@ssw0rd!23";
    private readonly HttpClient _client;

    public PushEndpointsTests(AuthApiFactory factory)
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

    private static object SubscribePayload(string endpoint, string? label = null) => new
    {
        endpoint,
        keys = new { p256dh = "BAsHwdd7testp256dhkey", auth = "authSecret123" },
        deviceLabel = label,
    };

    private async Task<HttpResponseMessage> SubscribeAsync(string token, string endpoint, string? label = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/push/subscribe")
        {
            Content = JsonContent.Create(SubscribePayload(endpoint, label)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> DeleteAsync(string token, string endpoint)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/push/subscribe")
        {
            Content = JsonContent.Create(new { endpoint }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<List<DeviceBody>> GetDevicesAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/push/devices");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<DeviceBody>>())!;
    }

    [Fact]
    public async Task Subscribe_persists_a_device_for_the_caller()
    {
        var token = await RegisterAndLoginAsync($"push-{Guid.NewGuid():N}@example.test");
        var endpoint = $"https://push.example.test/{Guid.NewGuid():N}";

        var response = await SubscribeAsync(token, endpoint, "Rafal's laptop");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var devices = await GetDevicesAsync(token);
        var device = Assert.Single(devices);
        Assert.Equal(endpoint, device.Endpoint);
        Assert.Equal("Rafal's laptop", device.Label);
    }

    [Fact]
    public async Task Subscribe_same_endpoint_upserts_without_duplicating()
    {
        var token = await RegisterAndLoginAsync($"push-{Guid.NewGuid():N}@example.test");
        var endpoint = $"https://push.example.test/{Guid.NewGuid():N}";

        await SubscribeAsync(token, endpoint, "First label");
        await SubscribeAsync(token, endpoint, "Renamed device");

        var devices = await GetDevicesAsync(token);
        var device = Assert.Single(devices);
        Assert.Equal("Renamed device", device.Label);
    }

    [Fact]
    public async Task Devices_list_returns_only_the_callers_rows()
    {
        var alice = await RegisterAndLoginAsync($"alice-{Guid.NewGuid():N}@example.test");
        var bob = await RegisterAndLoginAsync($"bob-{Guid.NewGuid():N}@example.test");
        var aliceEndpoint = $"https://push.example.test/{Guid.NewGuid():N}";
        var bobEndpoint = $"https://push.example.test/{Guid.NewGuid():N}";

        await SubscribeAsync(alice, aliceEndpoint, "Alice device");
        await SubscribeAsync(bob, bobEndpoint, "Bob device");

        var aliceDevices = await GetDevicesAsync(alice);
        var aliceDevice = Assert.Single(aliceDevices);
        Assert.Equal(aliceEndpoint, aliceDevice.Endpoint);
        Assert.DoesNotContain(aliceDevices, d => d.Endpoint == bobEndpoint);
    }

    [Fact]
    public async Task Delete_removes_only_the_callers_subscription()
    {
        var alice = await RegisterAndLoginAsync($"alice-{Guid.NewGuid():N}@example.test");
        var bob = await RegisterAndLoginAsync($"bob-{Guid.NewGuid():N}@example.test");
        var aliceEndpoint = $"https://push.example.test/{Guid.NewGuid():N}";

        await SubscribeAsync(alice, aliceEndpoint, "Alice device");

        // Bob cannot delete Alice's subscription (own-only) — still idempotent 204, Alice's row survives.
        var bobDelete = await DeleteAsync(bob, aliceEndpoint);
        Assert.Equal(HttpStatusCode.NoContent, bobDelete.StatusCode);
        Assert.Single(await GetDevicesAsync(alice));

        // Alice removes her own — it's gone.
        var aliceDelete = await DeleteAsync(alice, aliceEndpoint);
        Assert.Equal(HttpStatusCode.NoContent, aliceDelete.StatusCode);
        Assert.Empty(await GetDevicesAsync(alice));
    }

    [Fact]
    public async Task Key_reports_disabled_when_no_vapid_key_configured()
    {
        var token = await RegisterAndLoginAsync($"push-{Guid.NewGuid():N}@example.test");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/push/key");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync<KeyBody>())!;
        Assert.False(body.Enabled);
        Assert.Null(body.PublicKey);
    }

    [Fact]
    public async Task Push_routes_require_a_token()
    {
        var endpoint = $"https://push.example.test/{Guid.NewGuid():N}";

        var subscribe = await _client.PostAsJsonAsync("/api/push/subscribe", SubscribePayload(endpoint));
        Assert.Equal(HttpStatusCode.Unauthorized, subscribe.StatusCode);

        var devices = await _client.GetAsync("/api/push/devices");
        Assert.Equal(HttpStatusCode.Unauthorized, devices.StatusCode);

        var key = await _client.GetAsync("/api/push/key");
        Assert.Equal(HttpStatusCode.Unauthorized, key.StatusCode);
    }

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAtUtc, string RefreshToken);

    private sealed record DeviceBody(Guid Id, string? Label, string Endpoint, DateTime CreatedAtUtc);

    private sealed record KeyBody(string? PublicKey, bool Enabled);
}
