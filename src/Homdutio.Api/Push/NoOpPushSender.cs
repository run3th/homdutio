namespace Homdutio.Api.Push;

/// <summary>
/// Development/test stand-in for <see cref="IPushSender"/> selected when no <c>Vapid:PrivateKey</c> is
/// configured. Logs the notification instead of sending so <c>dotnet run</c> and the integration-test host
/// never reach out to a push service. Mirrors <see cref="Homdutio.Api.Email.NoOpEmailSender"/>.
/// </summary>
public sealed class NoOpPushSender(ILogger<NoOpPushSender> logger) : IPushSender
{
    public Task SendToUserAsync(string userId, PushMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Web Push disabled (no VAPID private key configured). Would notify {UserId}: {Title} — {Body} ({Url})",
            userId,
            message.Title,
            message.Body,
            message.Url);

        return Task.CompletedTask;
    }
}
