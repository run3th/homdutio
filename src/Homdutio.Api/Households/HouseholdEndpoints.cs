using System.Security.Claims;
using System.Security.Cryptography;
using Homdutio.Data;
using Homdutio.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Homdutio.Api.Households;

/// <summary>
/// Household membership endpoints (S-02). The acting user comes from the JWT <c>sub</c> claim and the
/// household is derived server-side — neither endpoint accepts a client-supplied household id. This
/// establishes the cross-household isolation pattern (server-derived scope, never trust a client id) at
/// the first domain endpoint; S-07 generalises it into a systematic sweep.
/// </summary>
public static class HouseholdEndpoints
{
    public static IEndpointRouteBuilder MapHouseholdEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/households").RequireAuthorization();

        // GET /api/households/me — the caller's household, or 204 when they have none yet.
        group.MapGet("/me", async (ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var userId = principal.FindFirstValue("sub");

            var member = await db.HouseholdMembers
                .AsNoTracking()
                .Include(m => m.Household)
                .SingleOrDefaultAsync(m => m.UserId == userId);

            return member is null
                ? Results.NoContent()
                : Results.Ok(new HouseholdResponse(member.HouseholdId, member.Household!.Name, member.Role.ToString()));
        });

        // POST /api/households — create a household and make the caller its first admin.
        group.MapPost("/", async (CreateHouseholdRequest request, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Name"] = ["A household name is required."],
                });
            }

            var userId = principal.FindFirstValue("sub")!;

            var alreadyMember = await db.HouseholdMembers.AnyAsync(m => m.UserId == userId);
            if (alreadyMember)
            {
                return Results.Conflict(new { message = "You already belong to a household." });
            }

            var household = new Household
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
            };

            var member = new HouseholdMember
            {
                Id = Guid.NewGuid(),
                HouseholdId = household.Id,
                UserId = userId,
                Role = HouseholdRole.Admin,
                JoinedAtUtc = DateTime.UtcNow,
            };

            db.Households.Add(household);
            db.HouseholdMembers.Add(member);
            await db.SaveChangesAsync();

            return Results.Created(
                $"/api/households/me",
                new HouseholdResponse(household.Id, household.Name, member.Role.ToString()));
        });

        // POST /api/households/invites — any member (admin or adult member, FR-005) mints a single-use,
        // time-expiring invite link to their household.
        group.MapPost("/invites", async (ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var userId = principal.FindFirstValue("sub");

            var member = await db.HouseholdMembers
                .AsNoTracking()
                .SingleOrDefaultAsync(m => m.UserId == userId);
            if (member is null)
            {
                return Results.NotFound();
            }

            var now = DateTime.UtcNow;
            var invite = new HouseholdInvite
            {
                Id = Guid.NewGuid(),
                HouseholdId = member.HouseholdId,
                Token = NewInviteToken(),
                CreatedById = userId!,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.Add(InviteLifetime),
            };

            db.HouseholdInvites.Add(invite);
            await db.SaveChangesAsync();

            return Results.Created($"/api/households/invites/{invite.Token}", new InviteResponse(invite.Token, invite.ExpiresAtUtc));
        });

        // GET /api/households/invites/{token} — public preview so a recipient sees which household they're
        // joining before they have an account. Leaks only the household name (US-02).
        group.MapGet("/invites/{token}", async (string token, ApplicationDbContext db) =>
        {
            var invite = await db.HouseholdInvites
                .AsNoTracking()
                .Include(i => i.Household)
                .SingleOrDefaultAsync(i => i.Token == token);

            if (invite is null)
            {
                return Results.NotFound();
            }

            if (invite.ConsumedAtUtc is not null || invite.ExpiresAtUtc <= DateTime.UtcNow)
            {
                return Results.StatusCode(StatusCodes.Status410Gone);
            }

            return Results.Ok(new InvitePreviewResponse(invite.Household!.Name));
        })
        .AllowAnonymous();

        // POST /api/households/invites/{token}/accept — consume the invite and add the caller as a Member
        // (FR-006), exactly once (FR-005) and never violating one-household-per-user (FR-007).
        group.MapPost("/invites/{token}/accept", async (string token, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var userId = principal.FindFirstValue("sub")!;

            var invite = await db.HouseholdInvites.SingleOrDefaultAsync(i => i.Token == token);
            if (invite is null)
            {
                return Results.NotFound();
            }

            if (invite.ConsumedAtUtc is not null || invite.ExpiresAtUtc <= DateTime.UtcNow)
            {
                return Results.StatusCode(StatusCodes.Status410Gone);
            }

            var alreadyMember = await db.HouseholdMembers.AnyAsync(m => m.UserId == userId);
            if (alreadyMember)
            {
                return Results.Conflict(new { message = "You already belong to a household." });
            }

            var now = DateTime.UtcNow;
            invite.ConsumedAtUtc = now;
            invite.ConsumedById = userId;
            db.HouseholdMembers.Add(new HouseholdMember
            {
                Id = Guid.NewGuid(),
                HouseholdId = invite.HouseholdId,
                UserId = userId,
                Role = HouseholdRole.Member,
                JoinedAtUtc = now,
            });

            try
            {
                // One atomic SaveChanges: the invite's rowversion makes consume single-use (a concurrent
                // second accept fails the version check → 410); the UserId unique index guards FR-007 (a
                // double-accept by the same user fails the insert → 409).
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.StatusCode(StatusCodes.Status410Gone);
            }
            catch (DbUpdateException)
            {
                return Results.Conflict(new { message = "You already belong to a household." });
            }

            var household = await db.Households.AsNoTracking().SingleAsync(h => h.Id == invite.HouseholdId);
            return Results.Ok(new HouseholdResponse(household.Id, household.Name, HouseholdRole.Member.ToString()));
        });

        // GET /api/households/members — the caller's household roster (any member may read it). Each row
        // carries server-computed isSelf/canManage flags so the SPA renders controls from flags, never by
        // re-deriving authorization (mirrors the affordance flags on TaskResponse, S-09).
        group.MapGet("/members", async (ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await ResolveCallerAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            var callerIsAdmin = caller.Role == HouseholdRole.Admin;
            var rows = await db.HouseholdMembers
                .AsNoTracking()
                .Where(m => m.HouseholdId == caller.HouseholdId)
                .Join(
                    db.Users.AsNoTracking(),
                    m => m.UserId,
                    u => u.Id,
                    (m, u) => new { m.UserId, u.DisplayName, u.Email, m.Role })
                .ToListAsync();

            var members = rows
                .OrderBy(r => r.Role) // Admin (0) before Member (1).
                .ThenBy(r => r.DisplayName)
                .Select(r => new MemberResponse(
                    r.UserId,
                    r.DisplayName,
                    r.Email ?? string.Empty,
                    r.Role.ToString(),
                    r.UserId == caller.UserId,
                    callerIsAdmin && r.UserId != caller.UserId))
                .ToList();

            return Results.Ok(members);
        });

        // POST /api/households/members/{userId}/role — promote/demote a member (FR-008). Admin-only; the
        // target is scoped to the caller's household (foreign/unknown → 404, no existence leak). An admin
        // cannot change their own role, and the last admin cannot be demoted (would orphan the household).
        group.MapPost("/members/{userId}/role", async (string userId, UpdateMemberRoleRequest request, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await ResolveCallerAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            if (caller.Role != HouseholdRole.Admin)
            {
                return Results.Forbid();
            }

            if (!Enum.TryParse<HouseholdRole>(request.Role, out var newRole))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Role"] = ["Role must be 'Admin' or 'Member'."],
                });
            }

            if (userId == caller.UserId)
            {
                return Results.Conflict(new { message = "You cannot change your own role." });
            }

            var target = await db.HouseholdMembers
                .SingleOrDefaultAsync(m => m.UserId == userId && m.HouseholdId == caller.HouseholdId);
            if (target is null)
            {
                return Results.NotFound();
            }

            // Idempotent: setting the role the member already has is a no-op success.
            if (target.Role != newRole)
            {
                if (target.Role == HouseholdRole.Admin && await IsLastAdminAsync(db, caller.HouseholdId))
                {
                    return Results.Conflict(new { message = "The household must keep at least one admin." });
                }

                target.Role = newRole;
                await db.SaveChangesAsync();
            }

            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
            return Results.Ok(new MemberResponse(
                target.UserId, user.DisplayName, user.Email ?? string.Empty, target.Role.ToString(), IsSelf: false, CanManage: true));
        });

        // DELETE /api/households/members/{userId} — remove a member (FR-009). Admin-only; target scoped to
        // the caller's household. An admin cannot remove themselves, and the last admin cannot be removed.
        // The removed member's in-progress tasks are swept back to To do unassigned in the SAME SaveChanges
        // as the membership delete, so no task is ever left claimed by a non-member.
        group.MapDelete("/members/{userId}", async (string userId, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await ResolveCallerAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            if (caller.Role != HouseholdRole.Admin)
            {
                return Results.Forbid();
            }

            if (userId == caller.UserId)
            {
                return Results.Conflict(new { message = "You cannot remove yourself." });
            }

            var target = await db.HouseholdMembers
                .SingleOrDefaultAsync(m => m.UserId == userId && m.HouseholdId == caller.HouseholdId);
            if (target is null)
            {
                return Results.NotFound();
            }

            if (target.Role == HouseholdRole.Admin && await IsLastAdminAsync(db, caller.HouseholdId))
            {
                return Results.Conflict(new { message = "The household must keep at least one admin." });
            }

            // Reuse the S-05 unclaim shape: status→ToDo, clear claim, append to the To-do bottom, one
            // Unclaimed event each. The admin effecting the removal is the actor on those events.
            var inProgress = await db.HouseholdTasks
                .Where(t => t.HouseholdId == caller.HouseholdId
                    && t.ClaimedById == userId
                    && t.Status == HouseholdTaskStatus.InProgress)
                .ToListAsync();

            if (inProgress.Count > 0)
            {
                var nextSortOrder = await NextToDoSortOrderAsync(db, caller.HouseholdId);
                var now = DateTime.UtcNow;
                foreach (var task in inProgress)
                {
                    task.Status = HouseholdTaskStatus.ToDo;
                    task.ClaimedById = null;
                    task.ClaimedAtUtc = null;
                    task.SortOrder = nextSortOrder++;
                    db.TaskEvents.Add(new TaskEvent
                    {
                        Id = Guid.NewGuid(),
                        TaskId = task.Id,
                        Type = TaskEventType.Unclaimed,
                        ActorId = caller.UserId,
                        OccurredAtUtc = now,
                    });
                }
            }

            db.HouseholdMembers.Remove(target);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });

        return app;
    }

    /// <summary>
    /// Resolves the caller's household membership from the JWT <c>sub</c> claim. Null when they have no
    /// membership — surfaced by callers as 404, consistent with the foreign-household rule.
    /// </summary>
    private static async Task<CallerContext?> ResolveCallerAsync(ClaimsPrincipal principal, ApplicationDbContext db)
    {
        var userId = principal.FindFirstValue("sub");
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        var member = await db.HouseholdMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.UserId == userId);

        return member is null ? null : new CallerContext(member.HouseholdId, member.Role, userId);
    }

    /// <summary>True when the household has exactly one admin — the last-admin guard for demote/remove (FR-008/009).</summary>
    private static async Task<bool> IsLastAdminAsync(ApplicationDbContext db, Guid householdId) =>
        await db.HouseholdMembers.CountAsync(m => m.HouseholdId == householdId && m.Role == HouseholdRole.Admin) <= 1;

    /// <summary>The next To-do <c>SortOrder</c> for a household — <c>max+1</c>, or 0 when To do is empty. The
    /// removal sweep increments from here so each freed task lands at the column bottom in order.</summary>
    private static async Task<int> NextToDoSortOrderAsync(ApplicationDbContext db, Guid householdId)
    {
        var max = await db.HouseholdTasks
            .Where(t => t.HouseholdId == householdId && t.Status == HouseholdTaskStatus.ToDo)
            .Select(t => (int?)t.SortOrder)
            .MaxAsync();

        return (max ?? -1) + 1;
    }

    private sealed record CallerContext(Guid HouseholdId, HouseholdRole Role, string UserId);

    /// <summary>An invite is valid for this window after creation (FR-005 time-expiry bound).</summary>
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    /// <summary>A 256-bit cryptographically-random, URL-safe (hex) token — unguessable and path-safe.</summary>
    private static string NewInviteToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}

public sealed record CreateHouseholdRequest(string Name);

public sealed record HouseholdResponse(Guid Id, string Name, string Role);

public sealed record InviteResponse(string Token, DateTime ExpiresAtUtc);

public sealed record InvitePreviewResponse(string HouseholdName);

public sealed record MemberResponse(string UserId, string DisplayName, string Email, string Role, bool IsSelf, bool CanManage);

public sealed record UpdateMemberRoleRequest(string Role);
