using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Homdutio.Data;
using Homdutio.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homdutio.Api.Tests;

/// <summary>
/// Locks the S-06 invite lifecycle and its correctness-sensitive guards: generate (admin + adult member),
/// public preview, accept-as-new-member, single-use re-consume rejection (FR-005), time-expiry, the
/// one-household-per-user block (FR-007), and cross-household token scoping (US-02 — a token grants access to
/// exactly one household and a foreign/unknown token grants none). Reuses <see cref="AuthApiFactory"/> and the
/// register → login → create-household → bearer pattern; a couple of tests reach into the DbContext to seed an
/// expired invite or a second member directly (the invite flow under test is the only join path otherwise).
/// </summary>
public class HouseholdInviteEndpointsTests : IClassFixture<AuthApiFactory>
{
    private const string Password = "P@ssw0rd!23";
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public HouseholdInviteEndpointsTests(AuthApiFactory factory)
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

    /// <summary>Registers with an explicit display name so the emailed-invite tests can assert the inviter name.</summary>
    private async Task<string> RegisterAndLoginWithNameAsync(string email, string displayName)
    {
        (await _client.PostAsJsonAsync("/api/auth/register", new { email, password = Password, displayName })).EnsureSuccessStatusCode();
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

    private async Task<InviteBody> GenerateInviteAsync(string token)
    {
        var resp = await _client.SendAsync(Authed(HttpMethod.Post, "/api/households/invites", token));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<InviteBody>())!;
    }

    private Task<HttpResponseMessage> AcceptAsync(string token, string inviteToken) =>
        _client.SendAsync(Authed(HttpMethod.Post, $"/api/households/invites/{inviteToken}/accept", token));

    /// <summary>Seeds a member row directly so non-admin / already-in-household paths can be exercised.</summary>
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

    /// <summary>Forces an invite's expiry into the past so the time-expiry branch can be exercised.</summary>
    private async Task ExpireInviteAsync(string inviteToken)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invite = await db.HouseholdInvites.SingleAsync(i => i.Token == inviteToken);
        invite.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
    }

    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    // --- Tests ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Generate_returns_token_and_future_expiry()
    {
        var token = await RegisterAndLoginAsync(NewEmail("gen"));
        await CreateHouseholdAsync(token, "Inviters");

        var invite = await GenerateInviteAsync(token);

        Assert.False(string.IsNullOrWhiteSpace(invite.Token));
        Assert.True(invite.ExpiresAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Adult_member_can_also_generate_an_invite()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("gen-admin"));
        var householdId = await CreateHouseholdAsync(adminToken, "Member Inviters");

        var memberEmail = NewEmail("gen-member");
        var memberToken = await RegisterAndLoginAsync(memberEmail);
        await SeedMemberAsync(memberEmail, householdId, HouseholdRole.Member);

        var resp = await _client.SendAsync(Authed(HttpMethod.Post, "/api/households/invites", memberToken));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task Preview_returns_household_name_and_inviter_for_a_valid_token()
    {
        var token = await RegisterAndLoginWithNameAsync(NewEmail("prev"), "Robin");
        await CreateHouseholdAsync(token, "Preview House");
        var invite = await GenerateInviteAsync(token);

        // Preview is public — no bearer.
        var resp = await _client.GetAsync($"/api/households/invites/{invite.Token}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var preview = await resp.Content.ReadFromJsonAsync<PreviewBody>();
        Assert.Equal("Preview House", preview!.HouseholdName);
        Assert.Equal("Robin", preview.InviterName);
        Assert.False(string.IsNullOrWhiteSpace(preview.InviterId));
    }

    [Fact]
    public async Task Preview_of_an_unknown_token_returns_404()
    {
        var resp = await _client.GetAsync($"/api/households/invites/{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Accept_joins_a_new_member_who_then_sees_the_household()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("acc-admin"));
        await CreateHouseholdAsync(adminToken, "Joinable");
        var invite = await GenerateInviteAsync(adminToken);

        var joinerToken = await RegisterAndLoginAsync(NewEmail("acc-joiner"));
        var accept = await AcceptAsync(joinerToken, invite.Token);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        var joined = await accept.Content.ReadFromJsonAsync<HouseholdBody>();
        Assert.Equal("Joinable", joined!.Name);
        Assert.Equal("Member", joined.Role);

        // The joiner now resolves the household via /me.
        var me = await _client.SendAsync(Authed(HttpMethod.Get, "/api/households/me", joinerToken));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var mine = await me.Content.ReadFromJsonAsync<HouseholdBody>();
        Assert.Equal(joined.Id, mine!.Id);
        Assert.Equal("Member", mine.Role);
    }

    [Fact]
    public async Task Second_accept_of_the_same_token_returns_410_and_creates_no_second_membership()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("once-admin"));
        var householdId = await CreateHouseholdAsync(adminToken, "Once Only");
        var invite = await GenerateInviteAsync(adminToken);

        var firstJoiner = await RegisterAndLoginAsync(NewEmail("once-first"));
        (await AcceptAsync(firstJoiner, invite.Token)).EnsureSuccessStatusCode();

        var secondJoiner = await RegisterAndLoginAsync(NewEmail("once-second"));
        var second = await AcceptAsync(secondJoiner, invite.Token);
        Assert.Equal(HttpStatusCode.Gone, second.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var memberCount = await db.HouseholdMembers.CountAsync(m => m.HouseholdId == householdId);
        Assert.Equal(2, memberCount); // admin + the first joiner only
    }

    [Fact]
    public async Task Expired_invite_returns_410_on_preview_and_accept()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("exp-admin"));
        await CreateHouseholdAsync(adminToken, "Expired House");
        var invite = await GenerateInviteAsync(adminToken);
        await ExpireInviteAsync(invite.Token);

        var preview = await _client.GetAsync($"/api/households/invites/{invite.Token}");
        Assert.Equal(HttpStatusCode.Gone, preview.StatusCode);

        var joiner = await RegisterAndLoginAsync(NewEmail("exp-joiner"));
        var accept = await AcceptAsync(joiner, invite.Token);
        Assert.Equal(HttpStatusCode.Gone, accept.StatusCode);
    }

    [Fact]
    public async Task Accept_while_already_in_a_household_returns_409_and_leaves_the_token_unconsumed()
    {
        var adminA = await RegisterAndLoginAsync(NewEmail("dup-a"));
        await CreateHouseholdAsync(adminA, "House A");
        var invite = await GenerateInviteAsync(adminA);

        // A second user who already created their own household tries to accept.
        var adminB = await RegisterAndLoginAsync(NewEmail("dup-b"));
        await CreateHouseholdAsync(adminB, "House B");

        var accept = await AcceptAsync(adminB, invite.Token);
        Assert.Equal(HttpStatusCode.Conflict, accept.StatusCode);

        // Token stays unconsumed — a genuinely free user can still use it.
        var joiner = await RegisterAndLoginAsync(NewEmail("dup-joiner"));
        Assert.Equal(HttpStatusCode.OK, (await AcceptAsync(joiner, invite.Token)).StatusCode);
    }

    [Fact]
    public async Task Unknown_token_accept_returns_404_and_grants_no_membership()
    {
        var joiner = await RegisterAndLoginAsync(NewEmail("unk-joiner"));
        var accept = await AcceptAsync(joiner, $"{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);

        var me = await _client.SendAsync(Authed(HttpMethod.Get, "/api/households/me", joiner));
        Assert.Equal(HttpStatusCode.NoContent, me.StatusCode); // still no household
    }

    [Fact]
    public async Task Accept_adds_the_caller_to_the_tokens_household_only()
    {
        var adminA = await RegisterAndLoginAsync(NewEmail("scope-a"));
        var householdA = await CreateHouseholdAsync(adminA, "Scope A");
        var inviteA = await GenerateInviteAsync(adminA);

        // A separate household B exists; its admin's invite is unrelated.
        var adminB = await RegisterAndLoginAsync(NewEmail("scope-b"));
        var householdB = await CreateHouseholdAsync(adminB, "Scope B");

        var joiner = await RegisterAndLoginAsync(NewEmail("scope-joiner"));
        var accept = await AcceptAsync(joiner, inviteA.Token);
        accept.EnsureSuccessStatusCode();
        var joined = await accept.Content.ReadFromJsonAsync<HouseholdBody>();

        Assert.Equal(householdA, joined!.Id);
        Assert.NotEqual(householdB, joined.Id);
    }

    [Fact]
    public async Task Unauthenticated_generate_and_accept_return_401_while_preview_is_anonymous()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("anon-admin"));
        await CreateHouseholdAsync(adminToken, "Anon House");
        var invite = await GenerateInviteAsync(adminToken);

        var generate = await _client.PostAsync("/api/households/invites", null);
        Assert.Equal(HttpStatusCode.Unauthorized, generate.StatusCode);

        var accept = await _client.PostAsync($"/api/households/invites/{invite.Token}/accept", null);
        Assert.Equal(HttpStatusCode.Unauthorized, accept.StatusCode);

        // Preview needs no auth.
        var preview = await _client.GetAsync($"/api/households/invites/{invite.Token}");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
    }

    [Fact]
    public async Task Generate_with_recipient_email_sends_invite_mail_with_link_household_and_inviter()
    {
        var adminToken = await RegisterAndLoginWithNameAsync(NewEmail("email-admin"), "Alex");
        await CreateHouseholdAsync(adminToken, "Emailed House");
        var recipient = NewEmail("invitee");

        var resp = await _client.SendAsync(Authed(HttpMethod.Post, "/api/households/invites", adminToken, new { recipientEmail = recipient }));
        resp.EnsureSuccessStatusCode();
        var invite = await resp.Content.ReadFromJsonAsync<InviteBody>();

        // Exactly one invite mail captured for this recipient, carrying the server-built /join/<token> link.
        var sent = Assert.Single(_factory.EmailSender.Invites, i => i.Recipient == recipient);
        Assert.EndsWith($"/join/{invite!.Token}", sent.Link);
        Assert.Equal("Emailed House", sent.HouseholdName);
        Assert.Equal("Alex", sent.InviterName);
    }

    [Fact]
    public async Task Generate_with_malformed_recipient_email_returns_400_and_mints_nothing()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("bademail-admin"));
        await CreateHouseholdAsync(adminToken, "Bad Email House");

        var resp = await _client.SendAsync(Authed(HttpMethod.Post, "/api/households/invites", adminToken, new { recipientEmail = "not-an-email" }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.DoesNotContain(_factory.EmailSender.Invites, i => i.Recipient == "not-an-email");
    }

    [Fact]
    public async Task Generate_without_recipient_email_sends_no_invite_mail()
    {
        var adminToken = await RegisterAndLoginAsync(NewEmail("noemail-admin"));
        await CreateHouseholdAsync(adminToken, "No Email House");

        var invite = await GenerateInviteAsync(adminToken); // no body → copy-link path

        Assert.False(string.IsNullOrWhiteSpace(invite.Token));
        Assert.DoesNotContain(_factory.EmailSender.Invites, i => i.Link.EndsWith($"/join/{invite.Token}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Emailed_invite_link_token_can_be_accepted_by_a_new_member()
    {
        var adminToken = await RegisterAndLoginWithNameAsync(NewEmail("link-admin"), "Sam");
        await CreateHouseholdAsync(adminToken, "Linkable By Mail");
        var recipient = NewEmail("link-invitee");

        var resp = await _client.SendAsync(Authed(HttpMethod.Post, "/api/households/invites", adminToken, new { recipientEmail = recipient }));
        resp.EnsureSuccessStatusCode();

        var sent = Assert.Single(_factory.EmailSender.Invites, i => i.Recipient == recipient);
        var emailedToken = sent.Link[(sent.Link.LastIndexOf("/join/", StringComparison.Ordinal) + "/join/".Length)..];

        var joiner = await RegisterAndLoginAsync(NewEmail("link-joiner"));
        var accept = await AcceptAsync(joiner, emailedToken);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
    }

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAtUtc);

    private sealed record HouseholdBody(Guid Id, string Name, string Role);

    private sealed record InviteBody(string Token, DateTime ExpiresAtUtc);

    private sealed record PreviewBody(string HouseholdName, string InviterName, string InviterId);
}

/// <summary>Lowers the invite rate limit so the per-user 429 can be asserted deterministically.</summary>
public sealed class InviteRateLimitedApiFactory : AuthApiFactory
{
    protected override int InvitePermitLimit => 3;
}

/// <summary>Invite minting is rate-limited per caller (429 after the configured threshold) — the with-email
/// path is an outbound-mail vector, so it is throttled like forgot-password but partitioned by user.</summary>
public class InviteRateLimitTests : IClassFixture<InviteRateLimitedApiFactory>
{
    private const string Password = "P@ssw0rd!23";
    private readonly HttpClient _client;

    public InviteRateLimitTests(InviteRateLimitedApiFactory factory)
    {
        factory.EnsureDatabaseMigrated();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Invite_minting_is_rate_limited_after_threshold()
    {
        var email = $"invite-rl-{Guid.NewGuid():N}@example.test";
        (await _client.PostAsJsonAsync("/api/auth/register", new { email, password = Password })).EnsureSuccessStatusCode();
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginBody>())!.AccessToken;

        HttpRequestMessage InvitePost()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/households/invites");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        }

        var create = new HttpRequestMessage(HttpMethod.Post, "/api/households");
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        create.Content = JsonContent.Create(new { name = "RL House" });
        (await _client.SendAsync(create)).EnsureSuccessStatusCode();

        // PermitLimit = 3 per window: the first three mints pass, the fourth (same user) is rejected.
        for (var i = 0; i < 3; i++)
        {
            var ok = await _client.SendAsync(InvitePost());
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        var limited = await _client.SendAsync(InvitePost());
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAtUtc);
}
