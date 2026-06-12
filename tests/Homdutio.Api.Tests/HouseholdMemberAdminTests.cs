using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Homdutio.Data;
using Homdutio.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homdutio.Api.Tests;

/// <summary>
/// Locks the S-09 member-administration guards (FR-008 promote/demote, FR-009 remove): roster scoping +
/// flags, the admin-only gate, self-action blocks, the role round-trip, foreign/unknown-id 404s, and the
/// removal-time sweep of the removed member's in-progress tasks back to To do (while a *closed* task's
/// audit attribution is left intact, NFR-3). Reuses <see cref="AuthApiFactory"/> and the
/// register → login → create-household → bearer pattern; a couple of tests seed a second member directly.
///
/// Note on the last-admin guard: the server keeps a defensive "cannot demote/remove the last admin" check,
/// but it is unreachable through the API as long as self-actions are blocked first — to target an admin
/// *other than the caller* the household must already hold ≥2 admins. The orphan-prevention that matters in
/// practice is therefore exercised by the self role-change / self-remove tests below.
/// </summary>
public class HouseholdMemberAdminTests : IClassFixture<AuthApiFactory>
{
    private const string Password = "P@ssw0rd!23";
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public HouseholdMemberAdminTests(AuthApiFactory factory)
    {
        factory.EnsureDatabaseMigrated();
        _factory = factory;
        _client = factory.CreateClient();
    }

    // --- Helpers -------------------------------------------------------------------------------------

    private async Task<string> RegisterAndLoginAsync(string email)
    {
        (await _client.PostAsJsonAsync("/api/auth/register", new { email, password = Password })).EnsureSuccessStatusCode();
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

    /// <summary>Seeds a member row directly so multi-member roster / role / remove paths can be exercised.</summary>
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

    private async Task<MemberBody[]> GetRosterAsync(string token)
    {
        var resp = await _client.SendAsync(Authed(HttpMethod.Get, "/api/households/members", token));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<MemberBody[]>())!;
    }

    private Task<HttpResponseMessage> SetRoleAsync(string token, string userId, string role) =>
        _client.SendAsync(Authed(HttpMethod.Post, $"/api/households/members/{userId}/role", token, new { role }));

    private Task<HttpResponseMessage> RemoveAsync(string token, string userId) =>
        _client.SendAsync(Authed(HttpMethod.Delete, $"/api/households/members/{userId}", token));

    private async Task<Guid> CreateTaskAsync(string token, string title)
    {
        var resp = await _client.SendAsync(Authed(HttpMethod.Post, "/api/tasks", token, new { title }));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TaskBody>())!.Id;
    }

    private Task<HttpResponseMessage> ClaimAsync(string token, Guid taskId) =>
        _client.SendAsync(Authed(HttpMethod.Post, $"/api/tasks/{taskId}/claim", token));

    private Task<HttpResponseMessage> MarkDoneAsync(string token, Guid taskId) =>
        _client.SendAsync(Authed(HttpMethod.Post, $"/api/tasks/{taskId}/done", token));

    private Task<HttpResponseMessage> ConfirmAsync(string token, Guid taskId) =>
        _client.SendAsync(Authed(HttpMethod.Post, $"/api/tasks/{taskId}/confirm", token));

    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    // --- Roster --------------------------------------------------------------------------------------

    [Fact]
    public async Task Roster_lists_only_callers_household_with_correct_flags_for_admin()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("ros-admin"));
        var householdId = await CreateHouseholdAsync(adminToken, "Roster House");
        var adminId = await UserIdByEmailAsync(await EmailOfTokenAsync(adminToken));

        var memberEmail = NewEmail("ros-member");
        await RegisterAndLoginAsync(memberEmail);
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);
        var memberId = await UserIdByEmailAsync(memberEmail);

        // A separate household whose member must never appear in this roster.
        var otherToken = await RegisterAndLoginAsync(NewEmail("ros-other"));
        await CreateHouseholdAsync(otherToken, "Other House");

        var otherId = await UserIdByEmailAsync(await EmailOfTokenAsync(otherToken));

        var roster = await GetRosterAsync(adminToken);

        // Exactly this household's two members — the other household's member never appears (US-02 scoping).
        Assert.Equal(2, roster.Length);
        Assert.DoesNotContain(roster, m => m.UserId == otherId);

        var adminRow = roster.Single(m => m.UserId == adminId);
        Assert.Equal("Admin", adminRow.Role);
        Assert.True(adminRow.IsSelf);
        Assert.False(adminRow.CanManage); // can't manage own row

        var memberRow = roster.Single(m => m.UserId == memberId);
        Assert.Equal("Member", memberRow.Role);
        Assert.False(memberRow.IsSelf);
        Assert.True(memberRow.CanManage); // admin can manage another member
    }

    [Fact]
    public async Task Roster_is_read_only_for_a_non_admin_member()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("ro-admin"));
        var householdId = await CreateHouseholdAsync(adminToken, "ReadOnly House");

        var memberEmail = NewEmail("ro-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail);
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);

        var roster = await GetRosterAsync(memberToken);

        Assert.Equal(2, roster.Length);
        Assert.All(roster, m => Assert.False(m.CanManage)); // a member can manage no one
    }

    [Fact]
    public async Task Roster_for_a_user_without_a_household_returns_404()
    {
        var token = await RegisterAndLoginAsync(NewEmail("ros-nohouse"));

        var resp = await _client.SendAsync(Authed(HttpMethod.Get, "/api/households/members", token));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // --- Role change ---------------------------------------------------------------------------------

    [Fact]
    public async Task Promote_then_demote_round_trips_the_role()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("rt-admin"));
        var householdId = await CreateHouseholdAsync(adminToken, "RoundTrip House");

        var memberEmail = NewEmail("rt-member");
        await RegisterAndLoginAsync(memberEmail);
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);
        var memberId = await UserIdByEmailAsync(memberEmail);

        var promote = await SetRoleAsync(adminToken, memberId, "Admin");
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);
        Assert.Equal("Admin", (await promote.Content.ReadFromJsonAsync<MemberBody>())!.Role);

        var demote = await SetRoleAsync(adminToken, memberId, "Member");
        Assert.Equal(HttpStatusCode.OK, demote.StatusCode);
        Assert.Equal("Member", (await demote.Content.ReadFromJsonAsync<MemberBody>())!.Role);
    }

    [Fact]
    public async Task Setting_the_role_a_member_already_has_is_an_idempotent_200()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("idem-admin"));
        var householdId = await CreateHouseholdAsync(adminToken, "Idem House");

        var memberEmail = NewEmail("idem-member");
        await RegisterAndLoginAsync(memberEmail);
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);
        var memberId = await UserIdByEmailAsync(memberEmail);

        var resp = await SetRoleAsync(adminToken, memberId, "Member");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("Member", (await resp.Content.ReadFromJsonAsync<MemberBody>())!.Role);
    }

    [Fact]
    public async Task Role_change_with_an_undefined_role_value_returns_400()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("badrole-admin"));
        var householdId = await CreateHouseholdAsync(adminToken, "Bad Role House");

        var memberEmail = NewEmail("badrole-member");
        await RegisterAndLoginAsync(memberEmail);
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);
        var memberId = await UserIdByEmailAsync(memberEmail);

        // "5" parses via Enum.TryParse but is not a defined HouseholdRole — must be rejected, not persisted.
        var resp = await SetRoleAsync(adminToken, memberId, "5");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_role_change_returns_403()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("na-admin"));
        var householdId = await CreateHouseholdAsync(adminToken, "NonAdmin House");
        var adminId = await UserIdByEmailAsync(await EmailOfTokenAsync(adminToken));

        var memberEmail = NewEmail("na-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail);
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);

        var resp = await SetRoleAsync(memberToken, adminId, "Member");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Self_role_change_returns_409()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("self-role-admin"));
        await CreateHouseholdAsync(adminToken, "Self Role House");
        var adminId = await UserIdByEmailAsync(await EmailOfTokenAsync(adminToken));

        var resp = await SetRoleAsync(adminToken, adminId, "Member");

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Role_change_for_a_foreign_user_returns_404()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("fr-admin"));
        await CreateHouseholdAsync(adminToken, "Foreign Role House");

        // A user in a different household — unknown to this household → 404 (no existence leak).
        var foreignEmail = NewEmail("fr-foreign");
        var foreignToken = await RegisterAndLoginAsync(foreignEmail);
        await CreateHouseholdAsync(foreignToken, "Foreign House");
        var foreignId = await UserIdByEmailAsync(foreignEmail);

        var resp = await SetRoleAsync(adminToken, foreignId, "Admin");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // --- Removal -------------------------------------------------------------------------------------

    [Fact]
    public async Task NonAdmin_remove_returns_403()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("rm-na-admin"));
        var householdId = await CreateHouseholdAsync(adminToken, "Remove NonAdmin House");
        var adminId = await UserIdByEmailAsync(await EmailOfTokenAsync(adminToken));

        var memberEmail = NewEmail("rm-na-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail);
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);

        var resp = await RemoveAsync(memberToken, adminId);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Self_remove_returns_409()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("self-rm-admin"));
        await CreateHouseholdAsync(adminToken, "Self Remove House");
        var adminId = await UserIdByEmailAsync(await EmailOfTokenAsync(adminToken));

        var resp = await RemoveAsync(adminToken, adminId);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Remove_of_a_foreign_user_returns_404()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("rm-fr-admin"));
        await CreateHouseholdAsync(adminToken, "Remove Foreign House");

        var foreignEmail = NewEmail("rm-fr-foreign");
        var foreignToken = await RegisterAndLoginAsync(foreignEmail);
        await CreateHouseholdAsync(foreignToken, "Foreign House 2");
        var foreignId = await UserIdByEmailAsync(foreignEmail);

        var resp = await RemoveAsync(adminToken, foreignId);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Remove_member_sweeps_in_progress_task_to_todo_and_drops_membership()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("sweep-admin"));
        var householdId = await CreateHouseholdAsync(adminToken, "Sweep House");

        var memberEmail = NewEmail("sweep-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail);
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);
        var memberId = await UserIdByEmailAsync(memberEmail);

        // Admin creates a task; the member claims it → it sits In progress, claimed by the member.
        var taskId = await CreateTaskAsync(adminToken, "Take out bins");
        (await ClaimAsync(memberToken, taskId)).EnsureSuccessStatusCode();

        var remove = await RemoveAsync(adminToken, memberId);
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Membership gone.
        Assert.False(await db.HouseholdMembers.AnyAsync(m => m.UserId == memberId));

        // The claimed task returned to To do, unassigned, with an Unclaimed event appended.
        var task = await db.HouseholdTasks.SingleAsync(t => t.Id == taskId);
        Assert.Equal(HouseholdTaskStatus.ToDo, task.Status);
        Assert.Null(task.ClaimedById);
        Assert.Null(task.ClaimedAtUtc);
        Assert.True(await db.TaskEvents.AnyAsync(e => e.TaskId == taskId && e.Type == TaskEventType.Unclaimed));
    }

    [Fact]
    public async Task Admin_can_remove_another_admin_while_one_remains()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("rm2-admin"));
        var householdId = await CreateHouseholdAsync(adminToken, "Two Admins House");

        // Promote a second member to admin, then remove them — the household keeps the original admin, so
        // the last-admin guard must NOT trip. Exercises the transactional remove-admin path.
        var otherEmail = NewEmail("rm2-other");
        await RegisterAndLoginAsync(otherEmail);
        await SeedMemberAsync(otherEmail, householdId, HouseholdRole.Member);
        var otherId = await UserIdByEmailAsync(otherEmail);
        (await SetRoleAsync(adminToken, otherId, "Admin")).EnsureSuccessStatusCode();

        var remove = await RemoveAsync(adminToken, otherId);

        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.HouseholdMembers.AnyAsync(m => m.UserId == otherId));
        Assert.Equal(1, await db.HouseholdMembers.CountAsync(m => m.HouseholdId == householdId && m.Role == HouseholdRole.Admin));
    }

    [Fact]
    public async Task Remove_member_leaves_a_closed_tasks_audit_attribution_intact()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("audit-admin"));
        var householdId = await CreateHouseholdAsync(adminToken, "Audit House");

        var memberEmail = NewEmail("audit-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail);
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);
        var memberId = await UserIdByEmailAsync(memberEmail);

        // The member claims + completes a task; the admin confirms it → closed (NFR-3 record).
        var taskId = await CreateTaskAsync(adminToken, "Closed chore");
        (await ClaimAsync(memberToken, taskId)).EnsureSuccessStatusCode();
        (await MarkDoneAsync(memberToken, taskId)).EnsureSuccessStatusCode();
        (await ConfirmAsync(adminToken, taskId)).EnsureSuccessStatusCode();

        (await RemoveAsync(adminToken, memberId)).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // The closed task keeps the (now-removed) member as claimer and stays closed — the sweep never touches it.
        var task = await db.HouseholdTasks.SingleAsync(t => t.Id == taskId);
        Assert.Equal(memberId, task.ClaimedById);
        Assert.NotNull(task.ClosedAtUtc);
    }

    // --- Auth ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Unauthenticated_roster_and_mutations_return_401()
    {
        var roster = await _client.GetAsync("/api/households/members");
        Assert.Equal(HttpStatusCode.Unauthorized, roster.StatusCode);

        var role = await _client.PostAsJsonAsync($"/api/households/members/{Guid.NewGuid():N}/role", new { role = "Admin" });
        Assert.Equal(HttpStatusCode.Unauthorized, role.StatusCode);

        var remove = await _client.DeleteAsync($"/api/households/members/{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Unauthorized, remove.StatusCode);
    }

    /// <summary>Recovers the signed-in email from the JWT so a token can be mapped back to its user id.</summary>
    private async Task<string> EmailOfTokenAsync(string token)
    {
        var resp = await _client.SendAsync(Authed(HttpMethod.Get, "/api/auth/me", token));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<MeBody>())!.Email!;
    }

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAtUtc);

    private sealed record HouseholdBody(Guid Id, string Name, string Role);

    private sealed record MemberBody(string UserId, string DisplayName, string Email, string Role, bool IsSelf, bool CanManage);

    private sealed record TaskBody(Guid Id);

    private sealed record MeBody(string? Sub, string? Email);
}
