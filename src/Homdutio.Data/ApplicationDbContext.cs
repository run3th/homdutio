using Homdutio.Data.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Homdutio.Data;

/// <summary>
/// The application's single EF Core DbContext. Hosts the ASP.NET Core Identity user store
/// (<see cref="ApplicationUser"/> and the <c>AspNet*</c> tables). Real domain entities (households,
/// tasks, the audit record) arrive with the slices that need them.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }
}
