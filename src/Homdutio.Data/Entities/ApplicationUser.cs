using Microsoft.AspNetCore.Identity;

namespace Homdutio.Data.Entities;

/// <summary>
/// The application's Identity user. Empty for now — defined as a custom type so later slices can add
/// properties (e.g. the household membership link per FR-007, arriving with S-02) as plain additive
/// migrations, without swapping the generic user type across the context, DI, and migrations.
/// </summary>
public class ApplicationUser : IdentityUser
{
}
