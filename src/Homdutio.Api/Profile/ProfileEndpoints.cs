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

            // The declared content-type is attacker-controlled; verify the bytes actually start with the
            // matching image signature so an HTML/script payload can't be stored under an image MIME type.
            var bytes = buffer.ToArray();
            if (!HasMatchingImageSignature(bytes, contentType))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["avatar"] = ["The file is not a valid PNG or JPEG image."],
                });
            }

            var userId = principal.FindFirstValue("sub");

            // Store the bytes and bump the version in one UPDATE so the increment can't lose a concurrent
            // write (the version is the cache-busting signal — it must advance on every write). Re-read the
            // resulting version to build the fresh URL.
            var affected = await db.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.AvatarData, bytes)
                    .SetProperty(u => u.AvatarContentType, contentType)
                    .SetProperty(u => u.AvatarVersion, u => u.AvatarVersion + 1));
            if (affected == 0)
            {
                return Results.Unauthorized();
            }

            var version = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.AvatarVersion)
                .SingleAsync();

            return Results.Ok(new AvatarResponse(UserAvatarEndpoints.BuildUrl(userId!, hasAvatar: true, version)));
        });

        // DELETE /api/profile/me/avatar — clear the caller's photo, bumping the version so any cached image
        // is abandoned. Idempotent: removing an already-absent photo still 204s (and still bumps).
        group.MapDelete("/me/avatar", async (ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var userId = principal.FindFirstValue("sub");

            // Clear the photo and bump the version atomically (same reason as upload — the version must
            // advance even when racing another write so cached bytes are abandoned).
            var affected = await db.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.AvatarData, (byte[]?)null)
                    .SetProperty(u => u.AvatarContentType, (string?)null)
                    .SetProperty(u => u.AvatarVersion, u => u.AvatarVersion + 1));
            if (affected == 0)
            {
                return Results.Unauthorized();
            }

            return Results.NoContent();
        });

        return app;
    }

    /// <summary>The caller's versioned avatar URL, or null when they have no photo.</summary>
    private static string? AvatarUrlOf(Data.Entities.ApplicationUser user) =>
        UserAvatarEndpoints.BuildUrl(user.Id, user.AvatarData != null, user.AvatarVersion);

    /// <summary>
    /// True when <paramref name="bytes"/> begins with the magic-byte signature of the declared
    /// <paramref name="contentType"/> (PNG or JPEG). Guards against a mislabeled (e.g. HTML/SVG) payload
    /// declared as an image — the bytes are later served verbatim from an anonymous origin URL.
    /// </summary>
    private static bool HasMatchingImageSignature(byte[] bytes, string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            // 89 50 4E 47 0D 0A 1A 0A
            "image/png" => bytes.Length >= 8 &&
                bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A,
            // FF D8 FF
            "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            _ => false,
        };

    private static IResult AvatarTooLarge() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["avatar"] = [$"The image must be {MaxAvatarBytes / 1024} KB or smaller."],
        });
}

public sealed record UpdateProfileRequest(string? DisplayName);

public sealed record ProfileResponse(string Id, string DisplayName, string? AvatarUrl);

public sealed record AvatarResponse(string? AvatarUrl);
