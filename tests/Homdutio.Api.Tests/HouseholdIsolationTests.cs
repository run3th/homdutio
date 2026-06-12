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
/// infer the existence of another household's data. Every household-scoped route in
/// <c>TaskEndpoints</c> and <c>HouseholdEndpoints</c> is driven from a foreign household (House B) against
/// House A's ids and must return 404 / an own-only payload. Two parity tests additionally assert a
/// foreign-id 404 is byte-indistinguishable from an unknown-id 404 — so the status code can never serve as
/// an existence oracle. A new household-scoped endpoint should be added here; the S-07 route-coverage guard
/// (<see cref="RouteIsolationCoverageTests"/>) fails the build if it isn't.
///
/// Reuses <see cref="AuthApiFactory"/> and the register → login → create-household → bearer pattern; House A
/// seeds a task in every lifecycle state plus a second member directly via the DbContext (per the existing
/// per-file test convention — the test project shares no base class).
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

    // --- Helpers -------------------------------------------------------------------------------------

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

    // --- Read isolation ------------------------------------------------------------------------------

    [Fact]
    public async Task Foreign_household_board_never_shows_house_a_tasks()
    {
        var a = await BuildHouseAAsync("board");
        var bToken = await BuildHouseBAsync("board");
        var bOwn = await CreateTaskAsync(bToken, "B's own task");

        var board = await GetBoardAsync(bToken);

        // B sees only its own task; none of A's three tasks leak onto B's board (US-02 read scoping).
        Assert.Contains(board, t => t.Id == bOwn);
        Assert.DoesNotContain(board, t => t.Id == a.TodoTaskId);
        Assert.DoesNotContain(board, t => t.Id == a.InProgressTaskId);
        Assert.DoesNotContain(board, t => t.Id == a.DoneTaskId);
    }

    // --- Task lifecycle isolation --------------------------------------------------------------------

    [Fact]
    public async Task Every_task_lifecycle_route_returns_404_across_households()
    {
        var a = await BuildHouseAAsync("life");
        var bToken = await BuildHouseBAsync("life");

        // Each transition targets a House A task in the state that transition requires — so the 404 comes
        // from household scoping, not a state guard. claim/done/confirm fill the previously-untested gaps.
        Assert.Equal(HttpStatusCode.NotFound, (await ActionAsync(bToken, a.TodoTaskId, "claim")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ActionAsync(bToken, a.InProgressTaskId, "done")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ActionAsync(bToken, a.DoneTaskId, "confirm")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ActionAsync(bToken, a.InProgressTaskId, "unclaim")).StatusCode);

        var sendback = await _client.SendAsync(
            Authed(HttpMethod.Post, $"/api/tasks/{a.DoneTaskId}/sendback", bToken, new { comment = "not yours" }));
        Assert.Equal(HttpStatusCode.NotFound, sendback.StatusCode);
    }

    [Fact]
    public async Task Task_management_routes_return_404_across_households()
    {
        var a = await BuildHouseAAsync("mgmt");
        var bToken = await BuildHouseBAsync("mgmt");
        var b1 = await CreateTaskAsync(bToken, "B1");
        var b2 = await CreateTaskAsync(bToken, "B2");

        var edit = await _client.SendAsync(Authed(HttpMethod.Put, $"/api/tasks/{a.TodoTaskId}", bToken,
            new { title = "hijack", description = (string?)null, category = (string?)null }));
        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);

        var delete = await _client.SendAsync(Authed(HttpMethod.Delete, $"/api/tasks/{a.TodoTaskId}", bToken));
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        // A foreign id mixed into B's own reorder rejects the whole request — no partial reindex, no leak.
        var reorder = await _client.SendAsync(Authed(HttpMethod.Put, "/api/tasks/order", bToken,
            new { status = "ToDo", orderedIds = new[] { b2, a.TodoTaskId, b1 } }));
        Assert.Equal(HttpStatusCode.NotFound, reorder.StatusCode);

        // B's board keeps its original creation order — the failed reorder changed nothing.
        var board = await GetBoardAsync(bToken);
        Assert.Equal(new[] { b1, b2 }, board.Select(t => t.Id).ToArray());
    }

    [Fact]
    public async Task Comment_routes_return_404_across_households()
    {
        var a = await BuildHouseAAsync("cmt");
        var bToken = await BuildHouseBAsync("cmt");

        var post = await _client.SendAsync(
            Authed(HttpMethod.Post, $"/api/tasks/{a.TodoTaskId}/comments", bToken, new { body = "sneaking in" }));
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);

        var list = await _client.SendAsync(Authed(HttpMethod.Get, $"/api/tasks/{a.TodoTaskId}/comments", bToken));
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
    }

    // --- Member-administration isolation -------------------------------------------------------------

    [Fact]
    public async Task Member_admin_routes_isolate_across_households()
    {
        var a = await BuildHouseAAsync("mem");
        var bToken = await BuildHouseBAsync("mem");

        // B's roster lists only B's own member (its admin) — neither of A's two members appears.
        var roster = await _client.SendAsync(Authed(HttpMethod.Get, "/api/households/members", bToken));
        roster.EnsureSuccessStatusCode();
        var rows = (await roster.Content.ReadFromJsonAsync<MemberBody[]>())!;
        Assert.Single(rows);
        Assert.DoesNotContain(rows, m => m.UserId == a.MemberId);

        // B's admin cannot promote/demote or remove a member of House A — foreign target → 404 (no leak).
        var role = await _client.SendAsync(
            Authed(HttpMethod.Post, $"/api/households/members/{a.MemberId}/role", bToken, new { role = "Admin" }));
        Assert.Equal(HttpStatusCode.NotFound, role.StatusCode);

        var remove = await _client.SendAsync(Authed(HttpMethod.Delete, $"/api/households/members/{a.MemberId}", bToken));
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
    }

    // --- Existence-oracle seal (body-shape parity) ---------------------------------------------------

    [Fact]
    public async Task Foreign_task_id_404_is_indistinguishable_from_an_unknown_id()
    {
        var a = await BuildHouseAAsync("parity-task");
        var bToken = await BuildHouseBAsync("parity-task");
        var unknown = Guid.NewGuid();

        await AssertNotFoundParityAsync(HttpMethod.Post, $"/api/tasks/{a.TodoTaskId}/claim", $"/api/tasks/{unknown}/claim", bToken);
        await AssertNotFoundParityAsync(HttpMethod.Post, $"/api/tasks/{a.InProgressTaskId}/done", $"/api/tasks/{unknown}/done", bToken);
        await AssertNotFoundParityAsync(HttpMethod.Post, $"/api/tasks/{a.DoneTaskId}/confirm", $"/api/tasks/{unknown}/confirm", bToken);
        await AssertNotFoundParityAsync(HttpMethod.Put, $"/api/tasks/{a.TodoTaskId}", $"/api/tasks/{unknown}", bToken,
            new { title = "x", description = (string?)null, category = (string?)null });
        await AssertNotFoundParityAsync(HttpMethod.Delete, $"/api/tasks/{a.TodoTaskId}", $"/api/tasks/{unknown}", bToken);
    }

    [Fact]
    public async Task Foreign_member_id_404_is_indistinguishable_from_an_unknown_id()
    {
        var a = await BuildHouseAAsync("parity-mem");
        var bToken = await BuildHouseBAsync("parity-mem");
        var unknown = $"{Guid.NewGuid():N}";

        await AssertNotFoundParityAsync(HttpMethod.Post,
            $"/api/households/members/{a.MemberId}/role", $"/api/households/members/{unknown}/role", bToken, new { role = "Admin" });
        await AssertNotFoundParityAsync(HttpMethod.Delete,
            $"/api/households/members/{a.MemberId}", $"/api/households/members/{unknown}", bToken);
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
