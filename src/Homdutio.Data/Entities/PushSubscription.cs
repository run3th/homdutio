namespace Homdutio.Data.Entities;

/// <summary>
/// A persisted browser Web Push subscription — one row per subscribed browser/device, per user, so the
/// device registry is account-wide and survives across every browser the account signs into (the bug the
/// old <c>localStorage</c> simulation had: a device enabled on a phone was invisible on the desktop). Modeled
/// on <see cref="RefreshToken"/>: <see cref="UserId"/> is a raw <c>AspNetUsers.Id</c> value with no
/// navigation — mapping it as an FK would add a cascade path through AspNetUsers that is neither needed nor
/// wanted here. <see cref="Endpoint"/> (the push service URL the browser hands us) is the subscription's
/// identity, so re-subscribing the same browser upserts by it rather than creating a duplicate.
/// </summary>
public class PushSubscription
{
    public Guid Id { get; set; }

    /// <summary>The account this subscription belongs to (raw <c>AspNetUsers.Id</c>, no navigation).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>The push service URL (FCM/Mozilla/WNS). The unique identity of a subscription — a send targets it.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>The subscription's public key (<c>PushSubscription.getKey('p256dh')</c>, base64url).</summary>
    public string P256dh { get; set; } = string.Empty;

    /// <summary>The subscription's auth secret (<c>PushSubscription.getKey('auth')</c>, base64url).</summary>
    public string Auth { get; set; } = string.Empty;

    /// <summary>Human-readable device name derived from the User-Agent at subscribe time (for the Settings list).</summary>
    public string? DeviceLabel { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Refreshed on each re-subscribe; a proxy for "still alive" ahead of the push service's own pruning.</summary>
    public DateTime LastSeenAtUtc { get; set; }
}
