using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Homdutio.Data;
using Homdutio.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homdutio.Api.Tests;

/// <summary>
/// Locks the S-03 north-star loop and its invariants: the full create → claim → done → confirm lifecycle
/// (closure removes the card but the row + its four <see cref="TaskEvent"/>s persist, NFR-3), the
/// self-attested path (FR-016), every transition guard, the server-computed affordance flags, and the
/// foreign-household 404 (US-02/FR-019 — no existence leak). Reuses <see cref="AuthApiFactory"/> and the
/// register → login → create-household → bearer pattern from <see cref="HouseholdEndpointsTests"/>; a few
/// tests seed a second (non-admin) member directly via the DbContext because the invite flow is S-06.
/// </summary>
public class TaskEndpointsTests : IClassFixture<AuthApiFactory>
{
    private const string Password = "P@ssw0rd!23";
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public TaskEndpointsTests(AuthApiFactory factory)
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
        var body = await resp.Content.ReadFromJsonAsync<HouseholdBody>();
        return body!.Id;
    }

    private async Task<TaskBody> CreateTaskAsync(string token, string title)
    {
        var resp = await _client.SendAsync(Authed(HttpMethod.Post, "/api/tasks", token, new { title }));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TaskBody>())!;
    }

    private Task<HttpResponseMessage> ActionAsync(string token, Guid id, string action) =>
        _client.SendAsync(Authed(HttpMethod.Post, $"/api/tasks/{id}/{action}", token));

    private async Task<List<TaskBody>> GetBoardAsync(string token)
    {
        var resp = await _client.SendAsync(Authed(HttpMethod.Get, "/api/tasks", token));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<TaskBody>>())!;
    }

    /// <summary>Seeds a member row directly (no invite flow until S-06) so non-admin paths can be exercised.</summary>
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

    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    // --- Tests ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Full_loop_closes_task_and_get_omits_it()
    {
        var token = await RegisterAndLoginAsync(NewEmail("loop"), "Molly");
        await CreateHouseholdAsync(token, "The Burrow");

        var task = await CreateTaskAsync(token, "Take out bins");
        Assert.Equal("ToDo", task.Status);
        Assert.Equal("Molly", task.CreatedByName);

        (await ActionAsync(token, task.Id, "claim")).EnsureSuccessStatusCode();
        (await ActionAsync(token, task.Id, "done")).EnsureSuccessStatusCode();
        var confirm = await ActionAsync(token, task.Id, "confirm");
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        var board = await GetBoardAsync(token);
        Assert.DoesNotContain(board, t => t.Id == task.Id);
    }

    [Fact]
    public async Task Closed_task_row_and_events_persist()
    {
        var token = await RegisterAndLoginAsync(NewEmail("persist"), "Arthur");
        await CreateHouseholdAsync(token, "Persisters");

        var task = await CreateTaskAsync(token, "Fix the clock");
        (await ActionAsync(token, task.Id, "claim")).EnsureSuccessStatusCode();
        (await ActionAsync(token, task.Id, "done")).EnsureSuccessStatusCode();
        (await ActionAsync(token, task.Id, "confirm")).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.HouseholdTasks.SingleAsync(t => t.Id == task.Id);
        Assert.NotNull(row.ClosedAtUtc);
        Assert.Equal(HouseholdTaskStatus.Done, row.Status);

        var events = await db.TaskEvents.Where(e => e.TaskId == task.Id).ToListAsync();
        Assert.Equal(4, events.Count);
        Assert.Contains(events, e => e.Type == TaskEventType.Created);
        Assert.Contains(events, e => e.Type == TaskEventType.Claimed);
        Assert.Contains(events, e => e.Type == TaskEventType.MarkedDone);
        Assert.Contains(events, e => e.Type == TaskEventType.Confirmed);
    }

    [Fact]
    public async Task Self_attested_confirm_records_flag_on_event_and_projection()
    {
        var token = await RegisterAndLoginAsync(NewEmail("self"), "Solo");
        await CreateHouseholdAsync(token, "Lone Admins");

        var task = await CreateTaskAsync(token, "Self loop");
        (await ActionAsync(token, task.Id, "claim")).EnsureSuccessStatusCode();
        (await ActionAsync(token, task.Id, "done")).EnsureSuccessStatusCode();
        (await ActionAsync(token, task.Id, "confirm")).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.HouseholdTasks.SingleAsync(t => t.Id == task.Id);
        Assert.True(row.SelfAttested);

        var confirmed = await db.TaskEvents.SingleAsync(e => e.TaskId == task.Id && e.Type == TaskEventType.Confirmed);
        Assert.True(confirmed.SelfAttested);
    }

    [Fact]
    public async Task Claiming_an_already_claimed_task_returns_409()
    {
        var token = await RegisterAndLoginAsync(NewEmail("claimed"), "Admin");
        await CreateHouseholdAsync(token, "Claimers");

        var task = await CreateTaskAsync(token, "Once only");
        (await ActionAsync(token, task.Id, "claim")).EnsureSuccessStatusCode();

        var second = await ActionAsync(token, task.Id, "claim");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Marking_done_as_non_claimer_returns_403()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("md-admin"), "Admin");
        var householdId = await CreateHouseholdAsync(adminToken, "Done House");

        var otherEmail = NewEmail("md-other");
        var otherToken = await RegisterAndLoginAsync(otherEmail, "Other");
        await SeedMemberAsync(otherEmail, householdId, HouseholdRole.Member);

        var task = await CreateTaskAsync(adminToken, "Claimed by admin");
        (await ActionAsync(adminToken, task.Id, "claim")).EnsureSuccessStatusCode();

        var done = await ActionAsync(otherToken, task.Id, "done");
        Assert.Equal(HttpStatusCode.Forbidden, done.StatusCode);
    }

    [Fact]
    public async Task Marking_done_on_a_non_in_progress_task_returns_409()
    {
        var token = await RegisterAndLoginAsync(NewEmail("md-state"), "Admin");
        await CreateHouseholdAsync(token, "State House");

        var task = await CreateTaskAsync(token, "Still to do");

        var done = await ActionAsync(token, task.Id, "done");
        Assert.Equal(HttpStatusCode.Conflict, done.StatusCode);
    }

    [Fact]
    public async Task Confirming_as_a_non_admin_member_returns_403()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("cf-admin"), "Admin");
        var householdId = await CreateHouseholdAsync(adminToken, "Confirm House");

        var memberEmail = NewEmail("cf-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail, "Member");
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);

        var task = await CreateTaskAsync(adminToken, "Needs confirm");
        (await ActionAsync(adminToken, task.Id, "claim")).EnsureSuccessStatusCode();
        (await ActionAsync(adminToken, task.Id, "done")).EnsureSuccessStatusCode();

        var confirm = await ActionAsync(memberToken, task.Id, "confirm");
        Assert.Equal(HttpStatusCode.Forbidden, confirm.StatusCode);
    }

    [Fact]
    public async Task Confirming_a_non_done_task_returns_409()
    {
        var token = await RegisterAndLoginAsync(NewEmail("cf-state"), "Admin");
        await CreateHouseholdAsync(token, "Confirm State");

        var task = await CreateTaskAsync(token, "Not done yet");
        (await ActionAsync(token, task.Id, "claim")).EnsureSuccessStatusCode();

        var confirm = await ActionAsync(token, task.Id, "confirm");
        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
    }

    [Fact]
    public async Task Foreign_household_task_id_returns_404()
    {
        var aToken = await RegisterAndLoginAsync(NewEmail("hh-a"), "Alice");
        await CreateHouseholdAsync(aToken, "House A");
        var task = await CreateTaskAsync(aToken, "Belongs to A");

        var bToken = await RegisterAndLoginAsync(NewEmail("hh-b"), "Bob");
        await CreateHouseholdAsync(bToken, "House B");

        var claim = await ActionAsync(bToken, task.Id, "claim");
        Assert.Equal(HttpStatusCode.NotFound, claim.StatusCode);

        // And it never appears on B's board.
        var board = await GetBoardAsync(bToken);
        Assert.DoesNotContain(board, t => t.Id == task.Id);
    }

    [Fact]
    public async Task Blank_title_returns_400()
    {
        var token = await RegisterAndLoginAsync(NewEmail("blank"), "Admin");
        await CreateHouseholdAsync(token, "Blank House");

        var resp = await _client.SendAsync(Authed(HttpMethod.Post, "/api/tasks", token, new { title = "   " }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_get_returns_401()
    {
        var resp = await _client.GetAsync("/api/tasks");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ToDo_task_reports_can_claim_for_a_member()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("aff-admin"), "Admin");
        var householdId = await CreateHouseholdAsync(adminToken, "Affordance House");

        var memberEmail = NewEmail("aff-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail, "Member");
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);

        var created = await CreateTaskAsync(adminToken, "Claimable");

        var memberView = (await GetBoardAsync(memberToken)).Single(t => t.Id == created.Id);
        Assert.True(memberView.CanClaim);
        Assert.False(memberView.CanMarkDone);
        Assert.False(memberView.CanConfirm);
    }

    [Fact]
    public async Task Done_task_reports_can_confirm_only_for_admin_with_self_attest()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("done-admin"), "Admin");
        var householdId = await CreateHouseholdAsync(adminToken, "Done Affordance");

        var memberEmail = NewEmail("done-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail, "Member");
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);

        var task = await CreateTaskAsync(adminToken, "Drive to done");
        (await ActionAsync(adminToken, task.Id, "claim")).EnsureSuccessStatusCode();
        (await ActionAsync(adminToken, task.Id, "done")).EnsureSuccessStatusCode();

        var adminView = (await GetBoardAsync(adminToken)).Single(t => t.Id == task.Id);
        Assert.True(adminView.CanConfirm);
        Assert.True(adminView.WillSelfAttest); // the admin is also the claimer

        var memberView = (await GetBoardAsync(memberToken)).Single(t => t.Id == task.Id);
        Assert.False(memberView.CanConfirm);
        Assert.False(memberView.WillSelfAttest);
    }

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAtUtc);

    private sealed record HouseholdBody(Guid Id, string Name, string Role);

    private sealed record TaskBody(
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
}
