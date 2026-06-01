using System.Security.Claims;
using Homdutio.Data;
using Homdutio.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Homdutio.Api.Tasks;

/// <summary>
/// The household task lifecycle endpoints (S-03) — the product's north star. Every endpoint derives the
/// caller's household server-side from the JWT <c>sub</c> claim (never a client-supplied id), guards the
/// current state + actor eligibility, appends one <see cref="TaskEvent"/> per transition in the *same*
/// <c>SaveChanges</c> as the projection mutation (so the audit log can never diverge from current state),
/// and returns DTOs carrying server-computed affordance flags so the SPA stays dumb about authorization.
/// Closure is <see cref="HouseholdTask.ClosedAtUtc"/> being set at confirm — a closed task simply stops
/// coming back from <c>GET /api/tasks</c>; it is never deleted. A foreign or missing task id resolves as
/// 404 (no existence leak), establishing the US-02/FR-019 pattern S-07 later verifies systematically.
/// </summary>
public static class TaskEndpoints
{
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tasks").RequireAuthorization();

        // GET /api/tasks — the caller's open board: every non-closed task in their household.
        group.MapGet("/", async (ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await ResolveMemberAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            var tasks = await db.HouseholdTasks
                .AsNoTracking()
                .Where(t => t.HouseholdId == caller.HouseholdId && t.ClosedAtUtc == null)
                .OrderBy(t => t.Status)
                .ThenBy(t => t.CreatedAtUtc)
                .ToListAsync();

            var names = await ResolveNamesAsync(db, tasks);

            return Results.Ok(tasks.Select(t => ToResponse(t, caller, names)).ToList());
        });

        // POST /api/tasks — create a task in the caller's household; it lands unassigned in To do.
        group.MapPost("/", async (CreateTaskRequest request, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await ResolveMemberAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Title"] = ["A task title is required."],
                });
            }

            var now = DateTime.UtcNow;
            var task = new HouseholdTask
            {
                Id = Guid.NewGuid(),
                HouseholdId = caller.HouseholdId,
                Title = request.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim(),
                Status = HouseholdTaskStatus.ToDo,
                CreatedById = caller.UserId,
                CreatedAtUtc = now,
            };

            db.HouseholdTasks.Add(task);
            db.TaskEvents.Add(NewEvent(task.Id, TaskEventType.Created, caller.UserId, now));
            await db.SaveChangesAsync();

            var names = await ResolveNamesAsync(db, [task]);
            return Results.Created($"/api/tasks/{task.Id}", ToResponse(task, caller, names));
        });

        // POST /api/tasks/{id}/claim — a To-do task → In progress, carrying the claimer.
        group.MapPost("/{id:guid}/claim", async (Guid id, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await ResolveMemberAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            var task = await LoadScopedTaskAsync(db, id, caller.HouseholdId);
            if (task is null)
            {
                return Results.NotFound();
            }

            if (task.Status != HouseholdTaskStatus.ToDo)
            {
                return Results.Conflict(new { message = "This task can no longer be claimed." });
            }

            var now = DateTime.UtcNow;
            task.ClaimedById = caller.UserId;
            task.ClaimedAtUtc = now;
            task.Status = HouseholdTaskStatus.InProgress;
            db.TaskEvents.Add(NewEvent(task.Id, TaskEventType.Claimed, caller.UserId, now));
            await db.SaveChangesAsync();

            var names = await ResolveNamesAsync(db, [task]);
            return Results.Ok(ToResponse(task, caller, names));
        });

        // POST /api/tasks/{id}/done — the claimer marks their In-progress task Done.
        group.MapPost("/{id:guid}/done", async (Guid id, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await ResolveMemberAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            var task = await LoadScopedTaskAsync(db, id, caller.HouseholdId);
            if (task is null)
            {
                return Results.NotFound();
            }

            if (task.Status != HouseholdTaskStatus.InProgress)
            {
                return Results.Conflict(new { message = "Only an in-progress task can be marked done." });
            }

            if (task.ClaimedById != caller.UserId)
            {
                return Results.Forbid();
            }

            var now = DateTime.UtcNow;
            task.DoneAtUtc = now;
            task.Status = HouseholdTaskStatus.Done;
            db.TaskEvents.Add(NewEvent(task.Id, TaskEventType.MarkedDone, caller.UserId, now));
            await db.SaveChangesAsync();

            var names = await ResolveNamesAsync(db, [task]);
            return Results.Ok(ToResponse(task, caller, names));
        });

        // POST /api/tasks/{id}/confirm — an admin confirms a Done task, closing it off the board.
        group.MapPost("/{id:guid}/confirm", async (Guid id, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await ResolveMemberAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            var task = await LoadScopedTaskAsync(db, id, caller.HouseholdId);
            if (task is null)
            {
                return Results.NotFound();
            }

            if (caller.Role != HouseholdRole.Admin)
            {
                return Results.Forbid();
            }

            if (task.Status != HouseholdTaskStatus.Done)
            {
                return Results.Conflict(new { message = "Only a task that is done can be confirmed." });
            }

            var now = DateTime.UtcNow;
            var selfAttested = task.ClaimedById == caller.UserId;
            task.ConfirmedById = caller.UserId;
            task.ClosedAtUtc = now;
            task.SelfAttested = selfAttested;

            var confirmed = NewEvent(task.Id, TaskEventType.Confirmed, caller.UserId, now);
            confirmed.SelfAttested = selfAttested;
            db.TaskEvents.Add(confirmed);
            await db.SaveChangesAsync();

            var names = await ResolveNamesAsync(db, [task]);
            return Results.Ok(ToResponse(task, caller, names));
        });

        return app;
    }

    /// <summary>
    /// Resolves the caller's household membership from the JWT <c>sub</c>. Null when they have no
    /// membership — surfaced by callers as 404 (no board), consistent with the foreign-household rule.
    /// </summary>
    private static async Task<CallerContext?> ResolveMemberAsync(ClaimsPrincipal principal, ApplicationDbContext db)
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

    /// <summary>Loads a task scoped to the caller's household — foreign or missing id → null (no existence leak).</summary>
    private static Task<HouseholdTask?> LoadScopedTaskAsync(ApplicationDbContext db, Guid id, Guid householdId) =>
        db.HouseholdTasks.SingleOrDefaultAsync(t => t.Id == id && t.HouseholdId == householdId);

    /// <summary>Resolves the display names referenced by a set of tasks (creator + claimer) in one query — no N+1.</summary>
    private static async Task<Dictionary<string, string>> ResolveNamesAsync(
        ApplicationDbContext db, IReadOnlyCollection<HouseholdTask> tasks)
    {
        var ids = tasks.Select(t => t.CreatedById)
            .Concat(tasks.Where(t => t.ClaimedById != null).Select(t => t.ClaimedById!))
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        return await db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);
    }

    private static TaskEvent NewEvent(Guid taskId, TaskEventType type, string actorId, DateTime occurredAtUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            Type = type,
            ActorId = actorId,
            OccurredAtUtc = occurredAtUtc,
        };

    private static TaskResponse ToResponse(HouseholdTask task, CallerContext caller, IReadOnlyDictionary<string, string> names)
    {
        var canClaim = task.Status == HouseholdTaskStatus.ToDo;
        var canMarkDone = task.Status == HouseholdTaskStatus.InProgress && task.ClaimedById == caller.UserId;
        var canConfirm = caller.Role == HouseholdRole.Admin && task.Status == HouseholdTaskStatus.Done;
        var willSelfAttest = canConfirm && task.ClaimedById == caller.UserId;

        return new TaskResponse(
            task.Id,
            task.Title,
            task.Description,
            task.Category,
            task.Status.ToString(),
            names.TryGetValue(task.CreatedById, out var creator) ? creator : string.Empty,
            task.ClaimedById is not null && names.TryGetValue(task.ClaimedById, out var claimer) ? claimer : null,
            task.CreatedAtUtc,
            canClaim,
            canMarkDone,
            canConfirm,
            willSelfAttest);
    }

    private sealed record CallerContext(Guid HouseholdId, HouseholdRole Role, string UserId);
}

public sealed record CreateTaskRequest(string Title, string? Description, string? Category);

public sealed record TaskResponse(
    Guid Id,
    string Title,
    string? Description,
    string? Category,
    string Status,
    string CreatedByName,
    string? ClaimerName,
    DateTime CreatedAtUtc,
    bool CanClaim,
    bool CanMarkDone,
    bool CanConfirm,
    bool WillSelfAttest);
