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
            var user = new ApplicationUser { UserName = request.Email, Email = request.Email };
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
            JwtTokenService tokens) =>
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
            return Results.Ok(new LoginResponse(accessToken, expiresAtUtc));
        });

        group.MapGet("/me", (ClaimsPrincipal principal) =>
            Results.Ok(new MeResponse(
                principal.FindFirstValue("sub"),
                principal.FindFirstValue("email"))))
            .RequireAuthorization();

        return app;
    }
}

public sealed record RegisterRequest(string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc);

public sealed record MeResponse(string? Sub, string? Email);
