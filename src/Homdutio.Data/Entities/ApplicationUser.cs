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
}
