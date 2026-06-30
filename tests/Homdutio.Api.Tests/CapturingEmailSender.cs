using System.Collections.Concurrent;
using Homdutio.Api.Email;

namespace Homdutio.Api.Tests;

/// <summary>
/// Test <see cref="IEmailSender"/> that records what the endpoint built instead of sending. Lets
/// integration tests assert a reset/invite link was produced (and the invite's household/inviter values)
/// and replay it through the matching endpoint.
/// </summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<(string Recipient, string Link)> _sent = new();
    private readonly ConcurrentQueue<SentInvite> _invites = new();

    public IReadOnlyCollection<(string Recipient, string Link)> Sent => _sent;

    public IReadOnlyCollection<SentInvite> Invites => _invites;

    public Task<bool> SendPasswordResetAsync(string recipientEmail, string resetLink, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue((recipientEmail, resetLink));
        return Task.FromResult(true);
    }

    public Task<bool> SendInviteAsync(string recipientEmail, string inviteLink, string householdName, string inviterName, CancellationToken cancellationToken = default)
    {
        _invites.Enqueue(new SentInvite(recipientEmail, inviteLink, householdName, inviterName));
        return Task.FromResult(true);
    }

    public sealed record SentInvite(string Recipient, string Link, string HouseholdName, string InviterName);
}
