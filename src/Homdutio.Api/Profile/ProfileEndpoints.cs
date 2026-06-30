using System.Security.Claims;
using Homdutio.Data;
using Microsoft.EntityFrameworkCore;

namespace Homdutio.Api.Profile;

/// <summary>
/// Self-service profile endpoints (S-09). The acting user comes from the JWT <c>sub</c> claim — never a
/// client-supplied id. Changing the display name propagates everywhere for free: names are resolved at
/// fetch time from the user record (no denormalized copies), so existing cards/comments pick up the new
/// name on their next fetch. Avatar upload/serve/delete arrives in Phase 3.
/// </summary>
public static class ProfileEndpoints
{
    /// <summary>Upper bound on a stored display name — generous for human names, bounds the column + UI.</summary>
    public const int MaxDisplayNameLength = 60;

    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/profile").RequireAuthorization();

        // PUT /api/profile/me — change the caller's display name.
        group.MapPut("/me", async (UpdateProfileRequest request, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var displayName = request.DisplayName?.Trim() ?? string.Empty;
            if (displayName.Length == 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["displayName"] = ["A display name is required."],
                });
            }

            if (displayName.Length > MaxDisplayNameLength)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["displayName"] = [$"A display name must be {MaxDisplayNameLength} characters or fewer."],
                });
            }

            var userId = principal.FindFirstValue("sub");
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId);
            if (user is null)
            {
                // A valid token whose user no longer exists — treat as unauthenticated.
                return Results.Unauthorized();
            }

            user.DisplayName = displayName;
            await db.SaveChangesAsync();

            // AvatarUrl is null until Phase 3 wires avatar storage.
            return Results.Ok(new ProfileResponse(user.Id, user.DisplayName, null));
        });

        return app;
    }
}

public sealed record UpdateProfileRequest(string? DisplayName);

public sealed record ProfileResponse(string Id, string DisplayName, string? AvatarUrl);
