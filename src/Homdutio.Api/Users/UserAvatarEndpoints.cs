using Homdutio.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Homdutio.Api.Users;

/// <summary>
/// Serves user profile photos (S-09). The route is <see cref="MapUserAvatarEndpoints">AllowAnonymous</see>
/// because a bare <c>&lt;img src&gt;</c> never carries the bearer token — mirroring the public invite
/// preview. URLs are versioned (<c>?v={version}</c>) and served <c>immutable</c>, so the browser and the 4s
/// board poll re-use cached bytes until an upload/remove bumps the version (yielding a fresh URL). This
/// type also owns {@link BuildUrl}, the single place every DTO builds an avatar URL.
/// </summary>
public static class UserAvatarEndpoints
{
    /// <summary>
    /// The versioned avatar URL for a user, or <c>null</c> when they have no photo (the client then renders
    /// the colored initial). The single source of truth for the URL shape — every DTO calls this so the
    /// shape can never drift between surfaces.
    /// </summary>
    public static string? BuildUrl(string userId, bool hasAvatar, int version) =>
        hasAvatar ? $"/api/users/{userId}/avatar?v={version}" : null;

    public static IEndpointRouteBuilder MapUserAvatarEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/users/{id}/avatar — the stored bytes with an ETag from the version + a long immutable
        // cache. 404 when the user has no photo (the component falls back to the initial). Conditional
        // requests (If-None-Match) are handled by the file result → 304.
        app.MapGet("/api/users/{id}/avatar", async (string id, HttpResponse response, ApplicationDbContext db) =>
        {
            var avatar = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == id && u.AvatarData != null)
                .Select(u => new { u.AvatarData, u.AvatarContentType, u.AvatarVersion })
                .SingleOrDefaultAsync();

            if (avatar is null)
            {
                return Results.NotFound();
            }

            // Versioned URL → the bytes at this URL never change; cache hard. A new upload bumps the
            // version, so the next DTO carries a new URL and busts the cache.
            response.Headers.CacheControl = "public, max-age=31536000, immutable";

            return Results.File(
                avatar.AvatarData!,
                contentType: avatar.AvatarContentType ?? "application/octet-stream",
                entityTag: new EntityTagHeaderValue($"\"{avatar.AvatarVersion}\""));
        })
        .AllowAnonymous();

        return app;
    }
}

/// <summary>
/// A resolved user reference for a DTO: their current display name plus their versioned avatar URL (null
/// when they have no photo). Both are resolved at fetch time, so a rename or a new photo propagates to
/// every surface on the next fetch with no denormalized copies to update.
/// </summary>
public sealed record UserRef(string DisplayName, string? AvatarUrl);
