using Microsoft.AspNetCore.Identity;

namespace Homdutio.Data.Entities;

/// <summary>
/// The application's Identity user. Defined as a custom type so slices can add properties as plain
/// additive migrations, without swapping the generic user type across the context, DI, and migrations.
/// S-03 adds <see cref="DisplayName"/> so task cards read as human names, not raw emails.
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>
    /// A human-friendly name shown on task cards (S-03). Captured at registration; backfilled from the
    /// email local-part for blank/pre-existing rows. Required at the DB level.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// The user's profile photo bytes (S-09), already cropped + downscaled (~256²) client-side. Null when
    /// the user has no photo — every surface then falls back to the colored initial. Stored in SQL
    /// (varbinary(max)) rather than blob storage because <c>wwwroot</c> is wiped on deploy.
    /// </summary>
    public byte[]? AvatarData { get; set; }

    /// <summary>The stored photo's MIME type (<c>image/png</c> / <c>image/jpeg</c>); null when there is no photo.</summary>
    public string? AvatarContentType { get; set; }

    /// <summary>
    /// Monotonic cache-busting counter, bumped on every upload/remove. Avatar URLs are versioned
    /// (<c>/api/users/{id}/avatar?v={AvatarVersion}</c>) and served <c>immutable</c>, so a change yields a new
    /// URL and the browser (and the 4s board poll) re-use the cached bytes until the version moves. Defaults
    /// to 0 for existing rows.
    /// </summary>
    public int AvatarVersion { get; set; }
}
