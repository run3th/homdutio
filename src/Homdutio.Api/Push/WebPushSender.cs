using System.Net;
using System.Text.Json;
using Homdutio.Data;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using LibPushMessage = Lib.Net.Http.WebPush.PushMessage;
using LibPushSubscription = Lib.Net.Http.WebPush.PushSubscription;
using StoredSubscription = Homdutio.Data.Entities.PushSubscription;

namespace Homdutio.Api.Push;

/// <summary>
/// The live <see cref="IPushSender"/>: sends a VAPID-authenticated Web Push to every subscription the user
/// has registered, via <see cref="PushServiceClient"/> (Lib.Net.Http.WebPush). Registered only when a
/// <c>Vapid:PrivateKey</c> is configured; otherwise <see cref="NoOpPushSender"/> is used, mirroring the
/// <see cref="Homdutio.Api.Email.IEmailSender"/> pattern. Scoped because it touches the request-scoped
/// <see cref="ApplicationDbContext"/> both to read subscriptions and to prune dead ones.
/// </summary>
public sealed class WebPushSender : IPushSender
{
    private readonly ApplicationDbContext _db;
    private readonly PushServiceClient _client;
    private readonly VapidAuthentication _authentication;
    private readonly ILogger<WebPushSender> _logger;

    public WebPushSender(
        ApplicationDbContext db,
        PushServiceClient client,
        IOptions<VapidOptions> options,
        ILogger<WebPushSender> logger)
    {
        _db = db;
        _client = client;
        _logger = logger;

        var vapid = options.Value;
        _authentication = new VapidAuthentication(vapid.PublicKey, vapid.PrivateKey)
        {
            Subject = vapid.Subject,
        };
    }

    public async Task SendToUserAsync(string userId, PushMessage message, CancellationToken cancellationToken = default)
    {
        var subscriptions = await _db.PushSubscriptions
            .Where(s => s.UserId == userId)
            .ToListAsync(cancellationToken);

        if (subscriptions.Count == 0)
        {
            return;
        }

        // The Service Worker's push handler reads title/body/data.url from this JSON payload.
        var payload = JsonSerializer.Serialize(new
        {
            title = message.Title,
            body = message.Body,
            data = new { url = message.Url },
        });

        var dead = new List<StoredSubscription>();

        foreach (var subscription in subscriptions)
        {
            var target = new LibPushSubscription
            {
                Endpoint = subscription.Endpoint,
                Keys = new Dictionary<string, string>
                {
                    ["p256dh"] = subscription.P256dh,
                    ["auth"] = subscription.Auth,
                },
            };

            try
            {
                await _client.RequestPushMessageDeliveryAsync(
                    target,
                    new LibPushMessage(payload),
                    _authentication,
                    cancellationToken);

                subscription.LastSeenAtUtc = DateTime.UtcNow;
            }
            catch (PushServiceClientException ex) when (
                ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            {
                // The push service says this subscription is gone — prune it so the registry self-heals.
                dead.Add(subscription);
            }
            catch (Exception ex)
            {
                // Best-effort: any other failure (transport, bad key, timeout) is logged and swallowed so a
                // dead push service can never turn a successful task action into a 500.
                _logger.LogWarning(ex, "Web Push delivery failed for a subscription of user {UserId}.", userId);
            }
        }

        if (dead.Count > 0)
        {
            _db.PushSubscriptions.RemoveRange(dead);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
