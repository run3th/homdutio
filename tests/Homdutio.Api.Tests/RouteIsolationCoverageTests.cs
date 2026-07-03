using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Homdutio.Api.Tests;

/// <summary>
/// S-07's drift backstop: the route-coverage convention guard. It enumerates every registered <c>/api/</c>
/// route outside the auth area from the live host and asserts each is categorized as either SCOPED
/// (household-scoped — a foreign caller must get 404 / an own-only payload, and the route MUST be exercised
/// by <see cref="HouseholdIsolationTests"/>) or EXEMPT (no foreign-household-id surface: own-data reads,
/// create-in-the-caller's-own-household, or token-scoped invite routes). A new endpoint added without being
/// placed in one bucket — including one under a brand-new route prefix — breaks set-equality and fails the
/// build, forcing the author to decide which it is, so a future endpoint can never silently skip the sweep.
///
/// This guard proves COVERAGE (no route was forgotten), not query CORRECTNESS — the isolation sweep itself
/// proves each scoped route's WHERE clause is right.
/// </summary>
public class RouteIsolationCoverageTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;

    public RouteIsolationCoverageTests(AuthApiFactory factory)
    {
        _factory = factory;
        // Force the host to build so the endpoint graph is populated before we enumerate it.
        _ = factory.CreateClient();
    }

    /// <summary>
    /// Household-scoped routes: a foreign caller must receive 404 / an own-only payload. Derived from the
    /// shared <see cref="ScopedRouteInventory"/> so the guard's coverage set and the behavioral sweep can
    /// never disagree about which routes are scoped — a route is "categorized" here iff it is "exercised" in
    /// <see cref="HouseholdIsolationTests"/>. To add a scoped route, add one descriptor to the inventory.
    /// </summary>
    private static readonly HashSet<string> Scoped = new(ScopedRouteInventory.Keys, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Exempt routes carry no foreign-household-id surface, so the isolation sweep does not apply —
    /// own-data-only reads, create-in-the-caller's-own-household, or token-scoped invite routes (whose
    /// cross-household token scoping is locked by S-06's invite tests).
    /// </summary>
    private static readonly HashSet<string> Exempt = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST /api/tasks",                              // creates in the caller's own household
        "GET /api/households/me",                       // returns the caller's own household
        "POST /api/households",                         // create; the caller has no membership yet
        "POST /api/households/invites",                 // generates for the caller's own household
        "GET /api/households/invites/{token}",          // anonymous, token-scoped preview (S-06)
        "POST /api/households/invites/{token}/accept",  // token-scoped join (S-06)
        "PUT /api/profile/me",                          // updates the caller's own profile (sub-scoped, S-09)
        "PUT /api/profile/me/avatar",                   // stores the caller's own avatar (sub-scoped, S-09)
        "DELETE /api/profile/me/avatar",                // clears the caller's own avatar (sub-scoped, S-09)
        "GET /api/users/{id}/avatar",                   // anonymous public avatar serving (no household surface, S-09)
        "GET /api/push/key",                            // sub-scoped, per-user: returns the public VAPID key (no household surface)
        "POST /api/push/subscribe",                     // sub-scoped, per-user: upserts the caller's own push subscription
        "DELETE /api/push/subscribe",                   // sub-scoped, per-user: removes the caller's own push subscription
        "GET /api/push/devices",                        // sub-scoped, per-user: lists the caller's own registered devices
    };

    /// <summary>
    /// Non-household <c>/api/</c> areas the isolation sweep does not govern. Anything under <c>/api/</c> that
    /// does NOT match one of these prefixes must be categorized Scoped or Exempt — so a household-scoped route
    /// added under a brand-new prefix fails this guard instead of slipping past a hardcoded domain allowlist.
    ///
    /// BLIND SPOT (Gap #3): <c>/api/auth</c> is exempt UNCONDITIONALLY — every route under it skips this guard
    /// AND the isolation sweep. A household-scoped route MUST NEVER be added under <c>/api/auth</c>: it would be
    /// silently exempted and a foreign household could reach it undetected. Put household-scoped routes under
    /// <c>/api/tasks</c> or <c>/api/households</c> (or a new prefix, which then surfaces as uncategorized here).
    /// See test-plan.md §6.1 for the same rule on the authoring side.
    /// </summary>
    private static readonly string[] ExemptPrefixes = { "/api/auth" };

    [Fact]
    public void Every_household_domain_route_is_categorized_scoped_or_exempt()
    {
        var discovered = DiscoverDomainRoutes();

        var expected = new HashSet<string>(Scoped, StringComparer.OrdinalIgnoreCase);
        expected.UnionWith(Exempt);

        var uncategorized = discovered.Except(expected, StringComparer.OrdinalIgnoreCase).ToList();
        var stale = expected.Except(discovered, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.True(uncategorized.Count == 0 && stale.Count == 0, BuildFailureMessage(uncategorized, stale));
    }

    [Fact]
    public void Scoped_and_exempt_sets_do_not_overlap()
    {
        Assert.Empty(Scoped.Intersect(Exempt, StringComparer.OrdinalIgnoreCase));
    }

    private HashSet<string> DiscoverDomainRoutes()
    {
        var dataSource = _factory.Services.GetRequiredService<EndpointDataSource>();
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in dataSource.Endpoints.OfType<RouteEndpoint>())
        {
            var pattern = NormalizePattern(endpoint.RoutePattern.RawText);

            // Consider every /api/* route except those under a non-household exempt prefix (e.g. auth).
            // Inverting the filter this way means a future household-scoped route under a NEW prefix
            // surfaces as uncategorized and fails the build — the guard isn't blind to unforeseen prefixes.
            if (!pattern.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                || ExemptPrefixes.Any(p => pattern.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
            if (methods is null)
            {
                continue;
            }

            foreach (var method in methods)
            {
                routes.Add($"{method} {pattern}");
            }
        }

        return routes;
    }

    /// <summary>
    /// Strips inline route constraints (<c>{id:guid}</c> → <c>{id}</c>) and normalizes the leading slash so a
    /// discovered pattern matches the categorized allowlists regardless of constraint syntax.
    /// </summary>
    private static string NormalizePattern(string? rawText)
    {
        var text = "/" + (rawText ?? string.Empty).Trim('/');
        return Regex.Replace(text, @"\{([^:}]+):[^}]+\}", "{$1}");
    }

    private static string BuildFailureMessage(IReadOnlyCollection<string> uncategorized, IReadOnlyCollection<string> stale)
    {
        var lines = new List<string> { "Household route coverage is out of sync with the S-07 isolation sweep." };
        if (uncategorized.Count > 0)
        {
            lines.Add("Uncategorized route(s) — exercise each in HouseholdIsolationTests and add to the Scoped set, or justify it in the Exempt set:");
            lines.AddRange(uncategorized.Select(r => "  + " + r));
        }

        if (stale.Count > 0)
        {
            lines.Add("Categorized route(s) no longer registered — remove from the Scoped/Exempt set:");
            lines.AddRange(stale.Select(r => "  - " + r));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
