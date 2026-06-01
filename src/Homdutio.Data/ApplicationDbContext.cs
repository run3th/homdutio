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

    public DbSet<HouseholdTask> HouseholdTasks => Set<HouseholdTask>();

    public DbSet<TaskEvent> TaskEvents => Set<TaskEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity needs its configuration applied first.
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(user =>
        {
            // Required so every account has a card-ready name; the migration backfills existing rows.
            user.Property(u => u.DisplayName).IsRequired().HasMaxLength(100);
        });

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

        builder.Entity<HouseholdTask>(task =>
        {
            task.Property(t => t.Title).IsRequired().HasMaxLength(200);
            task.Property(t => t.Category).HasMaxLength(100);

            // Stored as readable strings so rows stay legible and the enums can grow without a numeric remap.
            task.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);

            // The board query is scoped + ordered by household; this index backs it.
            task.HasIndex(t => t.HouseholdId);

            task.HasOne(t => t.Household)
                .WithMany()
                .HasForeignKey(t => t.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);

            // CreatedById/ClaimedById/ConfirmedById are raw AspNetUsers.Id columns with no navigation — a
            // reverse nav is unneeded, and mapping them as FKs would introduce multiple cascade paths
            // through AspNetUsers (which SQL Server rejects).
        });

        builder.Entity<TaskEvent>(taskEvent =>
        {
            taskEvent.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);

            taskEvent.HasOne(e => e.Task)
                .WithMany(t => t.Events)
                .HasForeignKey(e => e.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            // ActorId is a raw AspNetUsers.Id column with no navigation (see HouseholdTask above).
        });
    }
}
