---
change_id: real-web-push
title: Real Web Push (VAPID) with a database-backed subscription registry
status: implemented
created: 2026-07-03
updated: 2026-07-03
archived_at: null
supersedes: push-notifications (the localStorage-only simulation)
---

## Notes

Replaces the client-side **simulation** shipped under `context/changes/push-notifications/`
(everything lived in `localStorage`, so a device enabled on a phone was invisible on
the desktop, and nothing was ever really delivered) with **real Web Push**:

- **Subscription registry in the database** — per-user push subscriptions persist
  server-side, so the device list is the same on every browser/device of the account.
- **Real delivery** — a browser Service Worker + `PushManager` subscription + a
  server-side VAPID sender push notifications that arrive even when the app is closed
  (Android / desktop Chrome, Edge, Firefox).
- **Server-side triggers** — assigning a task to someone, and commenting on a task
  someone is running, send a push to that person's registered devices.

### VAPID key configuration (Phase 1)
Push delivery needs a VAPID keypair. Non-secret values are committed in `appsettings.json`
under `Vapid` (`PublicKey`, `Subject`); the **private key is never committed**, mirroring
`Jwt:SigningKey`:
- **Dev**: `dotnet user-secrets set "Vapid:PrivateKey" "<key>" --project src/Homdutio.Api`
  (also set `Vapid:PublicKey` in user-secrets, or fill it in `appsettings.json`).
- **Prod**: set `Vapid:PrivateKey` (and `Vapid:PublicKey`/`Vapid:Subject`) as App Service
  application settings.

When `Vapid:PrivateKey` is absent the app registers `NoOpPushSender` and `GET /api/push/key`
returns `{ publicKey: null, enabled: false }`, so local dev and the test host run without a keypair.
Generate a keypair with any Web Push VAPID generator (e.g. `web-push generate-vapid-keys`).

### Explicit out-of-scope / accepted risk
- **iPhone / iOS is NOT covered in this change.** Safari on iOS only delivers Web Push
  to an app installed to the Home Screen as a PWA (iOS 16.4+). We deliberately chose
  "Android/desktop first" and did NOT add the PWA manifest/install flow, so push will
  not reach an iPhone yet. A follow-up change adds full PWA installability.
