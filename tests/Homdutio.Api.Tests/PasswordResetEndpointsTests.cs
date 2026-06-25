using System.Net;
using System.Net.Http.Json;
using Homdutio.Api.Auth;
using Microsoft.AspNetCore.WebUtilities;

namespace Homdutio.Api.Tests;

/// <summary>
/// End-to-end coverage of the password-reset endpoints (S-08) through the real ASP.NET pipeline: the
/// always-200 anti-enumeration forgot-password, the reset-password happy path + failure modes, and
/// session revocation on success. A capturing fake <see cref="CapturingEmailSender"/> stands in for ACS
/// so the test can read the link/token the endpoint built and replay it.
/// </summary>
public class PasswordResetEndpointsTests : IClassFixture<AuthApiFactory>
{
    private const string Password = "P@ssw0rd!23";
    private const string NewPassword = "N3w!Passw0rd";

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public PasswordResetEndpointsTests(AuthApiFactory factory)
    {
        factory.EnsureDatabaseMigrated();
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string UniqueEmail() => $"reset-{Guid.NewGuid():N}@example.com";

    private async Task RegisterAsync(string email) =>
        (await _client.PostAsJsonAsync("/api/auth/register", new { email, password = Password }))
            .EnsureSuccessStatusCode();

    private Task<HttpResponseMessage> ForgotAsync(string email) =>
        _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });

    /// <summary>Pulls the email + (Base64Url-encoded) token out of the most recent captured reset link for a recipient.</summary>
    private (string Email, string Token) CapturedResetParams(string recipient)
    {
        var link = _factory.EmailSender.Sent.Last(s => s.Recipient == recipient).Link;
        var query = QueryHelpers.ParseQuery(new Uri(link).Query);
        return (query["email"].ToString(), query["token"].ToString());
    }

    [Fact] // 2.2
    public async Task ForgotPassword_IdenticalOk_ForKnownAndUnknownEmail()
    {
        var known = UniqueEmail();
        await RegisterAsync(known);

        var knownResponse = await ForgotAsync(known);
        var unknownResponse = await ForgotAsync(UniqueEmail());

        Assert.Equal(HttpStatusCode.OK, knownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknownResponse.StatusCode);
        Assert.Equal(
            await knownResponse.Content.ReadAsStringAsync(),
            await unknownResponse.Content.ReadAsStringAsync());
    }

    [Fact] // 2.3
    public async Task ForgotPassword_KnownEmail_CapturesLinkWithToken_UnknownDoesNot()
    {
        var known = UniqueEmail();
        await RegisterAsync(known);
        (await ForgotAsync(known)).EnsureSuccessStatusCode();

        var (capturedEmail, token) = CapturedResetParams(known);
        Assert.Equal(known, capturedEmail);
        Assert.False(string.IsNullOrWhiteSpace(token));

        var unknown = UniqueEmail();
        (await ForgotAsync(unknown)).EnsureSuccessStatusCode();
        Assert.DoesNotContain(_factory.EmailSender.Sent, s => s.Recipient == unknown);
    }

    [Fact] // 2.4
    public async Task ResetPassword_WithCapturedToken_Succeeds_AndNewPasswordLogsIn()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        (await ForgotAsync(email)).EnsureSuccessStatusCode();
        var (_, token) = CapturedResetParams(email);

        var reset = await _client.PostAsJsonAsync("/api/auth/reset-password",
            new { email, token, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var newLogin = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);

        var oldLogin = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
    }

    [Fact] // 2.5
    public async Task ResetPassword_InvalidToken_GenericFailure_IdenticalForUnknownUser()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);

        var knownUserBadToken = await _client.PostAsJsonAsync("/api/auth/reset-password",
            new { email, token = "not-a-real-token", newPassword = NewPassword });

        var unknownUser = await _client.PostAsJsonAsync("/api/auth/reset-password",
            new { email = UniqueEmail(), token = "not-a-real-token", newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.BadRequest, knownUserBadToken.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownUser.StatusCode);
        // Identical generic body → cannot tell "wrong token" from "no such account".
        Assert.Equal(
            await knownUserBadToken.Content.ReadAsStringAsync(),
            await unknownUser.Content.ReadAsStringAsync());
    }

    [Fact] // 2.6
    public async Task ResetPassword_WeakPassword_ReturnsValidationProblem()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        (await ForgotAsync(email)).EnsureSuccessStatusCode();
        var (_, token) = CapturedResetParams(email);

        var reset = await _client.PostAsJsonAsync("/api/auth/reset-password",
            new { email, token, newPassword = "weak" });

        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        var problem = await reset.Content.ReadFromJsonAsync<ValidationProblemBody>();
        Assert.NotNull(problem);
        // Surfaced as Identity password-policy codes, not the generic "token" failure.
        Assert.Contains(problem!.Errors.Keys, k => k.StartsWith("Password", StringComparison.Ordinal));
        Assert.DoesNotContain("token", problem.Errors.Keys);
    }

    [Fact] // 2.7
    public async Task ResetPassword_Success_RevokesExistingSessions()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();
        var refreshToken = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.RefreshToken;

        (await ForgotAsync(email)).EnsureSuccessStatusCode();
        var (_, token) = CapturedResetParams(email);
        (await _client.PostAsJsonAsync("/api/auth/reset-password",
            new { email, token, newPassword = NewPassword })).EnsureSuccessStatusCode();

        // The pre-reset refresh token must be rejected — every session was revoked.
        var refresh = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    private sealed record ValidationProblemBody(Dictionary<string, string[]> Errors);
}

/// <summary>Lowers the forgot-password rate limit so the 429 can be asserted deterministically.</summary>
public sealed class RateLimitedAuthApiFactory : AuthApiFactory
{
    protected override int ForgotPasswordPermitLimit => 3;
}

/// <summary>2.8 — forgot-password is rate-limited (429 after the configured threshold).</summary>
public class ForgotPasswordRateLimitTests : IClassFixture<RateLimitedAuthApiFactory>
{
    private readonly HttpClient _client;

    public ForgotPasswordRateLimitTests(RateLimitedAuthApiFactory factory)
    {
        factory.EnsureDatabaseMigrated();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ForgotPassword_RateLimited_AfterThreshold()
    {
        var email = $"rl-{Guid.NewGuid():N}@example.com";

        // PermitLimit = 3 per window: the first three pass, the fourth is rejected.
        for (var i = 0; i < 3; i++)
        {
            var ok = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var limited = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }
}
