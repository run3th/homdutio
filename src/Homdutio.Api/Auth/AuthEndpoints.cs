using System.Security.Claims;
using System.Text;
using Homdutio.Api.Email;
using Homdutio.Api.Users;
using Homdutio.Data;
using Homdutio.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Homdutio.Api.Auth;

/// <summary>
/// The auth plumbing endpoints (F-02). Register + login issue/validate JWTs; the protected /me probe
/// exercises the validation middleware. User-facing UI, validation polish, and logout UX belong to S-01.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/register", async (RegisterRequest request, UserManager<ApplicationUser> users) =>
        {
            // DisplayName powers task cards (S-03). Falls back to the email local-part when left blank,
            // so every account has a card-ready name from day one.
            var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? LocalPartOf(request.Email)
                : request.DisplayName.Trim();

            var user = new ApplicationUser { UserName = request.Email, Email = request.Email, DisplayName = displayName };
            var result = await users.CreateAsync(user, request.Password);

            return result.Succeeded
                ? Results.Ok()
                : Results.ValidationProblem(
                    result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
        });

        group.MapPost("/login", async (
            LoginRequest request,
            UserManager<ApplicationUser> users,
            SignInManager<ApplicationUser> signIn,
            JwtTokenService tokens,
            RefreshTokenService refreshTokens) =>
        {
            var user = await users.FindByEmailAsync(request.Email);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                return Results.Unauthorized();
            }

            var (accessToken, expiresAtUtc) = tokens.CreateAccessToken(user);
            // New family per login: independent sessions across devices (logout of one leaves the rest).
            var refreshToken = await refreshTokens.IssueAsync(user.Id);
            return Results.Ok(new LoginResponse(accessToken, expiresAtUtc, refreshToken.RawToken));
        });

        group.MapPost("/refresh", async (
            RefreshRequest request,
            JwtTokenService tokens,
            RefreshTokenService refreshTokens,
            UserManager<ApplicationUser> users) =>
        {
            // Anonymous: the access token may already be expired, so this path cannot require a bearer.
            var rotation = await refreshTokens.ValidateAndRotateAsync(request.RefreshToken);
            if (rotation.Outcome != RefreshOutcome.Success)
            {
                // Expired/Revoked/NotFound, and Replay (the service already revoked the family) all → 401.
                return Results.Unauthorized();
            }

            var user = await users.FindByIdAsync(rotation.UserId!);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var (accessToken, expiresAtUtc) = tokens.CreateAccessToken(user);
            return Results.Ok(new LoginResponse(accessToken, expiresAtUtc, rotation.RawToken!));
        });

        group.MapPost("/logout", async (RefreshRequest request, RefreshTokenService refreshTokens) =>
        {
            // Idempotent + existence-safe: revoke the family if the token is known, always return 200 so a
            // copied/expired/unknown token reveals nothing. No bearer required (expired access token must
            // still be able to log out).
            await refreshTokens.RevokeFamilyAsync(request.RefreshToken);
            return Results.Ok();
        });

        // The header/menu and Settings prefill need the real display name + avatar, which are no longer
        // derivable from claims alone (a rename/new photo would leave the token stale) — so read them from
        // the user record. Selecting AvatarData != null (not the bytes) keeps the probe cheap.
        group.MapGet("/me", async (ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var userId = principal.FindFirstValue("sub");
            var user = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.DisplayName, HasAvatar = u.AvatarData != null, u.AvatarVersion })
                .SingleOrDefaultAsync();

            return Results.Ok(new MeResponse(
                userId,
                principal.FindFirstValue("email"),
                user?.DisplayName,
                user is null ? null : UserAvatarEndpoints.BuildUrl(userId!, user.HasAvatar, user.AvatarVersion)));
        })
            .RequireAuthorization();

        // Forgotten-password recovery (S-08). Only an existing account gets a token + email; the response
        // is the same generic 200 regardless of account existence or send outcome (anti-enumeration).
        group.MapPost("/forgot-password", async (
            ForgotPasswordRequest request,
            UserManager<ApplicationUser> users,
            IEmailSender emailSender,
            IConfiguration configuration,
            ILoggerFactory loggerFactory) =>
        {
            var user = await users.FindByEmailAsync(request.Email);
            if (user is not null)
            {
                var token = await users.GeneratePasswordResetTokenAsync(user);
                // Identity's token is not URL-safe — Base64Url-encode it for the query string, and
                // URL-encode the email. reset-password reverses both before ResetPasswordAsync.
                var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
                var baseUrl = (configuration["AppBaseUrl"] ?? string.Empty).TrimEnd('/');
                var link = $"{baseUrl}/reset-password?email={Uri.EscapeDataString(request.Email)}&token={encodedToken}";

                var sent = await emailSender.SendPasswordResetAsync(request.Email, link);
                if (!sent)
                {
                    // Logged, never surfaced — a failed send must not become an enumeration signal.
                    loggerFactory.CreateLogger("Homdutio.Api.Auth.PasswordReset")
                        .LogError("Password-reset email send failed.");
                }
            }

            return Results.Ok(new ForgotPasswordResponse(
                "If an account exists for that email, a password reset link has been sent."));
        })
        .RequireRateLimiting(RateLimitPolicies.ForgotPassword);

        // Sets a new password from the emailed link. One generic failure for both unknown-user and
        // invalid/expired token (anti-enumeration); password-policy errors are surfaced so the user can
        // fix them. On success every active session for the account is revoked.
        group.MapPost("/reset-password", async (
            ResetPasswordRequest request,
            UserManager<ApplicationUser> users,
            RefreshTokenService refreshTokens) =>
        {
            string token;
            try
            {
                token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token));
            }
            catch (FormatException)
            {
                return InvalidResetLink();
            }

            var user = await users.FindByEmailAsync(request.Email);
            if (user is null)
            {
                return InvalidResetLink();
            }

            var result = await users.ResetPasswordAsync(user, token, request.NewPassword);
            if (result.Succeeded)
            {
                await refreshTokens.RevokeAllForUserAsync(user.Id);
                return Results.Ok();
            }

            // Invalid/expired token → generic failure; weak-password (policy) errors → surfaced.
            // Depends on Identity's stable "InvalidToken" error code (IdentityErrorDescriber). If that
            // code ever changed, a bad token would fall through to ValidationProblem and surface
            // token-validity detail — never account existence — so the fallback stays enumeration-safe.
            if (result.Errors.Any(e => e.Code == "InvalidToken"))
            {
                return InvalidResetLink();
            }

            return Results.ValidationProblem(
                result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
        });

        return app;
    }

    /// <summary>The single generic reset failure — identical for unknown-user and invalid/expired token, so neither leaks account existence.</summary>
    private static IResult InvalidResetLink() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["token"] = ["This password reset link is invalid or has expired."],
        });

    /// <summary>The portion of an email before the <c>@</c> — the display-name fallback. Returns the input unchanged when there is no <c>@</c>.</summary>
    private static string LocalPartOf(string email)
    {
        if (string.IsNullOrEmpty(email))
        {
            return string.Empty;
        }

        var at = email.IndexOf('@');
        return at <= 0 ? email : email[..at];
    }
}

public sealed record RegisterRequest(string Email, string Password, string? DisplayName = null);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc, string RefreshToken);

public sealed record MeResponse(string? Sub, string? Email, string? DisplayName, string? AvatarUrl);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ForgotPasswordResponse(string Message);

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

/// <summary>Named rate-limiting policies, shared between registration (Program.cs) and endpoint mapping.</summary>
public static class RateLimitPolicies
{
    public const string ForgotPassword = "forgot-password";

    /// <summary>Caps invite minting per caller — the with-email path is an outbound-email vector (email
    /// bombing / ACS quota), so it gets the same treatment as forgot-password but partitioned by user.</summary>
    public const string Invite = "invite";
}

/// <summary>
/// Forgot-password rate-limit settings bound from <c>RateLimiting:ForgotPassword</c> (non-secret,
/// committed in appsettings.json). Read via <c>IOptions</c> inside the policy so config overrides
/// (e.g. the test host) apply.
/// </summary>
public sealed class ForgotPasswordRateLimitOptions
{
    public const string SectionName = "RateLimiting:ForgotPassword";

    public int PermitLimit { get; set; } = 5;

    public int WindowSeconds { get; set; } = 900;
}

/// <summary>
/// Invite rate-limit settings bound from <c>RateLimiting:Invite</c> (non-secret, committed in
/// appsettings.json). Read via <c>IOptions</c> inside the policy so config overrides (e.g. the test host)
/// apply. Forgiving for humans (generating + emailing a few invites), but bounds an authenticated caller
/// from blasting invite mail at arbitrary addresses.
/// </summary>
public sealed class InviteRateLimitOptions
{
    public const string SectionName = "RateLimiting:Invite";

    public int PermitLimit { get; set; } = 10;

    public int WindowSeconds { get; set; } = 900;
}
