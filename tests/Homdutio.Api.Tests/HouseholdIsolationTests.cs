using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Homdutio.Data;
using Homdutio.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homdutio.Api.Tests;

/// <summary>
/// S-07's canonical cross-household isolation sweep — the single auditable place that proves the PRD's
/// worst-possible-bug guardrail (US-02, FR-019): a member of one household can neither act on, read, nor
/// infer the existence of another household's data. The sweep is <b>inventory-driven</b>: it iterates
/// <see cref="ScopedRouteInventory"/> (the same list the <see cref="RouteIsolationCoverageTests"/> build
/// guard projects its scoped set from) and drives every entry from a foreign household (House B) against
/// House A's ids, dispatching on each entry's <see cref="ScopedRouteInventory.Behavior"/>:
///
/// <list type="bullet">
/// <item><c>ParityNotFound</c> — a foreign-id 404 must be byte-indistinguishable from an unknown-id 404
/// (empty body), so the status code can never serve as an existence oracle (Gap #1).</item>
/// <item><c>OwnOnlyCollection</c> — a foreign caller's read returns only their own rows (board / roster).</item>
/// <item><c>MixedBatchRejected</c> — a foreign id mixed into the caller's own batch rejects the whole
/// request and leaves the caller's order intact.</item>
/// </list>
///
/// Because the sweep iterates the inventory, a route is "exercised" iff it is in the inventory — the same
/// fact the guard uses for "categorized" (Gap #2). Adding a household-scoped route to the inventory makes
/// both the guard and this sweep cover it with no second manual step.
///
/// Reuses <see cref="AuthApiFactory"/> and the register → login → create-household → bearer pattern; House A
/// seeds a task in every lifecycle state plus a second member directly via the DbContext (per the existing
/// per-file test convention — the test project shares no base class). One House A / House B pair is built per
/// sweep and reused across all entries: every foreign/unknown call resolves no row and therefore mutates
/// nothing (including DELETE and PUT — they 404 before any write), so the shared fixture stays pristine.
/// </summary>
public class HouseholdIsolationTests : IClassFixture<AuthApiFactory>
{
    private const string Password = "P@ssw0rd!23";
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public HouseholdIsolationTests(AuthApiFactory factory)
    {
        factory.EnsureDatabaseMigrated();
        _factory = factory;
        _client = factory.CreateClient();
    }

    // --- The sweep -----------------------------------------------------------------------------------

    /// <summary>
    /// Drives every <see cref="ScopedRouteInventory"/> entry from House B against House A and asserts its
    /// behavior-appropriate isolation. Per-route failures are aggregated into one descriptive assertion so a
    /// single run reports every leaking route at once. The explicit loop-count assertion locks Gap #2: the
    /// sweep visits exactly as many routes as the guard categorizes — no entry can be silently skipped.
    /// </summary>
    [Fact]
    public async Task Every_scoped_route_isolates_across_households()
    {
        var a = await BuildHouseAAsync("sweep");
        var bToken = await BuildHouseBAsync("sweep");

        // B's own data so the collection / batch behaviors have something of B's to assert against. Created
        // in order, both land in To do — B's board is [bOwn1, bOwn2] and stays that way (nothing mutates it).
        var bOwn1 = await CreateTaskAsync(bToken, "B1");
        var bOwn2 = await CreateTaskAsync(bToken, "B2");

        // Seed a distinctive tag in each household so the GET /api/tasks/tags own-only assertion has a
        // House A value that must NOT leak and a House B value that must appear.
        await SeedTagAsync(a.TodoTaskId, "alpha-secret-tag");
        await SeedTagAsync(bOwn1, "beta-own-tag");

        var failures = new List<string>();
        var exercised = 0;

        foreach (var route in ScopedRouteInventory.All)
        {
            exercised++;
            try
            {
                // Exhaustive dispatch on Behavior: a future Behavior value with no arm falls through to the
                // discard throw below and turns this test red (the project does not treat warnings as errors,
                // so this runtime backstop — not a compile error — is the strongest enforcement available).
                Task work = route.Behavior switch
                {
                    ScopedRouteInventory.Behavior.ParityNotFound => AssertParityAsync(route, a, bToken),
                    ScopedRouteInventory.Behavior.OwnOnlyCollection => AssertOwnOnlyCollectionAsync(route, a, bToken, bOwn1),
                    ScopedRouteInventory.Behavior.MixedBatchRejected => AssertMixedBatchRejectedAsync(route, a, bToken, bOwn1, bOwn2),
                    _ => throw new InvalidOperationException($"No sweep arm for Behavior.{route.Behavior}"),
                };
                await work;
            }
            catch (Exception ex)
            {
                // Prefix the exception type so triage can tell an isolation-assertion failure
                // (XunitException) from an infrastructure/wiring throw (e.g. HttpRequestException).
                failures.Add($"{route.Key} [{route.Behavior}]: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Gap #2: every inventory entry was driven — the sweep's reach equals the guard's categorized set.
        Assert.Equal(ScopedRouteInventory.All.Count, exercised);
        Assert.True(
            failures.Count == 0,
            "Cross-household isolation sweep failed for:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    // --- Behavior assertions -------------------------------------------------------------------------

    /// <summary>
    /// A foreign-id call and an unknown-id call must be indistinguishable: both 404 with identical (empty)
    /// bodies (the existence-oracle seal). Locks parity for every <c>ParityNotFound</c> route — including
    /// unclaim / sendback / comments / role, which previously had only a status-code check or none.
    /// </summary>
    private Task AssertParityAsync(ScopedRouteInventory.ScopedRoute route, HouseA a, string bToken)
    {
        var (foreignId, unknownId) = route.IdShape switch
        {
            // Any of A's task ids 404s for a foreign caller — the scoped lookup fails before any state guard.
            ScopedRouteInventory.IdShape.TaskId => (a.TodoTaskId.ToString(), Guid.NewGuid().ToString()),
            ScopedRouteInventory.IdShape.MemberId => (a.MemberId, $"{Guid.NewGuid():N}"),
            _ => throw new InvalidOperationException($"{route.Key}: ParityNotFound requires a TaskId/MemberId shape."),
        };

        var method = new HttpMethod(route.Method);
        var body = route.BodyFactory?.Invoke();
        return AssertNotFoundParityAsync(method, SubstituteId(route, foreignId), SubstituteId(route, unknownId), bToken, body);
    }

    /// <summary>
    /// A foreign caller's collection read returns 200 containing only their own rows — none of House A's data
    /// leaks. Dispatches on the route template (the two own-only collections are the board and the roster).
    /// </summary>
    private async Task AssertOwnOnlyCollectionAsync(ScopedRouteInventory.ScopedRoute route, HouseA a, string bToken, Guid bOwn1)
    {
        switch (route.Template)
        {
            case "/api/tasks":
                var board = await GetBoardAsync(bToken);
                Assert.Contains(board, t => t.Id == bOwn1); // B sees its own board...
                Assert.DoesNotContain(board, t => t.Id == a.TodoTaskId); // ...and none of A's three tasks.
                Assert.DoesNotContain(board, t => t.Id == a.InProgressTaskId);
                Assert.DoesNotContain(board, t => t.Id == a.DoneTaskId);
                break;

            case "/api/households/members":
                var roster = await _client.SendAsync(Authed(HttpMethod.Get, route.Template, bToken));
                roster.EnsureSuccessStatusCode();
                var rows = (await roster.Content.ReadFromJsonAsync<MemberBody[]>())!;
                Assert.Single(rows); // B's roster is only B's own admin...
                Assert.DoesNotContain(rows, m => m.UserId == a.MemberId); // ...neither of A's two members appears.
                break;

            case "/api/tasks/tags":
                var tagsResp = await _client.SendAsync(Authed(HttpMethod.Get, route.Template, bToken));
                tagsResp.EnsureSuccessStatusCode();
                var tagValues = (await tagsResp.Content.ReadFromJsonAsync<string[]>())!;
                Assert.Contains("beta-own-tag", tagValues); // B sees its own tag...
                Assert.DoesNotContain("alpha-secret-tag", tagValues); // ...and never House A's.
                break;

            default:
                throw new InvalidOperationException($"{route.Key}: no own-only assertion wired for this collection.");
        }
    }

    /// <summary>
    /// A foreign id mixed into B's own reorder batch rejects the whole request (404) and must not corrupt B's
    /// order — no partial reindex, no leak. (PUT /api/tasks/order is the lone mixed-batch route.)
    /// </summary>
    private async Task AssertMixedBatchRejectedAsync(ScopedRouteInventory.ScopedRoute route, HouseA a, string bToken, Guid bOwn1, Guid bOwn2)
    {
        var reorder = await _client.SendAsync(Authed(HttpMethod.Put, route.Template, bToken,
            new { status = "ToDo", orderedIds = new[] { bOwn2, a.TodoTaskId, bOwn1 } }));
        Assert.Equal(HttpStatusCode.NotFound, reorder.StatusCode);

        // B's board keeps its original creation order — the rejected reorder changed nothing.
        var board = await GetBoardAsync(bToken);
        Assert.Equal(new[] { bOwn1, bOwn2 }, board.Select(t => t.Id).ToArray());
    }

    // --- Helpers -------------------------------------------------------------------------------------

    /// <summary>Substitutes the route's id placeholder with a concrete id (foreign or unknown) of its shape.</summary>
    private static string SubstituteId(ScopedRouteInventory.ScopedRoute route, string id) => route.IdShape switch
    {
        ScopedRouteInventory.IdShape.TaskId => route.Template.Replace("{id}", id),
        ScopedRouteInventory.IdShape.MemberId => route.Template.Replace("{userId}", id),
        _ => route.Template,
    };

    private async Task<string> RegisterAndLoginAsync(string email, string? displayName = null)
    {
        object register = displayName is null
            ? new { email, password = Password }
            : new { email, password = Password, displayName };
        (await _client.PostAsJsonAsync("/api/auth/register", register)).EnsureSuccessStatusCode();

        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginBody>();
        return body!.AccessToken;
    }

    private HttpRequestMessage Authed(HttpMethod method, string uri, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private async Task<Guid> CreateHouseholdAsync(string token, string name)
    {
        var resp = await _client.SendAsync(Authed(HttpMethod.Post, "/api/households", token, new { name }));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<HouseholdBody>())!.Id;
    }

    private async Task<Guid> CreateTaskAsync(string token, string title)
    {
        var resp = await _client.SendAsync(Authed(HttpMethod.Post, "/api/tasks", token, new { title }));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TaskBody>())!.Id;
    }

    private Task<HttpResponseMessage> ActionAsync(string token, Guid id, string action) =>
        _client.SendAsync(Authed(HttpMethod.Post, $"/api/tasks/{id}/{action}", token));

    private async Task<List<TaskBody>> GetBoardAsync(string token)
    {
        var resp = await _client.SendAsync(Authed(HttpMethod.Get, "/api/tasks", token));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<TaskBody>>())!;
    }

    /// <summary>Seeds a member row directly so House A's roster + a foreign member-id target exist.</summary>
    private async Task SeedMemberAsync(string email, Guid householdId, HouseholdRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        db.HouseholdMembers.Add(new HouseholdMember
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            UserId = user.Id,
            Role = role,
            JoinedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Seeds a tag row on a task directly (copying the task's household id) so the suggestion sweep has data.</summary>
    private async Task SeedTagAsync(Guid taskId, string value)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var task = await db.HouseholdTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        db.TaskTags.Add(new TaskTag
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            HouseholdId = task.HouseholdId,
            Value = value,
        });
        await db.SaveChangesAsync();
    }

    private async Task<string> UserIdByEmailAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.Users.SingleAsync(u => u.Email == email)).Id;
    }

    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    /// <summary>
    /// Builds House A fully populated: an admin, a second (Member) member, and a task sitting in each
    /// lifecycle state (To do / In progress / Done) so every foreign transition can target a real,
    /// state-appropriate id. The returned context carries the ids House B will attack.
    /// </summary>
    private async Task<HouseA> BuildHouseAAsync(string tag)
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail($"{tag}-a-admin"), "AdminA");
        var householdId = await CreateHouseholdAsync(adminToken, $"House A {tag}");

        var memberEmail = NewEmail($"{tag}-a-member");
        await RegisterAndLoginAsync(memberEmail, "MemberA");
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);
        var memberId = await UserIdByEmailAsync(memberEmail);

        var todo = await CreateTaskAsync(adminToken, "A to-do");

        var inProgress = await CreateTaskAsync(adminToken, "A in-progress");
        (await ActionAsync(adminToken, inProgress, "claim")).EnsureSuccessStatusCode();

        var done = await CreateTaskAsync(adminToken, "A done");
        (await ActionAsync(adminToken, done, "claim")).EnsureSuccessStatusCode();
        (await ActionAsync(adminToken, done, "done")).EnsureSuccessStatusCode();

        return new HouseA(adminToken, householdId, memberId, todo, inProgress, done);
    }

    /// <summary>Registers a foreign caller and gives them their own household (House B).</summary>
    private async Task<string> BuildHouseBAsync(string tag)
    {
        var token = await RegisterAndLoginAsync(NewEmail($"{tag}-b-admin"), "AdminB");
        await CreateHouseholdAsync(token, $"House B {tag}");
        return token;
    }

    /// <summary>
    /// Asserts a request against a foreign-household id and the same request against a never-existed id
    /// are indistinguishable: same 404 status AND identical (empty) response body. This is the existence-
    /// oracle seal — a future change that adds a distinguishing "belongs to another household" message
    /// would break parity and fail here.
    /// </summary>
    private async Task AssertNotFoundParityAsync(HttpMethod method, string foreignUri, string unknownUri, string token, object? body = null)
    {
        var foreign = await _client.SendAsync(Authed(method, foreignUri, token, body));
        var unknown = await _client.SendAsync(Authed(method, unknownUri, token, body));

        var foreignBody = await foreign.Content.ReadAsStringAsync();
        var unknownBody = await unknown.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(unknownBody, foreignBody);
        Assert.Equal(string.Empty, foreignBody);
    }

    private sealed record HouseA(
        string AdminToken,
        Guid HouseholdId,
        string MemberId,
        Guid TodoTaskId,
        Guid InProgressTaskId,
        Guid DoneTaskId);

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAtUtc);

    private sealed record HouseholdBody(Guid Id, string Name, string Role);

    private sealed record MemberBody(string UserId, string DisplayName, string Email, string Role, bool IsSelf, bool CanManage);

    private sealed record TaskBody(Guid Id, string Title, string Status);
}
