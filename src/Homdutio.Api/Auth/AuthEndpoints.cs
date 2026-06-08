using System.Security.Claims;
using Homdutio.Data.Entities;
using Microsoft.AspNetCore.Identity;

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

        group.MapGet("/me", (ClaimsPrincipal principal) =>
            Results.Ok(new MeResponse(
                principal.FindFirstValue("sub"),
                principal.FindFirstValue("email"))))
            .RequireAuthorization();

        return app;
    }

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

public sealed record MeResponse(string? Sub, string? Email);
