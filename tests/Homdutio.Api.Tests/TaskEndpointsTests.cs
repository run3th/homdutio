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

    // --- Phase 2 / Risk #2: lifecycle guard completeness (oracle-critical) ----------------------------
    // Statuses/flags below are derived from the transition matrix in research.md, NOT from a fresh read
    // of the guard code at authoring time (that would re-introduce the oracle problem the risk warns of).

    [Fact]
    public async Task Cross_member_confirm_records_self_attested_false()
    {
        // The missing half of "self-attested iff admin confirms own work": a *different* member claims
        // and marks done, the admin confirms → success AND SelfAttested == false on both the projection
        // and the Confirmed event (the true half is already covered at :150).
        var adminEmail = NewEmail("xm-admin");
        var adminToken = await RegisterAndLoginAsync(adminEmail, "Admin");
        var householdId = await CreateHouseholdAsync(adminToken, "Cross Member House");

        var memberEmail = NewEmail("xm-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail, "Member");
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);

        var task = await CreateTaskAsync(adminToken, "Member's work, admin confirms");
        (await ActionAsync(memberToken, task.Id, "claim")).EnsureSuccessStatusCode();
        (await ActionAsync(memberToken, task.Id, "done")).EnsureSuccessStatusCode();

        var confirm = await ActionAsync(adminToken, task.Id, "confirm");
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        // Read the flag from the persisted row and the event — the source of truth — not the
        // willSelfAttest affordance preview (:296).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var adminId = (await db.Users.SingleAsync(u => u.Email == adminEmail)).Id;

        var row = await db.HouseholdTasks.SingleAsync(t => t.Id == task.Id);
        Assert.False(row.SelfAttested);
        Assert.Equal(adminId, row.ConfirmedById);

        var confirmed = await db.TaskEvents.SingleAsync(e => e.TaskId == task.Id && e.Type == TaskEventType.Confirmed);
        Assert.False(confirmed.SelfAttested);
    }

    [Fact]
    public async Task Confirming_a_non_done_task_as_a_non_admin_returns_403()
    {
        // Crossed axes: caller is non-admin AND the task is not yet Done (InProgress). confirm is
        // role-first (TaskEndpoints.cs:217 role before :222 state), so the role guard fires → 403, not 409.
        var adminToken = await RegisterAndLoginAsync(NewEmail("cf-order-admin"), "Admin");
        var householdId = await CreateHouseholdAsync(adminToken, "Confirm Order House");

        var memberEmail = NewEmail("cf-order-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail, "Member");
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);

        var task = await CreateTaskAsync(adminToken, "Claimed, not done");
        (await ActionAsync(memberToken, task.Id, "claim")).EnsureSuccessStatusCode();

        var confirm = await ActionAsync(memberToken, task.Id, "confirm");
        Assert.Equal(HttpStatusCode.Forbidden, confirm.StatusCode);
    }

    [Fact]
    public async Task Marking_done_a_to_do_task_as_a_non_claimer_returns_409()
    {
        // Crossed axes: caller is not the claimer AND the task is not InProgress (ToDo, unclaimed). done
        // is state-first (TaskEndpoints.cs:178 state before :183 actor), so the state guard fires → 409, not 403.
        var adminToken = await RegisterAndLoginAsync(NewEmail("md-order-admin"), "Admin");
        var householdId = await CreateHouseholdAsync(adminToken, "Done Order House");

        var memberEmail = NewEmail("md-order-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail, "Member");
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);

        var task = await CreateTaskAsync(adminToken, "Still to do");

        var done = await ActionAsync(memberToken, task.Id, "done");
        Assert.Equal(HttpStatusCode.Conflict, done.StatusCode);
    }

    // --- Phase 2 / Risk #2: lifecycle completeness sweep ----------------------------------------------
    // Foreign-household 404 parity for done/confirm, the logical double-claim 409, and the member-open
    // Delete pin. Statuses below are derived from the transition matrix in research.md, not a fresh guard read.

    [Fact]
    public async Task Done_and_confirm_on_a_foreign_household_task_return_404()
    {
        // Extends the foreign-household sweep to the two verbs research flagged untested (claim parity at
        // :319, unclaim/sendback at :870). Scope (HouseholdScope.LoadScopedTaskAsync) fires before the
        // state/role guards, so an outsider gets 404 with an empty body — byte-identical to an unknown-id
        // 404 (the §6.1 existence-oracle seal: no "exists but forbidden" leak).
        var aToken = await RegisterAndLoginAsync(NewEmail("dc-a"), "Alice");
        await CreateHouseholdAsync(aToken, "House A");
        var task = await CreateTaskAsync(aToken, "Belongs to A");

        var bToken = await RegisterAndLoginAsync(NewEmail("dc-b"), "Bob");
        await CreateHouseholdAsync(bToken, "House B");

        var done = await ActionAsync(bToken, task.Id, "done");
        Assert.Equal(HttpStatusCode.NotFound, done.StatusCode);
        Assert.Empty(await done.Content.ReadAsStringAsync());

        var confirm = await ActionAsync(bToken, task.Id, "confirm");
        Assert.Equal(HttpStatusCode.NotFound, confirm.StatusCode);
        Assert.Empty(await confirm.Content.ReadAsStringAsync());

        // Parity anchor: an unknown id from B's own perspective is the same empty-body 404.
        var unknown = await ActionAsync(bToken, Guid.NewGuid(), "done");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Empty(await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Claiming_an_in_progress_task_as_a_second_member_returns_409()
    {
        // A *different* member claiming an already-InProgress task is rejected via the in-handler status read
        // (TaskEndpoints.cs:143) — the logical 409. Distinct from the same-claimer re-claim at :246.
        // NOTE: this proves only the *logical* conflict. HouseholdTask has no rowversion / optimistic-
        // concurrency token, so a *true simultaneous* double-claim is not provable at this layer — that
        // concurrent race belongs to Phase 3 / Risk #3 (cf. lessons.md "Guard min-count invariants with an
        // atomic check-and-mutate": the codebase locks where it cares about races, deliberately not on claim).
        var adminToken = await RegisterAndLoginAsync(NewEmail("dbl-admin"), "Admin");
        var householdId = await CreateHouseholdAsync(adminToken, "Double Claim House");

        var firstEmail = NewEmail("dbl-first");
        var firstToken = await RegisterAndLoginAsync(firstEmail, "First");
        await SeedMemberAsync(firstEmail, householdId, HouseholdRole.Member);

        var secondEmail = NewEmail("dbl-second");
        var secondToken = await RegisterAndLoginAsync(secondEmail, "Second");
        await SeedMemberAsync(secondEmail, householdId, HouseholdRole.Member);

        var task = await CreateTaskAsync(adminToken, "First come first served");
        (await ActionAsync(firstToken, task.Id, "claim")).EnsureSuccessStatusCode();

        var second = await ActionAsync(secondToken, task.Id, "claim");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_to_do_task_as_a_non_admin_member_succeeds()
    {
        // PINNED BEHAVIOR (deliberate): Delete is state-gated (ToDo only) but NOT role-gated
        // (TaskEndpoints.cs:418-420 has no caller.Role check), unlike admin-only Edit. So a non-admin
        // member may delete a ToDo task. This pins today's behavior so a future "lock delete to admins"
        // change is a conscious break rather than a silent regression — if that change lands, update this
        // test as a deliberate decision.
        var adminToken = await RegisterAndLoginAsync(NewEmail("del-mem-admin"), "Admin");
        var householdId = await CreateHouseholdAsync(adminToken, "Member Delete House");

        var memberEmail = NewEmail("del-mem-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail, "Member");
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);

        var task = await CreateTaskAsync(adminToken, "Any member may delete this");

        var resp = await _client.SendAsync(Authed(HttpMethod.Delete, $"/api/tasks/{task.Id}", memberToken));
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.HouseholdTasks.AnyAsync(t => t.Id == task.Id));
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

    // --- S-04: ordering + edit/delete/reorder ---------------------------------------------------------

    [Fact]
    public async Task Created_tasks_append_to_the_bottom_of_to_do_in_creation_order()
    {
        var token = await RegisterAndLoginAsync(NewEmail("order"), "Molly");
        await CreateHouseholdAsync(token, "Order House");

        var a = await CreateTaskAsync(token, "First");
        var b = await CreateTaskAsync(token, "Second");
        var c = await CreateTaskAsync(token, "Third");

        var board = await GetBoardAsync(token);
        Assert.Equal(new[] { a.Id, b.Id, c.Id }, board.Select(t => t.Id).ToArray());
    }

    [Fact]
    public async Task Reorder_persists_a_new_within_column_order()
    {
        var token = await RegisterAndLoginAsync(NewEmail("reorder"), "Molly");
        await CreateHouseholdAsync(token, "Reorder House");

        var a = await CreateTaskAsync(token, "A");
        var b = await CreateTaskAsync(token, "B");
        var c = await CreateTaskAsync(token, "C");

        var reorder = await _client.SendAsync(Authed(HttpMethod.Put, "/api/tasks/order", token,
            new { status = "ToDo", orderedIds = new[] { c.Id, a.Id, b.Id } }));
        Assert.Equal(HttpStatusCode.NoContent, reorder.StatusCode);

        var board = await GetBoardAsync(token);
        Assert.Equal(new[] { c.Id, a.Id, b.Id }, board.Select(t => t.Id).ToArray());
    }

    [Fact]
    public async Task Reorder_with_a_foreign_household_id_returns_404_and_leaves_order_unchanged()
    {
        var aToken = await RegisterAndLoginAsync(NewEmail("ro-a"), "Alice");
        await CreateHouseholdAsync(aToken, "House A");
        var foreign = await CreateTaskAsync(aToken, "Belongs to A");

        var bToken = await RegisterAndLoginAsync(NewEmail("ro-b"), "Bob");
        await CreateHouseholdAsync(bToken, "House B");
        var b1 = await CreateTaskAsync(bToken, "B1");
        var b2 = await CreateTaskAsync(bToken, "B2");

        var resp = await _client.SendAsync(Authed(HttpMethod.Put, "/api/tasks/order", bToken,
            new { status = "ToDo", orderedIds = new[] { b2.Id, foreign.Id, b1.Id } }));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

        // No partial reindex — B's board keeps its original creation order.
        var board = await GetBoardAsync(bToken);
        Assert.Equal(new[] { b1.Id, b2.Id }, board.Select(t => t.Id).ToArray());
    }

    [Fact]
    public async Task Reorder_with_a_wrong_status_id_returns_404()
    {
        var token = await RegisterAndLoginAsync(NewEmail("ro-status"), "Molly");
        await CreateHouseholdAsync(token, "Status House");

        var todo = await CreateTaskAsync(token, "Stays to do");
        var claimed = await CreateTaskAsync(token, "Will be claimed");
        (await ActionAsync(token, claimed.Id, "claim")).EnsureSuccessStatusCode();

        // Asking to reorder "ToDo" but including a now-InProgress task → rejected.
        var resp = await _client.SendAsync(Authed(HttpMethod.Put, "/api/tasks/order", token,
            new { status = "ToDo", orderedIds = new[] { claimed.Id, todo.Id } }));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Reorder_with_an_unknown_status_returns_400()
    {
        var token = await RegisterAndLoginAsync(NewEmail("ro-badstatus"), "Molly");
        await CreateHouseholdAsync(token, "Bad Status House");
        var a = await CreateTaskAsync(token, "A");

        var resp = await _client.SendAsync(Authed(HttpMethod.Put, "/api/tasks/order", token,
            new { status = "Nonsense", orderedIds = new[] { a.Id } }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Edit_updates_a_to_do_task()
    {
        var token = await RegisterAndLoginAsync(NewEmail("edit"), "Molly");
        await CreateHouseholdAsync(token, "Edit House");
        var task = await CreateTaskAsync(token, "Original");

        var resp = await _client.SendAsync(Authed(HttpMethod.Put, $"/api/tasks/{task.Id}", token,
            new { title = "Updated", description = "Now with detail", tags = new[] { "Kitchen" } }));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<TaskBody>())!;
        Assert.Equal("Updated", body.Title);
        Assert.Equal("Now with detail", body.Description);
        Assert.Equal(new[] { "Kitchen" }, body.Tags);
    }

    [Fact]
    public async Task Edit_with_a_blank_title_returns_400()
    {
        var token = await RegisterAndLoginAsync(NewEmail("edit-blank"), "Molly");
        await CreateHouseholdAsync(token, "Edit Blank House");
        var task = await CreateTaskAsync(token, "Has a title");

        var resp = await _client.SendAsync(Authed(HttpMethod.Put, $"/api/tasks/{task.Id}", token,
            new { title = "   ", description = (string?)null, tags = (string[]?)null }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_edits_a_claimed_task_returns_200()
    {
        // S-05 replaces the To-do-only edit guard (the old Editing_a_claimed_task_returns_409) with
        // admin-anytime editing: an admin may edit a task in any column.
        var token = await RegisterAndLoginAsync(NewEmail("edit-claimed"), "Molly");
        await CreateHouseholdAsync(token, "Edit Claimed House");
        var task = await CreateTaskAsync(token, "About to be claimed");
        (await ActionAsync(token, task.Id, "claim")).EnsureSuccessStatusCode();

        var resp = await _client.SendAsync(Authed(HttpMethod.Put, $"/api/tasks/{task.Id}", token,
            new { title = "Edited while claimed", description = (string?)null, tags = (string[]?)null }));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<TaskBody>())!;
        Assert.Equal("Edited while claimed", body.Title);
    }

    [Fact]
    public async Task Editing_a_foreign_household_task_returns_404()
    {
        var aToken = await RegisterAndLoginAsync(NewEmail("edit-a"), "Alice");
        await CreateHouseholdAsync(aToken, "House A");
        var task = await CreateTaskAsync(aToken, "Belongs to A");

        var bToken = await RegisterAndLoginAsync(NewEmail("edit-b"), "Bob");
        await CreateHouseholdAsync(bToken, "House B");

        var resp = await _client.SendAsync(Authed(HttpMethod.Put, $"/api/tasks/{task.Id}", bToken,
            new { title = "Hijack", description = (string?)null, tags = (string[]?)null }));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_a_to_do_task_and_its_created_event()
    {
        var token = await RegisterAndLoginAsync(NewEmail("delete"), "Molly");
        await CreateHouseholdAsync(token, "Delete House");
        var task = await CreateTaskAsync(token, "Delete me");

        var resp = await _client.SendAsync(Authed(HttpMethod.Delete, $"/api/tasks/{task.Id}", token));
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var board = await GetBoardAsync(token);
        Assert.DoesNotContain(board, t => t.Id == task.Id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.HouseholdTasks.AnyAsync(t => t.Id == task.Id));
        Assert.False(await db.TaskEvents.AnyAsync(e => e.TaskId == task.Id));
    }

    [Fact]
    public async Task Deleting_a_claimed_task_returns_409()
    {
        var token = await RegisterAndLoginAsync(NewEmail("delete-claimed"), "Molly");
        await CreateHouseholdAsync(token, "Delete Claimed House");
        var task = await CreateTaskAsync(token, "Claimed, cannot delete");
        (await ActionAsync(token, task.Id, "claim")).EnsureSuccessStatusCode();

        var resp = await _client.SendAsync(Authed(HttpMethod.Delete, $"/api/tasks/{task.Id}", token));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_foreign_household_task_returns_404()
    {
        var aToken = await RegisterAndLoginAsync(NewEmail("del-a"), "Alice");
        await CreateHouseholdAsync(aToken, "House A");
        var task = await CreateTaskAsync(aToken, "Belongs to A");

        var bToken = await RegisterAndLoginAsync(NewEmail("del-b"), "Bob");
        await CreateHouseholdAsync(bToken, "House B");

        var resp = await _client.SendAsync(Authed(HttpMethod.Delete, $"/api/tasks/{task.Id}", bToken));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Claiming_a_task_appends_it_to_the_bottom_of_in_progress()
    {
        var token = await RegisterAndLoginAsync(NewEmail("transition"), "Molly");
        await CreateHouseholdAsync(token, "Transition House");

        var first = await CreateTaskAsync(token, "First in progress");
        var second = await CreateTaskAsync(token, "Second in progress");
        (await ActionAsync(token, first.Id, "claim")).EnsureSuccessStatusCode();
        (await ActionAsync(token, second.Id, "claim")).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var firstRow = await db.HouseholdTasks.SingleAsync(t => t.Id == first.Id);
        var secondRow = await db.HouseholdTasks.SingleAsync(t => t.Id == second.Id);
        Assert.True(secondRow.SortOrder > firstRow.SortOrder);
    }

    [Fact]
    public async Task Admin_keeps_can_edit_after_claim_while_can_delete_goes_false()
    {
        // S-05: edit is admin-anytime (so it survives the claim), delete stays To-do-only (so it doesn't).
        var token = await RegisterAndLoginAsync(NewEmail("aff-ed"), "Molly");
        await CreateHouseholdAsync(token, "Affordance Edit House");
        var task = await CreateTaskAsync(token, "Manageable");

        var todoView = (await GetBoardAsync(token)).Single(t => t.Id == task.Id);
        Assert.True(todoView.CanEdit);
        Assert.True(todoView.CanDelete);

        (await ActionAsync(token, task.Id, "claim")).EnsureSuccessStatusCode();
        var claimedView = (await GetBoardAsync(token)).Single(t => t.Id == task.Id);
        Assert.True(claimedView.CanEdit);   // admin-anytime
        Assert.False(claimedView.CanDelete); // To-do-only
    }

    [Fact]
    public async Task Unauthenticated_edit_delete_and_reorder_return_401()
    {
        var id = Guid.NewGuid();

        var edit = await _client.PutAsJsonAsync($"/api/tasks/{id}", new { title = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, edit.StatusCode);

        var delete = await _client.DeleteAsync($"/api/tasks/{id}");
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);

        var reorder = await _client.PutAsJsonAsync("/api/tasks/order", new { status = "ToDo", orderedIds = new[] { id } });
        Assert.Equal(HttpStatusCode.Unauthorized, reorder.StatusCode);
    }

    // --- S-05: loop-recovery transitions --------------------------------------------------------------

    private Task<HttpResponseMessage> SendBackAsync(string token, Guid id, string comment) =>
        _client.SendAsync(Authed(HttpMethod.Post, $"/api/tasks/{id}/sendback", token, new { comment }));

    private async Task<HouseholdTask> LoadTaskRowAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.HouseholdTasks.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    [Fact]
    public async Task Claimer_unclaims_their_in_progress_task_back_to_to_do_unassigned()
    {
        var token = await RegisterAndLoginAsync(NewEmail("uc-self"), "Molly");
        await CreateHouseholdAsync(token, "Unclaim House");
        var task = await CreateTaskAsync(token, "Stuck task");
        (await ActionAsync(token, task.Id, "claim")).EnsureSuccessStatusCode();

        var resp = await ActionAsync(token, task.Id, "unclaim");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<TaskBody>())!;
        Assert.Equal("ToDo", body.Status);
        Assert.Null(body.ClaimerName);

        var row = await LoadTaskRowAsync(task.Id);
        Assert.Equal(HouseholdTaskStatus.ToDo, row.Status);
        Assert.Null(row.ClaimedById);
        Assert.Null(row.ClaimedAtUtc);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.TaskEvents.AnyAsync(e => e.TaskId == task.Id && e.Type == TaskEventType.Unclaimed));
    }

    [Fact]
    public async Task Admin_unclaims_another_members_in_progress_task()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("uc-admin"), "Admin");
        var householdId = await CreateHouseholdAsync(adminToken, "Admin Unclaim House");

        var memberEmail = NewEmail("uc-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail, "Member");
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);

        var task = await CreateTaskAsync(adminToken, "Member's task");
        (await ActionAsync(memberToken, task.Id, "claim")).EnsureSuccessStatusCode();

        var resp = await ActionAsync(adminToken, task.Id, "unclaim");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var row = await LoadTaskRowAsync(task.Id);
        Assert.Equal(HouseholdTaskStatus.ToDo, row.Status);
        Assert.Null(row.ClaimedById);
    }

    [Fact]
    public async Task Unclaim_by_a_non_claimer_non_admin_returns_403()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("uc-403-admin"), "Admin");
        var householdId = await CreateHouseholdAsync(adminToken, "Unclaim 403 House");

        var claimerEmail = NewEmail("uc-403-claimer");
        var claimerToken = await RegisterAndLoginAsync(claimerEmail, "Claimer");
        await SeedMemberAsync(claimerEmail, householdId, HouseholdRole.Member);

        var otherEmail = NewEmail("uc-403-other");
        var otherToken = await RegisterAndLoginAsync(otherEmail, "Other");
        await SeedMemberAsync(otherEmail, householdId, HouseholdRole.Member);

        var task = await CreateTaskAsync(adminToken, "Claimed by claimer");
        (await ActionAsync(claimerToken, task.Id, "claim")).EnsureSuccessStatusCode();

        var resp = await ActionAsync(otherToken, task.Id, "unclaim");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Unclaim_of_a_non_in_progress_task_returns_409()
    {
        var token = await RegisterAndLoginAsync(NewEmail("uc-409"), "Molly");
        await CreateHouseholdAsync(token, "Unclaim 409 House");
        var task = await CreateTaskAsync(token, "Still to do");

        var resp = await ActionAsync(token, task.Id, "unclaim");
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_sends_back_a_done_task_keeping_the_claimer_and_recording_the_reason()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("sb-admin"), "Admin");
        var householdId = await CreateHouseholdAsync(adminToken, "Send Back House");

        var memberEmail = NewEmail("sb-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail, "Member");
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);

        var task = await CreateTaskAsync(adminToken, "Sloppy work");
        (await ActionAsync(memberToken, task.Id, "claim")).EnsureSuccessStatusCode();
        (await ActionAsync(memberToken, task.Id, "done")).EnsureSuccessStatusCode();

        var resp = await SendBackAsync(adminToken, task.Id, "Please redo the corners");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<TaskBody>())!;
        Assert.Equal("InProgress", body.Status);
        Assert.Equal("Member", body.ClaimerName); // original claimer remains attached

        var row = await LoadTaskRowAsync(task.Id);
        Assert.Equal(HouseholdTaskStatus.InProgress, row.Status);
        Assert.Null(row.DoneAtUtc);
        Assert.NotNull(row.ClaimedById);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.TaskEvents.AnyAsync(e => e.TaskId == task.Id && e.Type == TaskEventType.SentBack));
        var comment = await db.TaskComments.SingleAsync(c => c.TaskId == task.Id && c.Kind == TaskCommentKind.SendBack);
        Assert.Equal("Please redo the corners", comment.Body);
    }

    [Fact]
    public async Task Send_back_by_a_non_admin_returns_403()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("sb-403-admin"), "Admin");
        var householdId = await CreateHouseholdAsync(adminToken, "Send Back 403 House");

        var memberEmail = NewEmail("sb-403-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail, "Member");
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);

        var task = await CreateTaskAsync(adminToken, "Done by member");
        (await ActionAsync(memberToken, task.Id, "claim")).EnsureSuccessStatusCode();
        (await ActionAsync(memberToken, task.Id, "done")).EnsureSuccessStatusCode();

        var resp = await SendBackAsync(memberToken, task.Id, "I disagree");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Send_back_of_a_non_done_task_returns_409()
    {
        var token = await RegisterAndLoginAsync(NewEmail("sb-409"), "Admin");
        await CreateHouseholdAsync(token, "Send Back 409 House");
        var task = await CreateTaskAsync(token, "Only claimed");
        (await ActionAsync(token, task.Id, "claim")).EnsureSuccessStatusCode();

        var resp = await SendBackAsync(token, task.Id, "Not done yet though");
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("oversized")]
    public async Task Send_back_with_a_blank_or_oversized_reason_returns_400(string kind)
    {
        var token = await RegisterAndLoginAsync(NewEmail($"sb-400-{kind.Trim()}"), "Admin");
        await CreateHouseholdAsync(token, "Send Back 400 House");
        var task = await CreateTaskAsync(token, "Reason validation");
        (await ActionAsync(token, task.Id, "claim")).EnsureSuccessStatusCode();
        (await ActionAsync(token, task.Id, "done")).EnsureSuccessStatusCode();

        var comment = kind == "oversized" ? new string('x', 281) : "   ";
        var resp = await SendBackAsync(token, task.Id, comment);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Non_admin_edit_returns_403()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("ed-403-admin"), "Admin");
        var householdId = await CreateHouseholdAsync(adminToken, "Edit 403 House");

        var memberEmail = NewEmail("ed-403-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail, "Member");
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);

        var task = await CreateTaskAsync(adminToken, "Members may not edit");

        var resp = await _client.SendAsync(Authed(HttpMethod.Put, $"/api/tasks/{task.Id}", memberToken,
            new { title = "Member tried", description = (string?)null, tags = (string[]?)null }));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_edits_a_done_task_returns_200()
    {
        var token = await RegisterAndLoginAsync(NewEmail("ed-done"), "Admin");
        await CreateHouseholdAsync(token, "Edit Done House");
        var task = await CreateTaskAsync(token, "Will be done");
        (await ActionAsync(token, task.Id, "claim")).EnsureSuccessStatusCode();
        (await ActionAsync(token, task.Id, "done")).EnsureSuccessStatusCode();

        var resp = await _client.SendAsync(Authed(HttpMethod.Put, $"/api/tasks/{task.Id}", token,
            new { title = "Edited while done", description = (string?)null, tags = (string[]?)null }));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Affordance_flags_report_unclaim_and_send_back_per_role_and_status()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("aff-uc-admin"), "Admin");
        var householdId = await CreateHouseholdAsync(adminToken, "Affordance Recovery House");

        var memberEmail = NewEmail("aff-uc-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail, "Member");
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);

        var task = await CreateTaskAsync(adminToken, "Recovery affordances");

        // To do: nobody can unclaim or send back; only the admin can edit.
        var todoMember = (await GetBoardAsync(memberToken)).Single(t => t.Id == task.Id);
        Assert.False(todoMember.CanUnclaim);
        Assert.False(todoMember.CanSendBack);
        Assert.False(todoMember.CanEdit);
        var todoAdmin = (await GetBoardAsync(adminToken)).Single(t => t.Id == task.Id);
        Assert.True(todoAdmin.CanEdit);

        // In progress (claimed by member): claimer + admin can unclaim; nobody can send back yet.
        (await ActionAsync(memberToken, task.Id, "claim")).EnsureSuccessStatusCode();
        var ipMember = (await GetBoardAsync(memberToken)).Single(t => t.Id == task.Id);
        var ipAdmin = (await GetBoardAsync(adminToken)).Single(t => t.Id == task.Id);
        Assert.True(ipMember.CanUnclaim);   // the claimer
        Assert.True(ipAdmin.CanUnclaim);    // any admin
        Assert.False(ipMember.CanSendBack);
        Assert.False(ipAdmin.CanSendBack);

        // Done: only the admin can send back; unclaim no longer applies.
        (await ActionAsync(memberToken, task.Id, "done")).EnsureSuccessStatusCode();
        var doneMember = (await GetBoardAsync(memberToken)).Single(t => t.Id == task.Id);
        var doneAdmin = (await GetBoardAsync(adminToken)).Single(t => t.Id == task.Id);
        Assert.False(doneMember.CanSendBack);
        Assert.True(doneAdmin.CanSendBack);
        Assert.False(doneAdmin.CanUnclaim);
    }

    [Fact]
    public async Task Unclaim_and_send_back_on_a_foreign_household_task_return_404()
    {
        var aToken = await RegisterAndLoginAsync(NewEmail("rec-a"), "Alice");
        await CreateHouseholdAsync(aToken, "House A");
        var task = await CreateTaskAsync(aToken, "Belongs to A");
        (await ActionAsync(aToken, task.Id, "claim")).EnsureSuccessStatusCode();

        var bToken = await RegisterAndLoginAsync(NewEmail("rec-b"), "Bob");
        await CreateHouseholdAsync(bToken, "House B");

        var unclaim = await ActionAsync(bToken, task.Id, "unclaim");
        Assert.Equal(HttpStatusCode.NotFound, unclaim.StatusCode);

        var sendback = await SendBackAsync(bToken, task.Id, "Not yours");
        Assert.Equal(HttpStatusCode.NotFound, sendback.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_unclaim_and_send_back_return_401()
    {
        var id = Guid.NewGuid();

        var unclaim = await _client.PostAsync($"/api/tasks/{id}/unclaim", null);
        Assert.Equal(HttpStatusCode.Unauthorized, unclaim.StatusCode);

        var sendback = await _client.PostAsJsonAsync($"/api/tasks/{id}/sendback", new { comment = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, sendback.StatusCode);
    }

    // --- S-05: comments foundation --------------------------------------------------------------------

    private Task<HttpResponseMessage> PostCommentAsync(string token, Guid taskId, string body) =>
        _client.SendAsync(Authed(HttpMethod.Post, $"/api/tasks/{taskId}/comments", token, new { body }));

    private async Task<List<CommentBody>> GetCommentsAsync(string token, Guid taskId)
    {
        var resp = await _client.SendAsync(Authed(HttpMethod.Get, $"/api/tasks/{taskId}/comments", token));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<CommentBody>>())!;
    }

    [Fact]
    public async Task Posting_a_comment_returns_201_and_persists()
    {
        var token = await RegisterAndLoginAsync(NewEmail("cmt"), "Molly");
        await CreateHouseholdAsync(token, "Comment House");
        var task = await CreateTaskAsync(token, "Has a thread");

        var resp = await PostCommentAsync(token, task.Id, "First note");
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var created = (await resp.Content.ReadFromJsonAsync<CommentBody>())!;
        Assert.Equal("First note", created.Body);
        Assert.Equal("Member", created.Kind);
        Assert.Equal("Molly", created.AuthorName);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.TaskComments.SingleAsync(c => c.Id == created.Id);
        Assert.Equal("First note", row.Body);
        Assert.Equal(TaskCommentKind.Member, row.Kind);
        Assert.Equal(task.Id, row.TaskId);
    }

    [Fact]
    public async Task Listing_comments_returns_them_in_order_with_author_names()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("cl-admin"), "Admin");
        var householdId = await CreateHouseholdAsync(adminToken, "Thread House");

        var memberEmail = NewEmail("cl-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail, "Member");
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);

        var task = await CreateTaskAsync(adminToken, "Discuss me");
        (await PostCommentAsync(adminToken, task.Id, "From admin")).EnsureSuccessStatusCode();
        (await PostCommentAsync(memberToken, task.Id, "From member")).EnsureSuccessStatusCode();

        var comments = await GetCommentsAsync(memberToken, task.Id);
        Assert.Equal(2, comments.Count);
        Assert.Equal("From admin", comments[0].Body);
        Assert.Equal("Admin", comments[0].AuthorName);
        Assert.Equal("From member", comments[1].Body);
        Assert.Equal("Member", comments[1].AuthorName);
        Assert.True(comments[0].CreatedAtUtc <= comments[1].CreatedAtUtc);
    }

    [Fact]
    public async Task Posting_a_blank_comment_returns_400()
    {
        var token = await RegisterAndLoginAsync(NewEmail("cmt-blank"), "Molly");
        await CreateHouseholdAsync(token, "Blank Comment House");
        var task = await CreateTaskAsync(token, "No empty notes");

        var resp = await PostCommentAsync(token, task.Id, "   ");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Posting_an_oversized_comment_returns_400()
    {
        var token = await RegisterAndLoginAsync(NewEmail("cmt-big"), "Molly");
        await CreateHouseholdAsync(token, "Big Comment House");
        var task = await CreateTaskAsync(token, "No essays");

        var resp = await PostCommentAsync(token, task.Id, new string('x', 281));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Commenting_on_a_foreign_household_task_returns_404()
    {
        var aToken = await RegisterAndLoginAsync(NewEmail("cmt-a"), "Alice");
        await CreateHouseholdAsync(aToken, "House A");
        var task = await CreateTaskAsync(aToken, "Belongs to A");

        var bToken = await RegisterAndLoginAsync(NewEmail("cmt-b"), "Bob");
        await CreateHouseholdAsync(bToken, "House B");

        var post = await PostCommentAsync(bToken, task.Id, "Sneaking in");
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);

        var list = await _client.SendAsync(Authed(HttpMethod.Get, $"/api/tasks/{task.Id}/comments", bToken));
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_comment_routes_return_401()
    {
        var id = Guid.NewGuid();

        var post = await _client.PostAsJsonAsync($"/api/tasks/{id}/comments", new { body = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);

        var list = await _client.GetAsync($"/api/tasks/{id}/comments");
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
    }

    [Fact]
    public async Task Board_dto_reports_the_comment_count_per_task()
    {
        var token = await RegisterAndLoginAsync(NewEmail("cmt-count"), "Molly");
        await CreateHouseholdAsync(token, "Count House");
        var withComments = await CreateTaskAsync(token, "Chatty");
        var quiet = await CreateTaskAsync(token, "Silent");

        (await PostCommentAsync(token, withComments.Id, "one")).EnsureSuccessStatusCode();
        (await PostCommentAsync(token, withComments.Id, "two")).EnsureSuccessStatusCode();

        var board = await GetBoardAsync(token);
        Assert.Equal(2, board.Single(t => t.Id == withComments.Id).CommentCount);
        Assert.Equal(0, board.Single(t => t.Id == quiet.Id).CommentCount);
    }

    // --- S-12: task tags + per-household suggestions --------------------------------------------------

    private async Task<TaskBody> CreateTaskWithTagsAsync(string token, string title, params string[] tags)
    {
        var resp = await _client.SendAsync(Authed(HttpMethod.Post, "/api/tasks", token, new { title, tags }));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TaskBody>())!;
    }

    private async Task<List<string>> GetTagSuggestionsAsync(string token)
    {
        var resp = await _client.SendAsync(Authed(HttpMethod.Get, "/api/tasks/tags", token));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<string>>())!;
    }

    [Fact]
    public async Task Create_with_tags_persists_them_deduped_trimmed_and_sorted()
    {
        var token = await RegisterAndLoginAsync(NewEmail("tags-create"), "Molly");
        await CreateHouseholdAsync(token, "Tag House");

        // Case-insensitive de-dup (first-seen "Kitchen" wins), trim/collapse, alphabetical render.
        var created = await CreateTaskWithTagsAsync(token, "Tagged", "Kitchen", "kitchen", "  Garden ");
        Assert.Equal(new[] { "Garden", "Kitchen" }, created.Tags);

        var board = await GetBoardAsync(token);
        Assert.Equal(new[] { "Garden", "Kitchen" }, board.Single(t => t.Id == created.Id).Tags);
    }

    [Fact]
    public async Task Edit_rewrites_the_tag_set_wholesale()
    {
        var token = await RegisterAndLoginAsync(NewEmail("tags-edit"), "Molly");
        await CreateHouseholdAsync(token, "Tag Edit House");
        var task = await CreateTaskWithTagsAsync(token, "Re-tag me", "Kitchen", "Garden");

        var resp = await _client.SendAsync(Authed(HttpMethod.Put, $"/api/tasks/{task.Id}", token,
            new { title = "Re-tag me", description = (string?)null, tags = new[] { "Pets" } }));
        resp.EnsureSuccessStatusCode();
        var body = (await resp.Content.ReadFromJsonAsync<TaskBody>())!;
        Assert.Equal(new[] { "Pets" }, body.Tags); // old tags gone, only the new one remains
    }

    [Fact]
    public async Task Tag_suggestions_are_distinct_and_include_closed_task_tags()
    {
        var token = await RegisterAndLoginAsync(NewEmail("tags-suggest"), "Molly");
        await CreateHouseholdAsync(token, "Suggest House");

        // An open task and one we drive to closed — "Kitchen" is shared so it must appear exactly once,
        // and the closed task's "Pets" must still be suggested (no ClosedAtUtc filter).
        await CreateTaskWithTagsAsync(token, "Open one", "Kitchen", "Garden");
        var toClose = await CreateTaskWithTagsAsync(token, "Will close", "Kitchen", "Pets");
        (await ActionAsync(token, toClose.Id, "claim")).EnsureSuccessStatusCode();
        (await ActionAsync(token, toClose.Id, "done")).EnsureSuccessStatusCode();
        (await ActionAsync(token, toClose.Id, "confirm")).EnsureSuccessStatusCode();

        var suggestions = await GetTagSuggestionsAsync(token);
        Assert.Equal(new[] { "Garden", "Kitchen", "Pets" }, suggestions);
    }

    [Fact]
    public async Task Creating_with_over_limit_tags_returns_400()
    {
        var token = await RegisterAndLoginAsync(NewEmail("tags-overlimit"), "Molly");
        await CreateHouseholdAsync(token, "Overlimit House");

        var tooMany = Enumerable.Range(0, 11).Select(i => $"tag{i}").ToArray();
        var resp = await _client.SendAsync(Authed(HttpMethod.Post, "/api/tasks", token, new { title = "Too many", tags = tooMany }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_tag_suggestions_return_401()
    {
        var resp = await _client.GetAsync("/api/tasks/tags");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAtUtc);

    private sealed record HouseholdBody(Guid Id, string Name, string Role);

    private sealed record TaskBody(
        Guid Id,
        string Title,
        string? Description,
        string[] Tags,
        string Status,
        string CreatedByName,
        string? ClaimerName,
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

    private sealed record CommentBody(
        Guid Id,
        string Body,
        string Kind,
        string AuthorName,
        DateTime CreatedAtUtc);
}
