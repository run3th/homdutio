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
            new { title = "Updated", description = "Now with detail", category = "Kitchen" }));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<TaskBody>())!;
        Assert.Equal("Updated", body.Title);
        Assert.Equal("Now with detail", body.Description);
        Assert.Equal("Kitchen", body.Category);
    }

    [Fact]
    public async Task Edit_with_a_blank_title_returns_400()
    {
        var token = await RegisterAndLoginAsync(NewEmail("edit-blank"), "Molly");
        await CreateHouseholdAsync(token, "Edit Blank House");
        var task = await CreateTaskAsync(token, "Has a title");

        var resp = await _client.SendAsync(Authed(HttpMethod.Put, $"/api/tasks/{task.Id}", token,
            new { title = "   ", description = (string?)null, category = (string?)null }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Editing_a_claimed_task_returns_409()
    {
        var token = await RegisterAndLoginAsync(NewEmail("edit-claimed"), "Molly");
        await CreateHouseholdAsync(token, "Edit Claimed House");
        var task = await CreateTaskAsync(token, "About to be claimed");
        (await ActionAsync(token, task.Id, "claim")).EnsureSuccessStatusCode();

        var resp = await _client.SendAsync(Authed(HttpMethod.Put, $"/api/tasks/{task.Id}", token,
            new { title = "Too late", description = (string?)null, category = (string?)null }));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
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
            new { title = "Hijack", description = (string?)null, category = (string?)null }));
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
    public async Task To_do_task_reports_can_edit_and_can_delete_then_false_once_claimed()
    {
        var token = await RegisterAndLoginAsync(NewEmail("aff-ed"), "Molly");
        await CreateHouseholdAsync(token, "Affordance Edit House");
        var task = await CreateTaskAsync(token, "Manageable");

        var todoView = (await GetBoardAsync(token)).Single(t => t.Id == task.Id);
        Assert.True(todoView.CanEdit);
        Assert.True(todoView.CanDelete);

        (await ActionAsync(token, task.Id, "claim")).EnsureSuccessStatusCode();
        var claimedView = (await GetBoardAsync(token)).Single(t => t.Id == task.Id);
        Assert.False(claimedView.CanEdit);
        Assert.False(claimedView.CanDelete);
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
        bool WillSelfAttest,
        bool CanEdit,
        bool CanDelete,
        int CommentCount);

    private sealed record CommentBody(
        Guid Id,
        string Body,
        string Kind,
        string AuthorName,
        DateTime CreatedAtUtc);
}
