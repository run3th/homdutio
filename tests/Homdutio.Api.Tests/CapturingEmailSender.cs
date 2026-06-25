using System.Collections.Concurrent;
using Homdutio.Api.Email;

namespace Homdutio.Api.Tests;

/// <summary>
/// Test <see cref="IEmailSender"/> that records the recipient + reset link the endpoint built instead of
/// sending. Lets integration tests assert a link/token was produced and replay it through reset-password.
/// </summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<(string Recipient, string Link)> _sent = new();

    public IReadOnlyCollection<(string Recipient, string Link)> Sent => _sent;

    public Task<bool> SendPasswordResetAsync(string recipientEmail, string resetLink, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue((recipientEmail, resetLink));
        return Task.FromResult(true);
    }
}
