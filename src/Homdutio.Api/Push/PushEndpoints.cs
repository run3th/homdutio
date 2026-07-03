using System.Security.Claims;
using Homdutio.Data;
using Homdutio.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homdutio.Api.Push;

/// <summary>
/// Web Push subscription endpoints. Unlike the household domain, these are <b>sub-scoped</b> (per-user): the
/// caller is resolved directly from the JWT <c>sub</c> claim (like <c>HouseholdEndpoints.cs:28</c>), not via
/// <c>HouseholdScope</c>, because a push subscription belongs to a user across households, not to a household.
/// Every route here is therefore registered in <c>RouteIsolationCoverageTests.Exempt</c> — there is no
/// foreign-household-id surface to sweep. The bearer interceptor already attaches the JWT, so no extra client
/// auth wiring is needed.
/// </summary>
public static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/push").RequireAuthorization();

        // GET /api/push/key — the VAPID public key for PushManager.subscribe, or a disabled marker when no
        // key is configured (no-op sender) so the client degrades gracefully instead of failing.
        group.MapGet("/key", (IOptions<VapidOptions> options) =>
        {
            var publicKey = options.Value.PublicKey;
            var enabled = !string.IsNullOrWhiteSpace(publicKey);
            return Results.Ok(new PushKeyResponse(enabled ? publicKey : null, enabled));
        });

        // POST /api/push/subscribe — upsert the caller's subscription by endpoint (re-subscribing the same
        // browser refreshes its keys + owner instead of duplicating the row).
        group.MapPost("/subscribe", async (SubscribeRequest request, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var userId = principal.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request.Endpoint)
                || request.Keys is null
                || string.IsNullOrWhiteSpace(request.Keys.P256dh)
                || string.IsNullOrWhiteSpace(request.Keys.Auth))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["subscription"] = ["A push subscription with an endpoint and p256dh/auth keys is required."],
                });
            }

            var now = DateTime.UtcNow;
            var deviceLabel = string.IsNullOrWhiteSpace(request.DeviceLabel) ? null : request.DeviceLabel!.Trim();

            var existing = await db.PushSubscriptions.SingleOrDefaultAsync(s => s.Endpoint == request.Endpoint);
            if (existing is null)
            {
                db.PushSubscriptions.Add(new PushSubscription
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Endpoint = request.Endpoint,
                    P256dh = request.Keys.P256dh,
                    Auth = request.Keys.Auth,
                    DeviceLabel = deviceLabel,
                    CreatedAtUtc = now,
                    LastSeenAtUtc = now,
                });
            }
            else
            {
                // Take ownership for the caller (endpoints can be re-issued to a new login on the same browser)
                // and refresh the keys + last-seen. Keep an existing label if the caller didn't supply one.
                existing.UserId = userId;
                existing.P256dh = request.Keys.P256dh;
                existing.Auth = request.Keys.Auth;
                existing.DeviceLabel = deviceLabel ?? existing.DeviceLabel;
                existing.LastSeenAtUtc = now;
            }

            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // DELETE /api/push/subscribe — remove the caller's subscription for an endpoint. Idempotent: a
        // missing endpoint (already gone / not the caller's) still returns 204.
        group.MapDelete("/subscribe", async ([FromBody] UnsubscribeRequest request, ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var userId = principal.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            if (!string.IsNullOrWhiteSpace(request.Endpoint))
            {
                var subscription = await db.PushSubscriptions
                    .SingleOrDefaultAsync(s => s.Endpoint == request.Endpoint && s.UserId == userId);
                if (subscription is not null)
                {
                    db.PushSubscriptions.Remove(subscription);
                    await db.SaveChangesAsync();
                }
            }

            return Results.NoContent();
        });

        // GET /api/push/devices — the caller's registered devices, the source for the Settings device list.
        // The client marks the current device by matching Endpoint against its own subscription.
        group.MapGet("/devices", async (ClaimsPrincipal principal, ApplicationDbContext db) =>
        {
            var userId = principal.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var devices = await db.PushSubscriptions
                .AsNoTracking()
                .Where(s => s.UserId == userId)
                .OrderBy(s => s.CreatedAtUtc)
                .Select(s => new PushDeviceResponse(s.Id, s.DeviceLabel, s.Endpoint, s.CreatedAtUtc))
                .ToListAsync();

            return Results.Ok(devices);
        });

        return app;
    }
}

/// <summary>The public VAPID key for the client, plus whether push is enabled (a key is configured).</summary>
public sealed record PushKeyResponse(string? PublicKey, bool Enabled);

/// <summary>The browser subscription keys (<c>PushSubscription.toJSON().keys</c>).</summary>
public sealed record SubscriptionKeys(string P256dh, string Auth);

/// <summary>Subscribe payload: the serialized <c>PushSubscription</c> plus a UA-derived device label.</summary>
public sealed record SubscribeRequest(string Endpoint, SubscriptionKeys? Keys, string? DeviceLabel);

/// <summary>Unsubscribe payload: the endpoint of the subscription to remove.</summary>
public sealed record UnsubscribeRequest(string Endpoint);

/// <summary>One registered device for the Settings list. <c>Endpoint</c> lets the client flag the current one.</summary>
public sealed record PushDeviceResponse(Guid Id, string? Label, string Endpoint, DateTime CreatedAtUtc);
