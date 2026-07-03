using System.Collections.Concurrent;
using Homdutio.Api.Push;

namespace Homdutio.Api.Tests;

/// <summary>
/// Test <see cref="IPushSender"/> that records each send (recipient + message) instead of delivering, so the
/// trigger tests can assert exactly who was notified and with what. Mirrors <see cref="CapturingEmailSender"/>.
/// <see cref="ThrowOnSend"/> flips it into a failing sender so a test can prove delivery is best-effort — a
/// send failure must never break the assign/comment action that triggered it.
/// </summary>
public sealed class CapturingPushSender : IPushSender
{
    private readonly ConcurrentQueue<Sent> _sent = new();

    public IReadOnlyCollection<Sent> Notifications => _sent;

    /// <summary>When set, every send throws — exercises the handlers' best-effort try/catch.</summary>
    public bool ThrowOnSend { get; set; }

    public Task SendToUserAsync(string userId, PushMessage message, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSend)
        {
            throw new InvalidOperationException("Simulated push service failure.");
        }

        _sent.Enqueue(new Sent(userId, message));
        return Task.CompletedTask;
    }

    /// <summary>Reset between tests (the sender is a class-fixture singleton shared across the test class).</summary>
    public void Reset()
    {
        _sent.Clear();
        ThrowOnSend = false;
    }

    public sealed record Sent(string UserId, PushMessage Message);
}
