using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Homdutio.Data;
using Homdutio.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homdutio.Api.Tests;

/// <summary>
/// Locks the Phase-3 server-side push triggers (real-web-push): assigning a task notifies the assignee,
/// commenting notifies the task's runner, and both skip self-notification. Delivery is best-effort — a send
/// that throws must never fail the task action. Uses <see cref="AuthApiFactory.PushSender"/> (a capturing
/// fake) so the tests observe exactly who was notified without a real push service or VAPID key. Reuses the
/// register → login → create-household → bearer pattern and the direct-seed member trick from
/// <see cref="TaskEndpointsTests"/>. The sender is a class-fixture singleton, so each test resets it first.
/// </summary>
public class PushDeliveryTests : IClassFixture<AuthApiFactory>
{
    private const string Password = "P@ssw0rd!23";
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public PushDeliveryTests(AuthApiFactory factory)
    {
        factory.EnsureDatabaseMigrated();
        _factory = factory;
        _client = factory.CreateClient();
        _factory.PushSender.Reset();
    }

    // --- Helpers -------------------------------------------------------------------------------------

    private async Task<string> RegisterAndLoginAsync(string email, string displayName)
    {
        (await _client.PostAsJsonAsync("/api/auth/register", new { email, password = Password, displayName }))
            .EnsureSuccessStatusCode();

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

    private async Task<TaskBody> CreateTaskAsync(string token, string title, string? assigneeId = null)
    {
        object payload = assigneeId is null ? new { title } : new { title, assigneeId };
        var resp = await _client.SendAsync(Authed(HttpMethod.Post, "/api/tasks", token, payload));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TaskBody>())!;
    }

    private Task<HttpResponseMessage> ClaimAsync(string token, Guid id) =>
        _client.SendAsync(Authed(HttpMethod.Post, $"/api/tasks/{id}/claim", token));

    private Task<HttpResponseMessage> AssignAsync(string token, Guid id, string assigneeId) =>
        _client.SendAsync(Authed(HttpMethod.Post, $"/api/tasks/{id}/assign", token, new { assigneeId }));

    private Task<HttpResponseMessage> PostCommentAsync(string token, Guid id, string body) =>
        _client.SendAsync(Authed(HttpMethod.Post, $"/api/tasks/{id}/comments", token, new { body }));

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

    private async Task<string> UserIdAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.Users.SingleAsync(u => u.Email == email)).Id;
    }

    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    // --- Assignment triggers -------------------------------------------------------------------------

    [Fact]
    public async Task Assigning_a_task_pushes_to_the_assignee_with_a_task_deep_link()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("as-admin"), "Molly");
        var householdId = await CreateHouseholdAsync(adminToken, "Assign House");

        var memberEmail = NewEmail("as-member");
        await RegisterAndLoginAsync(memberEmail, "Arthur");
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);
        var memberId = await UserIdAsync(memberEmail);

        var task = await CreateTaskAsync(adminToken, "Take out bins");
        (await AssignAsync(adminToken, task.Id, memberId)).EnsureSuccessStatusCode();

        var sent = Assert.Single(_factory.PushSender.Notifications);
        Assert.Equal(memberId, sent.UserId);
        Assert.Contains("Molly", sent.Message.Body);
        Assert.Contains("Take out bins", sent.Message.Body);
        Assert.Equal($"/board?task={task.Id}", sent.Message.Url);
    }

    [Fact]
    public async Task Creating_a_task_with_an_assignee_pushes_to_the_assignee()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("cas-admin"), "Molly");
        var householdId = await CreateHouseholdAsync(adminToken, "Create-Assign House");

        var memberEmail = NewEmail("cas-member");
        await RegisterAndLoginAsync(memberEmail, "Arthur");
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);
        var memberId = await UserIdAsync(memberEmail);

        var task = await CreateTaskAsync(adminToken, "Fix the sink", assigneeId: memberId);

        var sent = Assert.Single(_factory.PushSender.Notifications);
        Assert.Equal(memberId, sent.UserId);
        Assert.Contains("Fix the sink", sent.Message.Body);
        Assert.Equal($"/board?task={task.Id}", sent.Message.Url);
    }

    [Fact]
    public async Task Self_assign_does_not_push()
    {
        var adminEmail = NewEmail("self-assign");
        var adminToken = await RegisterAndLoginAsync(adminEmail, "Solo");
        await CreateHouseholdAsync(adminToken, "Solo House");
        var adminId = await UserIdAsync(adminEmail);

        var task = await CreateTaskAsync(adminToken, "My own chore");
        (await AssignAsync(adminToken, task.Id, adminId)).EnsureSuccessStatusCode();

        Assert.Empty(_factory.PushSender.Notifications);
    }

    // --- Comment triggers ----------------------------------------------------------------------------

    [Fact]
    public async Task Commenting_on_a_claimed_task_pushes_to_the_runner_not_the_commenter()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("cm-admin"), "Molly");
        var householdId = await CreateHouseholdAsync(adminToken, "Comment House");

        var memberEmail = NewEmail("cm-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail, "Arthur");
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);
        var memberId = await UserIdAsync(memberEmail);

        var task = await CreateTaskAsync(adminToken, "Discuss me");
        (await ClaimAsync(memberToken, task.Id)).EnsureSuccessStatusCode();

        // Admin comments on the member's running task → the member (runner) is notified, not the admin.
        (await PostCommentAsync(adminToken, task.Id, "Nice work so far")).EnsureSuccessStatusCode();

        var sent = Assert.Single(_factory.PushSender.Notifications);
        Assert.Equal(memberId, sent.UserId);
        Assert.Equal("New comment", sent.Message.Title);
        Assert.Contains("Molly", sent.Message.Body);
        Assert.Contains("Discuss me", sent.Message.Body);
        Assert.Equal($"/board?task={task.Id}", sent.Message.Url);
    }

    [Fact]
    public async Task Self_comment_on_your_own_running_task_does_not_push()
    {
        var token = await RegisterAndLoginAsync(NewEmail("cm-self"), "Molly");
        await CreateHouseholdAsync(token, "Self Comment House");

        var task = await CreateTaskAsync(token, "I run this");
        (await ClaimAsync(token, task.Id)).EnsureSuccessStatusCode();

        (await PostCommentAsync(token, task.Id, "note to self")).EnsureSuccessStatusCode();

        Assert.Empty(_factory.PushSender.Notifications);
    }

    [Fact]
    public async Task Commenting_on_an_unclaimed_task_does_not_push()
    {
        var token = await RegisterAndLoginAsync(NewEmail("cm-unclaimed"), "Molly");
        await CreateHouseholdAsync(token, "Unclaimed Comment House");

        var task = await CreateTaskAsync(token, "Nobody owns me");
        (await PostCommentAsync(token, task.Id, "anyone?")).EnsureSuccessStatusCode();

        Assert.Empty(_factory.PushSender.Notifications);
    }

    // --- Best-effort delivery ------------------------------------------------------------------------

    [Fact]
    public async Task A_failing_push_does_not_fail_the_assign()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("be-assign-admin"), "Molly");
        var householdId = await CreateHouseholdAsync(adminToken, "Best Effort Assign House");

        var memberEmail = NewEmail("be-assign-member");
        await RegisterAndLoginAsync(memberEmail, "Arthur");
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);
        var memberId = await UserIdAsync(memberEmail);

        var task = await CreateTaskAsync(adminToken, "Assign despite dead push");
        _factory.PushSender.ThrowOnSend = true;

        var resp = await AssignAsync(adminToken, task.Id, memberId);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // The assignment still persisted — the swallowed push failure never touched the task action.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.HouseholdTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(memberId, row.ClaimedById);
        Assert.Equal(HouseholdTaskStatus.InProgress, row.Status);
    }

    [Fact]
    public async Task A_failing_push_does_not_fail_the_comment()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("be-cmt-admin"), "Molly");
        var householdId = await CreateHouseholdAsync(adminToken, "Best Effort Comment House");

        var memberEmail = NewEmail("be-cmt-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail, "Arthur");
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);

        var task = await CreateTaskAsync(adminToken, "Comment despite dead push");
        (await ClaimAsync(memberToken, task.Id)).EnsureSuccessStatusCode();
        _factory.PushSender.ThrowOnSend = true;

        var resp = await PostCommentAsync(adminToken, task.Id, "still lands");
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        // The comment still persisted.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.TaskComments.AnyAsync(c => c.TaskId == task.Id && c.Body == "still lands"));
    }

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAtUtc);

    private sealed record HouseholdBody(Guid Id, string Name, string Role);

    private sealed record TaskBody(Guid Id, string Title, string Status, string? ClaimerName);
}
