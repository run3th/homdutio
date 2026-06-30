using System.Security.Claims;
using Homdutio.Api.Users;
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
            var caller = await HouseholdScope.ResolveCallerAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            var tasks = await db.HouseholdTasks
                .AsNoTracking()
                .Where(t => t.HouseholdId == caller.HouseholdId && t.ClosedAtUtc == null)
                .OrderBy(t => t.Status)
                .ThenBy(t => t.SortOrder)
                .ThenBy(t => t.CreatedAtUtc)
                .ToListAsync();

            var names = await ResolveNamesAsync(db, tasks);
            var counts = await CountCommentsAsync(db, tasks);
            var tags = await ResolveTagsAsync(db, tasks);

            return Results.Ok(tasks.Select(t => ToResponse(t, caller, names, counts, tags)).ToList());
        });

        // GET /api/tasks/tags — distinct tag values used anywhere in the caller's household (INCLUDING closed
        // tasks — deliberately not the board's ClosedAtUtc == null filter), alphabetical, for the create/edit
        // chip-input autocomplete. Household-scoped server-side (S-07); registered in ScopedRouteInventory as
        // an own-only collection or RouteIsolationCoverageTests fails. A literal segment, so it never collides
        // with the {id:guid} lifecycle routes.
        group.MapGet("/tags", async (ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await HouseholdScope.ResolveCallerAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            var tags = await db.TaskTags
                .AsNoTracking()
                .Where(t => t.HouseholdId == caller.HouseholdId)
                .Select(t => t.Value)
                .Distinct()
                .OrderBy(v => v)
                .ToListAsync();

            return Results.Ok(tags);
        });

        // POST /api/tasks — create a task in the caller's household; it lands unassigned in To do.
        group.MapPost("/", async (CreateTaskRequest request, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await HouseholdScope.ResolveCallerAsync(principal, db);
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

            var normalized = TagNormalization.Normalize(request.Tags);
            if (!normalized.IsValid)
            {
                return Results.ValidationProblem(normalized.Errors!);
            }

            var now = DateTime.UtcNow;
            var task = new HouseholdTask
            {
                Id = Guid.NewGuid(),
                HouseholdId = caller.HouseholdId,
                Title = request.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                Status = HouseholdTaskStatus.ToDo,
                // Land at the bottom of "To do" so a manual order is never scrambled by a new task (FR-021).
                SortOrder = await NextSortOrderAsync(db, caller.HouseholdId, HouseholdTaskStatus.ToDo),
                CreatedById = caller.UserId,
                CreatedAtUtc = now,
            };

            db.HouseholdTasks.Add(task);
            db.TaskEvents.Add(NewEvent(task.Id, TaskEventType.Created, caller.UserId, now));
            // HouseholdId is copied onto each tag so the suggestion query never needs a join (incl. closed tasks).
            db.TaskTags.AddRange(normalized.Tags.Select(value => new TaskTag
            {
                Id = Guid.NewGuid(),
                TaskId = task.Id,
                HouseholdId = caller.HouseholdId,
                Value = value,
            }));
            await db.SaveChangesAsync();

            var names = await ResolveNamesAsync(db, [task]);
            var tags = await ResolveTagsAsync(db, [task]);
            // A brand-new task has no comments yet; an empty count map renders commentCount = 0.
            return Results.Created($"/api/tasks/{task.Id}", ToResponse(task, caller, names, new Dictionary<Guid, int>(), tags));
        });

        // POST /api/tasks/{id}/claim — a To-do task → In progress, carrying the claimer.
        group.MapPost("/{id:guid}/claim", async (Guid id, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await HouseholdScope.ResolveCallerAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            var task = await HouseholdScope.LoadScopedTaskAsync(db, id, caller.HouseholdId);
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
            // Append to the bottom of the destination column so the task joins its new neighbours in order.
            task.SortOrder = await NextSortOrderAsync(db, caller.HouseholdId, HouseholdTaskStatus.InProgress);
            db.TaskEvents.Add(NewEvent(task.Id, TaskEventType.Claimed, caller.UserId, now));
            await db.SaveChangesAsync();

            var names = await ResolveNamesAsync(db, [task]);
            var counts = await CountCommentsAsync(db, [task]);
            var tags = await ResolveTagsAsync(db, [task]);
            return Results.Ok(ToResponse(task, caller, names, counts, tags));
        });

        // POST /api/tasks/{id}/done — the claimer marks their In-progress task Done.
        group.MapPost("/{id:guid}/done", async (Guid id, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await HouseholdScope.ResolveCallerAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            var task = await HouseholdScope.LoadScopedTaskAsync(db, id, caller.HouseholdId);
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
            // Append to the bottom of the destination column so the task joins its new neighbours in order.
            task.SortOrder = await NextSortOrderAsync(db, caller.HouseholdId, HouseholdTaskStatus.Done);
            db.TaskEvents.Add(NewEvent(task.Id, TaskEventType.MarkedDone, caller.UserId, now));
            await db.SaveChangesAsync();

            var names = await ResolveNamesAsync(db, [task]);
            var counts = await CountCommentsAsync(db, [task]);
            var tags = await ResolveTagsAsync(db, [task]);
            return Results.Ok(ToResponse(task, caller, names, counts, tags));
        });

        // POST /api/tasks/{id}/confirm — an admin confirms a Done task, closing it off the board.
        group.MapPost("/{id:guid}/confirm", async (Guid id, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await HouseholdScope.ResolveCallerAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            var task = await HouseholdScope.LoadScopedTaskAsync(db, id, caller.HouseholdId);
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
            var counts = await CountCommentsAsync(db, [task]);
            var tags = await ResolveTagsAsync(db, [task]);
            return Results.Ok(ToResponse(task, caller, names, counts, tags));
        });

        // POST /api/tasks/{id}/unclaim — return an in-progress task to To do, unassigned (FR-022, S-05).
        // The claimer frees a task they can't finish, or any admin frees one whose claimer has gone absent.
        group.MapPost("/{id:guid}/unclaim", async (Guid id, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await HouseholdScope.ResolveCallerAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            var task = await HouseholdScope.LoadScopedTaskAsync(db, id, caller.HouseholdId);
            if (task is null)
            {
                return Results.NotFound();
            }

            if (task.Status != HouseholdTaskStatus.InProgress)
            {
                return Results.Conflict(new { message = "Only an in-progress task can be unclaimed." });
            }

            // Usable by the claimer (freeing their own task) or any admin (freeing an absent member's).
            if (task.ClaimedById != caller.UserId && caller.Role != HouseholdRole.Admin)
            {
                return Results.Forbid();
            }

            var now = DateTime.UtcNow;
            task.Status = HouseholdTaskStatus.ToDo;
            task.ClaimedById = null;
            task.ClaimedAtUtc = null;
            // Append to the bottom of To do so the freed task joins its new neighbours in order.
            task.SortOrder = await NextSortOrderAsync(db, caller.HouseholdId, HouseholdTaskStatus.ToDo);
            db.TaskEvents.Add(NewEvent(task.Id, TaskEventType.Unclaimed, caller.UserId, now));
            await db.SaveChangesAsync();

            var names = await ResolveNamesAsync(db, [task]);
            var counts = await CountCommentsAsync(db, [task]);
            var tags = await ResolveTagsAsync(db, [task]);
            return Results.Ok(ToResponse(task, caller, names, counts, tags));
        });

        // POST /api/tasks/{id}/sendback — an admin returns a Done task to In progress with a required reason
        // (FR-023, S-05). The original claimer stays attached; the reason enters the comment thread atomically.
        group.MapPost("/{id:guid}/sendback", async (Guid id, SendBackRequest request, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await HouseholdScope.ResolveCallerAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            var task = await HouseholdScope.LoadScopedTaskAsync(db, id, caller.HouseholdId);
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
                return Results.Conflict(new { message = "Only a task that is done can be sent back." });
            }

            var comment = request.Comment?.Trim();
            if (string.IsNullOrWhiteSpace(comment) || comment.Length > 280)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Comment"] = ["A send-back reason must be between 1 and 280 characters."],
                });
            }

            var now = DateTime.UtcNow;
            task.Status = HouseholdTaskStatus.InProgress;
            task.DoneAtUtc = null; // Keep ClaimedById — the original claimer remains attached (FR-023).
            // Append to the bottom of In progress so the returned task joins its new neighbours in order.
            task.SortOrder = await NextSortOrderAsync(db, caller.HouseholdId, HouseholdTaskStatus.InProgress);
            // The SentBack event and its reason comment land in the SAME SaveChanges as the status flip, so the
            // thread can never show a reason for a transition that didn't persist.
            db.TaskEvents.Add(NewEvent(task.Id, TaskEventType.SentBack, caller.UserId, now));
            db.TaskComments.Add(new TaskComment
            {
                Id = Guid.NewGuid(),
                TaskId = task.Id,
                AuthorId = caller.UserId,
                Body = comment,
                Kind = TaskCommentKind.SendBack,
                CreatedAtUtc = now,
            });
            await db.SaveChangesAsync();

            var names = await ResolveNamesAsync(db, [task]);
            var counts = await CountCommentsAsync(db, [task]);
            var tags = await ResolveTagsAsync(db, [task]);
            return Results.Ok(ToResponse(task, caller, names, counts, tags));
        });

        // PUT /api/tasks/{id} — edit a task's title/description/category; admin-only, any column (FR-011, S-05).
        group.MapPut("/{id:guid}", async (Guid id, UpdateTaskRequest request, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await HouseholdScope.ResolveCallerAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            var task = await HouseholdScope.LoadScopedTaskAsync(db, id, caller.HouseholdId);
            if (task is null)
            {
                return Results.NotFound();
            }

            // Editing is admin-only as of S-05 (members comment instead); an admin may edit in any column.
            if (caller.Role != HouseholdRole.Admin)
            {
                return Results.Forbid();
            }

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Title"] = ["A task title is required."],
                });
            }

            var normalized = TagNormalization.Normalize(request.Tags);
            if (!normalized.IsValid)
            {
                return Results.ValidationProblem(normalized.Errors!);
            }

            task.Title = request.Title.Trim();
            task.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

            // Tags are rewritten wholesale (delete-all + re-insert) — simpler than a per-tag diff and matches the
            // append-only spirit of the child rows; the suggestion index is small so the churn is negligible.
            var existingTags = await db.TaskTags.Where(t => t.TaskId == task.Id).ToListAsync();
            db.TaskTags.RemoveRange(existingTags);
            db.TaskTags.AddRange(normalized.Tags.Select(value => new TaskTag
            {
                Id = Guid.NewGuid(),
                TaskId = task.Id,
                HouseholdId = task.HouseholdId,
                Value = value,
            }));
            await db.SaveChangesAsync(); // Management action — no TaskEvent (the log stays the lifecycle record).

            var names = await ResolveNamesAsync(db, [task]);
            var counts = await CountCommentsAsync(db, [task]);
            var tags = await ResolveTagsAsync(db, [task]);
            return Results.Ok(ToResponse(task, caller, names, counts, tags));
        });

        // DELETE /api/tasks/{id} — remove a mistaken/obsolete task while it is still un-claimed (FR-012).
        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await HouseholdScope.ResolveCallerAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            var task = await HouseholdScope.LoadScopedTaskAsync(db, id, caller.HouseholdId);
            if (task is null)
            {
                return Results.NotFound();
            }

            if (task.Status != HouseholdTaskStatus.ToDo)
            {
                return Results.Conflict(new { message = "Only a task that is still to do can be deleted." });
            }

            // Hard delete: a deletable task is never-claimed, so its lone Created event has no honest record to
            // preserve (NFR-3 protects *closed*-task audit). The event cascades away via the FK.
            db.HouseholdTasks.Remove(task);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });

        // PUT /api/tasks/order — persist a new within-column order from a drag, shared across the household (FR-021).
        group.MapPut("/order", async (ReorderRequest request, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await HouseholdScope.ResolveCallerAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            if (!Enum.TryParse<HouseholdTaskStatus>(request.Status, out var status))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Status"] = ["Unknown task status."],
                });
            }

            var orderedIds = request.OrderedIds ?? [];
            var tasks = await db.HouseholdTasks
                .Where(t => t.HouseholdId == caller.HouseholdId && orderedIds.Contains(t.Id))
                .ToListAsync();

            // Every supplied id must resolve to one of the caller's tasks *and* be in the requested column —
            // a foreign, unknown, or wrong-status id rejects the whole request (no partial reindex, no leak).
            if (tasks.Count != orderedIds.Length || tasks.Any(t => t.Status != status))
            {
                return Results.NotFound();
            }

            var positions = orderedIds
                .Select((taskId, index) => (taskId, index))
                .ToDictionary(pair => pair.taskId, pair => pair.index);
            foreach (var task in tasks)
            {
                task.SortOrder = positions[task.Id];
            }

            await db.SaveChangesAsync(); // One atomic reindex; the client refetches. No TaskEvent.
            return Results.NoContent();
        });

        // POST /api/tasks/{id}/comments — any household member posts an immutable comment on a task (S-05).
        group.MapPost("/{id:guid}/comments", async (Guid id, CreateCommentRequest request, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await HouseholdScope.ResolveCallerAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            var task = await HouseholdScope.LoadScopedTaskAsync(db, id, caller.HouseholdId);
            if (task is null)
            {
                return Results.NotFound();
            }

            var body = request.Body?.Trim();
            if (string.IsNullOrWhiteSpace(body) || body.Length > 280)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Body"] = ["A comment must be between 1 and 280 characters."],
                });
            }

            var now = DateTime.UtcNow;
            var comment = new TaskComment
            {
                Id = Guid.NewGuid(),
                TaskId = task.Id,
                AuthorId = caller.UserId,
                Body = body,
                Kind = TaskCommentKind.Member,
                CreatedAtUtc = now,
            };
            db.TaskComments.Add(comment);
            await db.SaveChangesAsync();

            var names = await ResolveCommentNamesAsync(db, [comment]);
            return Results.Created($"/api/tasks/{task.Id}/comments/{comment.Id}", ToCommentResponse(comment, names));
        });

        // GET /api/tasks/{id}/comments — the task's full thread, oldest first, with author display names.
        group.MapGet("/{id:guid}/comments", async (Guid id, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var caller = await HouseholdScope.ResolveCallerAsync(principal, db);
            if (caller is null)
            {
                return Results.NotFound();
            }

            var task = await HouseholdScope.LoadScopedTaskAsync(db, id, caller.HouseholdId);
            if (task is null)
            {
                return Results.NotFound();
            }

            var comments = await db.TaskComments
                .AsNoTracking()
                .Where(c => c.TaskId == task.Id)
                .OrderBy(c => c.CreatedAtUtc)
                .ToListAsync();

            var names = await ResolveCommentNamesAsync(db, comments);
            return Results.Ok(comments.Select(c => ToCommentResponse(c, names)).ToList());
        });

        return app;
    }

    /// <summary>
    /// The next <see cref="HouseholdTask.SortOrder"/> for a column — <c>max(SortOrder)+1</c> among the
    /// household's tasks in that status, or 0 when the column is empty. Keeps a task at the bottom of the
    /// column it enters (create / claim / done) so the manual order is never scrambled by a transition.
    /// </summary>
    private static async Task<int> NextSortOrderAsync(ApplicationDbContext db, Guid householdId, HouseholdTaskStatus status)
    {
        var max = await db.HouseholdTasks
            .Where(t => t.HouseholdId == householdId && t.Status == status)
            .Select(t => (int?)t.SortOrder)
            .MaxAsync();

        return (max ?? -1) + 1;
    }

    /// <summary>
    /// Resolves the display name + versioned avatar URL referenced by a set of tasks (creator + claimer) in
    /// one query — no N+1. Selecting <c>AvatarData != null</c> (not the bytes) keeps the projection cheap.
    /// </summary>
    private static async Task<Dictionary<string, UserRef>> ResolveNamesAsync(
        ApplicationDbContext db, IReadOnlyCollection<HouseholdTask> tasks)
    {
        var ids = tasks.Select(t => t.CreatedById)
            .Concat(tasks.Where(t => t.ClaimedById != null).Select(t => t.ClaimedById!))
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<string, UserRef>();
        }

        return await ResolveUserRefsAsync(db, ids);
    }

    /// <summary>Grouped comment count per task in one query (no N+1) — backs the board's per-card 💬 badge.</summary>
    private static async Task<Dictionary<Guid, int>> CountCommentsAsync(
        ApplicationDbContext db, IReadOnlyCollection<HouseholdTask> tasks)
    {
        var ids = tasks.Select(t => t.Id).ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        return await db.TaskComments
            .AsNoTracking()
            .Where(c => ids.Contains(c.TaskId))
            .GroupBy(c => c.TaskId)
            .Select(g => new { TaskId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TaskId, x => x.Count);
    }

    /// <summary>
    /// The tags per task in one query (no N+1), each task's set ordered alphabetically so the board renders
    /// deterministically. Backs the card chip row and the modal's tag field.
    /// </summary>
    private static async Task<Dictionary<Guid, string[]>> ResolveTagsAsync(
        ApplicationDbContext db, IReadOnlyCollection<HouseholdTask> tasks)
    {
        var ids = tasks.Select(t => t.Id).ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string[]>();
        }

        var rows = await db.TaskTags
            .AsNoTracking()
            .Where(t => ids.Contains(t.TaskId))
            .Select(t => new { t.TaskId, t.Value })
            .ToListAsync();

        return rows
            .GroupBy(r => r.TaskId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => r.Value).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    /// <summary>Resolves the display name + avatar URL of a set of comments' authors in one query — no N+1.</summary>
    private static async Task<Dictionary<string, UserRef>> ResolveCommentNamesAsync(
        ApplicationDbContext db, IReadOnlyCollection<TaskComment> comments)
    {
        var ids = comments.Select(c => c.AuthorId).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<string, UserRef>();
        }

        return await ResolveUserRefsAsync(db, ids);
    }

    /// <summary>
    /// The shared id → {name, versioned avatar URL} lookup. Materializes the cheap projection (id, name,
    /// has-avatar flag, version) then builds each avatar URL in memory via the one canonical
    /// {@link UserAvatarEndpoints.BuildUrl}.
    /// </summary>
    private static async Task<Dictionary<string, UserRef>> ResolveUserRefsAsync(
        ApplicationDbContext db, IReadOnlyCollection<string> ids)
    {
        var rows = await db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, HasAvatar = u.AvatarData != null, u.AvatarVersion })
            .ToListAsync();

        return rows.ToDictionary(
            r => r.Id,
            r => new UserRef(r.DisplayName, UserAvatarEndpoints.BuildUrl(r.Id, r.HasAvatar, r.AvatarVersion)));
    }

    private static CommentResponse ToCommentResponse(TaskComment comment, IReadOnlyDictionary<string, UserRef> names)
    {
        var author = names.GetValueOrDefault(comment.AuthorId);
        return new CommentResponse(
            comment.Id,
            comment.Body,
            comment.Kind.ToString(),
            author?.DisplayName ?? string.Empty,
            author?.AvatarUrl,
            comment.CreatedAtUtc);
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

    private static TaskResponse ToResponse(
        HouseholdTask task,
        CallerContext caller,
        IReadOnlyDictionary<string, UserRef> names,
        IReadOnlyDictionary<Guid, int> counts,
        IReadOnlyDictionary<Guid, string[]> tags)
    {
        var canClaim = task.Status == HouseholdTaskStatus.ToDo;
        var canMarkDone = task.Status == HouseholdTaskStatus.InProgress && task.ClaimedById == caller.UserId;
        var canConfirm = caller.Role == HouseholdRole.Admin && task.Status == HouseholdTaskStatus.Done;
        var willSelfAttest = canConfirm && task.ClaimedById == caller.UserId;
        // Loop-recovery (S-05): the claimer or any admin may free an in-progress task; an admin may send a Done one back.
        var canUnclaim = task.Status == HouseholdTaskStatus.InProgress
            && (task.ClaimedById == caller.UserId || caller.Role == HouseholdRole.Admin);
        var canSendBack = caller.Role == HouseholdRole.Admin && task.Status == HouseholdTaskStatus.Done;
        // Editing is admin-only/any-column as of S-05 (members comment instead); delete stays To-do-only (FR-012).
        var canEdit = caller.Role == HouseholdRole.Admin;
        var canDelete = task.Status == HouseholdTaskStatus.ToDo;

        var creator = names.GetValueOrDefault(task.CreatedById);
        var claimer = task.ClaimedById is not null ? names.GetValueOrDefault(task.ClaimedById) : null;

        return new TaskResponse(
            task.Id,
            task.Title,
            task.Description,
            tags.TryGetValue(task.Id, out var taskTags) ? taskTags : [],
            task.Status.ToString(),
            creator?.DisplayName ?? string.Empty,
            creator?.AvatarUrl,
            claimer?.DisplayName,
            claimer?.AvatarUrl,
            task.CreatedAtUtc,
            canClaim,
            canMarkDone,
            canConfirm,
            willSelfAttest,
            canEdit,
            canDelete,
            canUnclaim,
            canSendBack,
            counts.TryGetValue(task.Id, out var commentCount) ? commentCount : 0);
    }
}

public sealed record CreateTaskRequest(string Title, string? Description, string[]? Tags);

public sealed record UpdateTaskRequest(string Title, string? Description, string[]? Tags);

public sealed record ReorderRequest(string Status, Guid[] OrderedIds);

public sealed record SendBackRequest(string Comment);

public sealed record CreateCommentRequest(string Body);

public sealed record CommentResponse(
    Guid Id,
    string Body,
    string Kind,
    string AuthorName,
    string? AuthorAvatarUrl,
    DateTime CreatedAtUtc);

public sealed record TaskResponse(
    Guid Id,
    string Title,
    string? Description,
    string[] Tags,
    string Status,
    string CreatedByName,
    string? CreatedByAvatarUrl,
    string? ClaimerName,
    string? ClaimerAvatarUrl,
    DateTime CreatedAtUtc,
    bool CanClaim,
    bool CanMarkDone,
    bool CanConfirm,
    bool WillSelfAttest,
    bool CanEdit,
    bool CanDelete,
    bool CanUnclaim,
    bool CanSendBack,
    int CommentCount);
