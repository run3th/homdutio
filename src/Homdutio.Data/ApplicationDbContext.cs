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

    public DbSet<TaskComment> TaskComments => Set<TaskComment>();

    public DbSet<TaskTag> TaskTags => Set<TaskTag>();

    public DbSet<HouseholdInvite> HouseholdInvites => Set<HouseholdInvite>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity needs its configuration applied first.
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(user =>
        {
            // Required so every account has a card-ready name; the migration backfills existing rows.
            user.Property(u => u.DisplayName).IsRequired().HasMaxLength(100);

            // Avatar (S-09): bytes are nullable varbinary(max); content-type is a short MIME string.
            // AvatarVersion defaults to 0 so existing rows (added by the migration) start un-versioned.
            user.Property(u => u.AvatarContentType).HasMaxLength(100);
            user.Property(u => u.AvatarVersion).HasDefaultValue(0);
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

        builder.Entity<TaskComment>(comment =>
        {
            comment.Property(c => c.Body).IsRequired().HasMaxLength(280);

            // Stored as a readable string (matching Status/Type) so the enum can grow without a numeric remap.
            comment.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);

            // Backs both the per-task thread query and the grouped commentCount on the board.
            comment.HasIndex(c => c.TaskId);

            comment.HasOne(c => c.Task)
                .WithMany()
                .HasForeignKey(c => c.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            // AuthorId is a raw AspNetUsers.Id column with no navigation (see HouseholdTask above).
        });

        builder.Entity<TaskTag>(tag =>
        {
            tag.Property(t => t.Value).IsRequired().HasMaxLength(50);

            // Backs the per-task tag fetch / wholesale rewrite on edit.
            tag.HasIndex(t => t.TaskId);

            // Backs the per-household suggestion query (DISTINCT Value WHERE HouseholdId == …, incl. closed tasks).
            tag.HasIndex(t => new { t.HouseholdId, t.Value });

            tag.HasOne(t => t.Task)
                .WithMany(t => t.Tags)
                .HasForeignKey(t => t.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            // HouseholdId is denormalized (copied from the task), not a second FK — a task already cascades
            // from its household, and a second cascade path through Households would be rejected by SQL Server.
        });

        builder.Entity<HouseholdInvite>(invite =>
        {
            invite.Property(i => i.Token).IsRequired().HasMaxLength(64);

            // The token lookup (preview/accept) hits this; unique so a token maps to exactly one invite.
            invite.HasIndex(i => i.Token).IsUnique();

            // The optimistic-concurrency guard that makes consume single-use (FR-005): a concurrent second
            // accept fails the version check rather than creating a second membership.
            invite.Property(i => i.RowVersion).IsRowVersion();

            invite.HasOne(i => i.Household)
                .WithMany()
                .HasForeignKey(i => i.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);

            // CreatedById/ConsumedById are raw AspNetUsers.Id columns with no navigation (see HouseholdTask).
        });

        builder.Entity<RefreshToken>(refreshToken =>
        {
            refreshToken.Property(r => r.TokenHash).IsRequired().HasMaxLength(64);

            // The hot path is the by-hash lookup on every refresh; unique so a hash maps to exactly one row.
            refreshToken.HasIndex(r => r.TokenHash).IsUnique();

            // Replay/logout revoke an entire rotation chain at once — this index backs the family sweep.
            refreshToken.HasIndex(r => r.FamilyId);

            // Per-user queries and the eventual expired-row cleanup job (see plan Performance Considerations).
            refreshToken.HasIndex(r => r.UserId);

            // Optimistic-concurrency guard that makes consume single-winner under a rotation race (S-10).
            refreshToken.Property(r => r.RowVersion).IsRowVersion();

            // UserId is a raw AspNetUsers.Id column with no navigation (see HouseholdTask above).
        });

        builder.Entity<PushSubscription>(subscription =>
        {
            // The push service URL is the subscription's identity; unique so re-subscribing one browser
            // upserts its row rather than duplicating it. Capped at 512 so the nvarchar key stays inside
            // SQL Server's 1700-byte unique-index limit (real endpoints are a few hundred chars).
            subscription.Property(s => s.Endpoint).IsRequired().HasMaxLength(512);
            subscription.HasIndex(s => s.Endpoint).IsUnique();

            // Per-user fan-out on send + the Settings device-list read.
            subscription.HasIndex(s => s.UserId);

            subscription.Property(s => s.P256dh).IsRequired().HasMaxLength(256);
            subscription.Property(s => s.Auth).IsRequired().HasMaxLength(256);
            subscription.Property(s => s.DeviceLabel).HasMaxLength(200);

            // UserId is a raw AspNetUsers.Id column with no navigation (see HouseholdTask above).
        });
    }
}
