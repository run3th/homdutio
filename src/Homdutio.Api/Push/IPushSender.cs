namespace Homdutio.Api.Push;

/// <summary>
/// The Web Push send seam, mirroring <see cref="Homdutio.Api.Email.IEmailSender"/>. Sends one notification
/// to every push subscription a user has registered and prunes any the push service reports as gone. The
/// live <see cref="WebPushSender"/> is swapped for <see cref="NoOpPushSender"/> whenever no VAPID private
/// key is configured (local dev + tests), so nothing user-facing depends on a real keypair or the push
/// service being reachable. Callers treat delivery as best-effort — a send must never fail the task action
/// that triggered it.
/// </summary>
public interface IPushSender
{
    /// <summary>
    /// Delivers <paramref name="message"/> to all of <paramref name="userId"/>'s registered devices. Dead
    /// subscriptions (push service reports 404/410) are pruned as part of the send. Best-effort: individual
    /// delivery failures are logged and swallowed, never surfaced to the caller.
    /// </summary>
    Task SendToUserAsync(string userId, PushMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// A notification to deliver: a <paramref name="Title"/> and <paramref name="Body"/> for the OS/browser
/// notification, plus a <paramref name="Url"/> the Service Worker deep-links to on click (e.g.
/// <c>/board?task=&lt;id&gt;</c>).
/// </summary>
public sealed record PushMessage(string Title, string Body, string Url);
