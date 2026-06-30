using System.Security.Claims;
using Homdutio.Api.Users;
using Homdutio.Data;
using Microsoft.EntityFrameworkCore;

namespace Homdutio.Api.Profile;

/// <summary>
/// Self-service profile endpoints (S-09). The acting user comes from the JWT <c>sub</c> claim — never a
/// client-supplied id. Changing the display name propagates everywhere for free: names are resolved at
/// fetch time from the user record (no denormalized copies), so existing cards/comments pick up the new
/// name on their next fetch. The profile photo is stored as SQL bytes + a version counter and served by the
/// anonymous <see cref="UserAvatarEndpoints"/>.
/// </summary>
public static class ProfileEndpoints
{
    /// <summary>Upper bound on a stored display name — generous for human names, bounds the column + UI.</summary>
    public const int MaxDisplayNameLength = 60;

    /// <summary>
    /// Max accepted avatar payload. The client crops + downscales to ~256² before upload, so this is a
    /// generous defense-in-depth cap, not the expected size.
    /// </summary>
    public const int MaxAvatarBytes = 1_048_576; // 1 MiB

    private static readonly HashSet<string> AllowedAvatarContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg" };

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

            return Results.Ok(new ProfileResponse(user.Id, user.DisplayName, AvatarUrlOf(user)));
        });

        // PUT /api/profile/me/avatar — store the caller's (already client-cropped/resized) photo. The raw
        // image is the request body; the content-type header declares its MIME type. Bumps the version so
        // the new URL busts caches.
        group.MapPut("/me/avatar", async (HttpRequest request, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var contentType = request.ContentType?.Split(';')[0].Trim() ?? string.Empty;
            if (!AllowedAvatarContentTypes.Contains(contentType))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["avatar"] = ["Only PNG or JPEG images are allowed."],
                });
            }

            // Reject an oversized declared length up front, then read with a hard cap so a chunked/unknown-
            // length body can't slip past the ContentLength check.
            if (request.ContentLength is > MaxAvatarBytes)
            {
                return AvatarTooLarge();
            }

            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer);
            if (buffer.Length == 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["avatar"] = ["The image is empty."],
                });
            }

            if (buffer.Length > MaxAvatarBytes)
            {
                return AvatarTooLarge();
            }

            var userId = principal.FindFirstValue("sub");
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            user.AvatarData = buffer.ToArray();
            user.AvatarContentType = contentType;
            user.AvatarVersion++;
            await db.SaveChangesAsync();

            return Results.Ok(new AvatarResponse(AvatarUrlOf(user)));
        });

        // DELETE /api/profile/me/avatar — clear the caller's photo, bumping the version so any cached image
        // is abandoned. Idempotent: removing an already-absent photo still 204s (and still bumps).
        group.MapDelete("/me/avatar", async (ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var userId = principal.FindFirstValue("sub");
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            user.AvatarData = null;
            user.AvatarContentType = null;
            user.AvatarVersion++;
            await db.SaveChangesAsync();

            return Results.NoContent();
        });

        return app;
    }

    /// <summary>The caller's versioned avatar URL, or null when they have no photo.</summary>
    private static string? AvatarUrlOf(Data.Entities.ApplicationUser user) =>
        UserAvatarEndpoints.BuildUrl(user.Id, user.AvatarData != null, user.AvatarVersion);

    private static IResult AvatarTooLarge() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["avatar"] = [$"The image must be {MaxAvatarBytes / 1024} KB or smaller."],
        });
}

public sealed record UpdateProfileRequest(string? DisplayName);

public sealed record ProfileResponse(string Id, string DisplayName, string? AvatarUrl);

public sealed record AvatarResponse(string? AvatarUrl);
