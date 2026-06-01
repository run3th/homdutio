using Homdutio.Data.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Homdutio.Data;

/// <summary>
/// The application's single EF Core DbContext. Hosts the ASP.NET Core Identity user store
/// (<see cref="ApplicationUser"/> and the <c>AspNet*</c> tables) and, from S-02, the household domain
/// (<see cref="Household"/> + <see cref="HouseholdMember"/>). Later domain entities (tasks, the audit
/// record) arrive with the slices that need them.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Household> Households => Set<Household>();

    public DbSet<HouseholdMember> HouseholdMembers => Set<HouseholdMember>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity needs its configuration applied first.
        base.OnModelCreating(builder);

        builder.Entity<Household>(household =>
        {
            household.Property(h => h.Name).IsRequired().HasMaxLength(100);
        });

        builder.Entity<HouseholdMember>(member =>
        {
            // One household per user is the v1 enforcement of FR-007 — a DB invariant, not app logic.
            // Doubles as the index backing the GET /api/households/me lookup.
            member.HasIndex(m => m.UserId).IsUnique();

            // Role is stored as a readable string so S-09 can promote/demote without a numeric remap.
            member.Property(m => m.Role).HasConversion<string>().HasMaxLength(20);

            member.HasOne(m => m.Household)
                .WithMany(h => h.Members)
                .HasForeignKey(m => m.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);

            member.HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
