namespace Homdutio.Api.Tests;

/// <summary>
/// The single source of truth for the household-scoped route surface (S-07 / Risk #1). Every route here is
/// one a foreign household must not be able to act on, read, or infer the existence of. Declaring the set
/// once lets the two isolation tests agree by construction rather than by convention:
///
/// <list type="bullet">
/// <item><see cref="RouteIsolationCoverageTests"/> (the build guard) projects each entry to its
/// <c>"METHOD /pattern"</c> <see cref="ScopedRoute.Key"/> and asserts set-equality against the live route
/// graph — a new <c>/api/*</c> route that isn't here (or isn't explicitly exempt) fails the build.</item>
/// <item><see cref="HouseholdIsolationTests"/> (the behavioral sweep) iterates this list and drives each
/// entry from a foreign household, dispatching on <see cref="ScopedRoute.Behavior"/> — so being in the
/// inventory makes a route both "categorized" and "exercised" at once.</item>
/// </list>
///
/// Adding a household-scoped route therefore means adding exactly one descriptor here; the guard and the
/// sweep both pick it up with no second manual step.
/// </summary>
public static class ScopedRouteInventory
{
    /// <summary>
    /// The foreign-caller shape a scoped route exhibits. The sweep dispatches on this; a new value with no
    /// matching arm falls through the switch's discard arm and throws at runtime, turning the sweep red
    /// (the project does not treat warnings as errors, so this runtime backstop — not a compile error — is
    /// the enforcement). A route can never be swept the wrong way without the suite catching it.
    /// </summary>
    public enum Behavior
    {
        /// <summary>A foreign/unknown id resolves no row → 404 with an empty body, byte-identical to an
        /// unknown-id 404 (the existence-oracle seal). The default shape for an id-addressed route.</summary>
        ParityNotFound,

        /// <summary>A collection read with no id surface: a foreign caller gets 200 containing only their own
        /// data (board, roster). Isolation is "none of the other household's rows appear," not a 404.</summary>
        OwnOnlyCollection,

        /// <summary>A batch mutation: a foreign id mixed into the caller's own batch rejects the whole request
        /// (404) and must not corrupt the caller's own order (PUT /api/tasks/order).</summary>
        MixedBatchRejected,
    }

    /// <summary>
    /// Which id placeholder a route addresses, so the sweep can substitute a real foreign id and an unknown id
    /// of the matching shape (a task <see cref="System.Guid"/> vs a member <c>userId</c> string).
    /// </summary>
    public enum IdShape
    {
        /// <summary>No id segment — a collection or batch route (the id, if any, travels in the body).</summary>
        None,

        /// <summary>A task <c>{id}</c> route key — substitute a House A task <see cref="System.Guid"/>.</summary>
        TaskId,

        /// <summary>A member <c>{userId}</c> route key — substitute a House A member id string.</summary>
        MemberId,
    }

    /// <summary>
    /// One household-scoped route descriptor: enough to (a) project the guard's normalized
    /// <c>"METHOD /pattern"</c> key and (b) build a real foreign request for the sweep. <paramref name="Template"/>
    /// must match <c>RouteIsolationCoverageTests.NormalizePattern</c> output exactly (leading slash, no inline
    /// constraints, <c>{id}</c>/<c>{userId}</c> tokens) — the one load-bearing coupling with the guard.
    /// </summary>
    /// <param name="Method">The HTTP method, upper-case (GET / POST / PUT / DELETE) — matches the route graph.</param>
    /// <param name="Template">The normalized route template, e.g. <c>/api/tasks/{id}/claim</c>.</param>
    /// <param name="IdShape">The id placeholder the sweep substitutes a foreign/unknown id into.</param>
    /// <param name="Behavior">The foreign-caller shape the sweep asserts.</param>
    /// <param name="BodyFactory">An optional request-body factory for routes whose handler validates the body
    /// before (or alongside) the scoped lookup — supply the minimal body that reaches the 404.</param>
    public sealed record ScopedRoute(
        string Method,
        string Template,
        IdShape IdShape,
        Behavior Behavior,
        Func<object>? BodyFactory = null)
    {
        /// <summary>The guard's set-equality key form, e.g. <c>"POST /api/tasks/{id}/claim"</c>.</summary>
        public string Key => $"{Method} {Template}";
    }

    /// <summary>
    /// The 15 household-scoped routes across <c>TaskEndpoints</c> and <c>HouseholdEndpoints</c>:
    /// 11 <see cref="Behavior.ParityNotFound"/>, 3 <see cref="Behavior.OwnOnlyCollection"/>,
    /// 1 <see cref="Behavior.MixedBatchRejected"/>. Body factories supply only what a handler validates before
    /// its scoped lookup returns 404 — notably the role route, which validates <c>role</c> ahead of the target
    /// lookup, so a foreign-id 404 is only reached with a parseable role.
    /// </summary>
    public static readonly IReadOnlyList<ScopedRoute> All = new[]
    {
        // --- Task board (own-only collection) --------------------------------------------------------
        new ScopedRoute("GET", "/api/tasks", IdShape.None, Behavior.OwnOnlyCollection),

        // --- Tag suggestions (own-only collection; includes closed-task tags) ------------------------
        new ScopedRoute("GET", "/api/tasks/tags", IdShape.None, Behavior.OwnOnlyCollection),

        // --- Task lifecycle (foreign-id 404 + body parity) -------------------------------------------
        new ScopedRoute("POST", "/api/tasks/{id}/claim", IdShape.TaskId, Behavior.ParityNotFound),
        new ScopedRoute("POST", "/api/tasks/{id}/done", IdShape.TaskId, Behavior.ParityNotFound),
        new ScopedRoute("POST", "/api/tasks/{id}/confirm", IdShape.TaskId, Behavior.ParityNotFound),
        new ScopedRoute("POST", "/api/tasks/{id}/unclaim", IdShape.TaskId, Behavior.ParityNotFound),
        new ScopedRoute("POST", "/api/tasks/{id}/sendback", IdShape.TaskId, Behavior.ParityNotFound,
            () => new { comment = "not yours" }),

        // --- Task management (foreign-id 404 + body parity) ------------------------------------------
        new ScopedRoute("PUT", "/api/tasks/{id}", IdShape.TaskId, Behavior.ParityNotFound,
            () => new { title = "hijack", description = (string?)null, tags = (string[]?)null }),
        new ScopedRoute("DELETE", "/api/tasks/{id}", IdShape.TaskId, Behavior.ParityNotFound),

        // --- Task reorder (mixed-batch rejection) ----------------------------------------------------
        new ScopedRoute("PUT", "/api/tasks/order", IdShape.None, Behavior.MixedBatchRejected),

        // --- Task comments (foreign-id 404 + body parity) --------------------------------------------
        new ScopedRoute("POST", "/api/tasks/{id}/comments", IdShape.TaskId, Behavior.ParityNotFound,
            () => new { body = "sneaking in" }),
        new ScopedRoute("GET", "/api/tasks/{id}/comments", IdShape.TaskId, Behavior.ParityNotFound),

        // --- Household roster (own-only collection) --------------------------------------------------
        new ScopedRoute("GET", "/api/households/members", IdShape.None, Behavior.OwnOnlyCollection),

        // --- Member administration (foreign-id 404 + body parity) ------------------------------------
        // NOTE: unlike the task routes (scoped lookup first), these two handlers run their admin gate
        // (403) BEFORE the scoped target lookup (404). Their foreign-id parity therefore holds only
        // because House B's sweep caller is an admin of House B (BuildHouseBAsync creates the household,
        // making its caller an admin). If that fixture ever seeds a non-admin caller, these flip to 403
        // and the parity assertion fails loud — correct, but for a confusing reason.
        new ScopedRoute("POST", "/api/households/members/{userId}/role", IdShape.MemberId, Behavior.ParityNotFound,
            () => new { role = "Admin" }),
        new ScopedRoute("DELETE", "/api/households/members/{userId}", IdShape.MemberId, Behavior.ParityNotFound),
    };

    /// <summary>The guard's <c>Scoped</c> set: every entry projected to its <see cref="ScopedRoute.Key"/>.</summary>
    public static IEnumerable<string> Keys => All.Select(r => r.Key);
}
