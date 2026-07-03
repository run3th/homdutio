# Real Web Push (VAPID) with a DB-backed Subscription Registry — Implementation Plan

## Overview

Replace the merged **simulation** (`context/changes/push-notifications/`) with **real Web Push**. Today every "notification" is a `localStorage` fake: consent, the device registry (`homdutio_devices`), and the "push" toast all live in one browser, so a device enabled on a phone is invisible on the desktop and nothing is ever actually delivered. This change makes it real end-to-end:

1. **Backend push infrastructure** — a `PushSubscription` table (per-user, cross-device), VAPID config, an `IPushSender` (real + no-op), and `/api/push` endpoints to subscribe / unsubscribe / list devices / read the public key.
2. **Browser Service Worker + subscription** — a minimal `sw.js`, real `Notification.requestPermission()` + `PushManager.subscribe(...)`, with the subscription persisted to the backend; the Settings device list now reads from the database.
3. **Server-side triggers → delivery** — assigning a task and commenting on a task send a Web Push to the recipient's registered devices; clicking the notification deep-links to the task; dead subscriptions self-prune.

Activation is **phone-only** (a real push subscription is created only on a phone, detected by User-Agent); desktop can't activate — it shows the account's device list plus a **QR code** to open the app on a phone. Real push replaces the simulation's *fake* pieces (the `localStorage` registry and simulated OS prompt), but the phone-only gate and the QR are **retained** as a product decision. **iOS/iPhone is explicitly out of scope** (no PWA install flow this change — see Open Risks).

## Current State Analysis

**Everything push-related is a frontend simulation — zero real infrastructure (confirmed both sides).**

Frontend (`web/`, Angular 21, esbuild `@angular/build`, built into `../src/Homdutio.Api/wwwroot`):
- `NotificationService` (`web/src/app/notifications/notification.service.ts`) is fully `localStorage`-backed: `homdutio_notif_perm`, `homdutio_devices`, `homdutio_device_id` (`:38-40`). Public surface: `permission` (`:64`), `isMobile` (`:70`), `deviceList` (`:86`), `anyEnabled` (`:110`), `notifStatusText` (`:113`), `softAskDismissed` (`:79`); actions `requestNotifs()` (`:127`), `grant()` (`:135`), `deny()` (`:141`), `dismissSoftAsk()` (`:147`), `pushNotify()` (`:155`, delivers via `FlashService.push`).
- Consumers: `shell/settings-dialog/settings-dialog.component.ts` (device list + QR + `sendTest`, `:67-88`), `notifications/notif-banner/notif-banner.component.ts` (soft-ask/desktop banner, `:28-55`), `board/create-task/create-task.component.ts` (`pushNotify` on self-assign, `:115`), `board/task-detail/task-detail.component.ts` (`:136`), `notifications/system-prompt/*` (fake OS prompt), `notifications/deny-help/*` (unblock help), `notifications/qr/*`, `notifications/push-card/*`.
- **No** Service Worker, `manifest.webmanifest`, `ngsw-config.json`, or `@angular/service-worker` dependency (only a transitive optional peer in the lockfile). `web/public/` holds only `favicon.ico`. `web/src/index.html` (13 lines) has no manifest link.
- The **bearer interceptor** already attaches the JWT to every `/api/*` call (`web/src/app/auth/bearer.interceptor.ts:10-21`), so backend push calls need no extra auth wiring. Dev proxy `/api` → `:5252` (`web/proxy.conf.json`). No `web/src/environments/` and no build-time constants mechanism. `qrcode-generator` is already allowed (`angular.json:31`).
- Providers assembled in `web/src/app/app.config.ts:14-22` (`provideHttpClient`, `provideAppInitializer(restoreSession)`) — the SW registration provider goes here.

Backend (`src/`, .NET 9, EF Core + SQL Server, `IdentityDbContext`):
- `ApplicationDbContext` (`src/Homdutio.Data/ApplicationDbContext.cs:13`); DbSets at `:20-34` (`=> Set<T>()`); one fluent block per entity in `OnModelCreating` (`:36`, `base` first at `:39`); enums stored as strings (`.HasConversion<string>().HasMaxLength(20)`).
- **`RefreshToken`** (`src/Homdutio.Data/Entities/RefreshToken.cs`) is the exact template for a per-user, no-navigation, indexed, hash/opaque-token entity (`UserId` raw `AspNetUsers.Id`, no FK — deliberate, avoids cascade paths; config block `ApplicationDbContext.cs:166-183` shows unique index on the lookup key + per-user index).
- **Migrations** are manual, additive, backward-compatible; NO startup auto-migration (`src/Homdutio.Data/MIGRATIONS.md`; verified no `Database.Migrate()` in `Program.cs`). Commands require `--project src/Homdutio.Data --startup-project src/Homdutio.Api`.
- **Config/secrets** pattern: strongly-typed options bound from a section — `JwtOptions` (`src/Homdutio.Api/Auth/JwtOptions.cs`, bound `Program.cs:66`), non-secret defaults in `appsettings.json`, secret injected out-of-band (user-secrets / App Service settings). `AppBaseUrl` already in `appsettings.json:15`.
- **Outbound-send** pattern: `IEmailSender` + real `AcsEmailSender` / `NoOpEmailSender`, selected by config at `Program.cs:107-116` — the exact template for `IPushSender` + `NoOpPushSender`.
- **Endpoints**: per-feature `static class` with `MapXEndpoints(this IEndpointRouteBuilder)`, `app.MapGroup("/api/xxx").RequireAuthorization()`, invoked in `Program.cs:181-185`. Caller identity via `HouseholdScope.ResolveCallerAsync` (`src/Homdutio.Api/HouseholdScope.cs:25`, reads JWT `sub`); raw `sub` read directly where household scope isn't needed (`HouseholdEndpoints.cs:28`). JWT config `Program.cs:66-88` (`MapInboundClaims = false`, raw `sub`).
- **Existing triggers to hook**: `POST /api/tasks/{id}/assign` and create-with-assignee (`src/Homdutio.Api/Tasks/TaskEndpoints.cs`), and `POST /api/tasks/{id}/comments` (`TaskEndpoints.cs`). `HouseholdTask.ClaimedById` identifies who is running a task.
- **Route-coverage guard**: `tests/Homdutio.Api.Tests/RouteIsolationCoverageTests.cs` asserts every `/api/*` route is Scoped or Exempt. `/api/push/*` is **per-user (sub-scoped), NOT household-scoped**, so each route goes in the **Exempt** set (`:43-55`, alongside `/api/profile/me`) — a new prefix that is neither Scoped nor Exempt fails the build.
- Backend test project exists: `tests/Homdutio.Api.Tests` (xUnit, `AuthApiFactory`).

### Key Discoveries:

- `RefreshToken.cs` + its config (`ApplicationDbContext.cs:166-183`) is a drop-in shape for `PushSubscription` (per-user, no-nav, unique index on endpoint).
- `IEmailSender`/`NoOpEmailSender` conditional registration (`Program.cs:107-116`) is the exact pattern for `IPushSender`/`NoOpPushSender` — dev with no VAPID key runs the no-op and never fails.
- The bearer interceptor already authorizes `/api/push/*` calls — no new client auth code.
- New `/api/push/*` routes must be added to `RouteIsolationCoverageTests.Exempt` (`:43-55`) or the coverage test fails the build.
- Real push stays gated to phones (product decision): the phone gate (now **UA-based**) and the QR are retained; only the *fake* `localStorage` registry and simulated OS prompt are removed (real `PushManager` subscription + the real permission prompt replace them).

## Desired End State

- On a supported **phone** browser, a user clicks "Enable notifications", the real browser permission prompt appears, and on grant the device is registered server-side. The device shows in Settings on **that** phone **and on every other browser/device** the user logs into (registry is in the DB). **Desktop cannot activate** — it shows the device list plus a QR to enable from a phone, with no enable button.
- When an admin assigns a task to a member, that member's registered devices receive a real OS/browser push ("New task assigned to you — …") even if the app is closed; clicking it opens the app focused on that task. Same for a comment on a task the user is running.
- Disabling on a device removes its subscription from the DB (and it disappears from the list everywhere). Subscriptions that the push service reports as gone (404/410) are pruned automatically on the next send.
- **Verification:** `dotnet build src/Homdutio.Api -c Release` and `cd web && npm run build` succeed; `dotnet test` and `cd web && npm test` + `npm run lint` pass; with a real VAPID keypair configured, the assign/comment flows deliver a push to a subscribed Chrome/Android/desktop browser and the click deep-links to the task.

## What We're NOT Doing

- **No iOS/iPhone support** and **no PWA install pipeline** this change (no `manifest.webmanifest`, no icons, no "Add to Home Screen" flow). iOS Web Push requires an installed PWA; that is a deliberate follow-up. This is the one accepted gap.
- **No `@angular/service-worker`/ngsw** — a minimal hand-written `sw.js` handles only `push` + `notificationclick`; no app-shell caching (avoids wwwroot/cache-busting conflicts).
- **No background job / queue** — delivery is inline, best-effort within the triggering request (no `BackgroundService`; none exists in the app).
- **No new push triggers beyond assign + comment** (send-back / confirm / due-reminders are out of scope this change).
- **No Azure Key Vault** — the VAPID private key lives in user-secrets (dev) / App Service application settings (prod), matching the existing `Jwt:SigningKey` handling.
- **No changes to the design reference** `templates/Homdutio Pro.html`.
- **No re-litigating task assignment** — assignment (endpoint, picker, audit) already shipped in `push-notifications` and is reused as-is; only its notification side-effect changes.

## Implementation Approach

Build backend-first so the client has a real API to subscribe against, then swap the client from simulation to real Service Worker + backend registry, then wire the server-side triggers that actually deliver. Follow existing conventions throughout: EF entity + fluent config + additive migration (per `MIGRATIONS.md`); options-from-config + out-of-band secret (like `JwtOptions`); interface + real/no-op sender selected by config (like `IEmailSender`); `MapGroup("/api/push").RequireAuthorization()` endpoints resolving the caller via the JWT `sub`; delivery inline and best-effort so a push failure never breaks the task action.

The web-push library is **`Lib.Net.Http.WebPush`** (actively maintained, `PushServiceClient` + `VapidAuthentication`, integrates with `HttpClient`); if it proves unsuitable, `WebPush` (the web-push-libs .NET port) is the fallback with the same `IPushSender` seam so nothing else changes.

## Critical Implementation Details

- **Push send must be best-effort and must never fail the task action.** In the assign/comment handlers, persist the task change first (as today), then attempt delivery in a try/catch that logs and swallows; a dead push service or missing VAPID key must not turn a successful assignment into a 500.
- **Dead-subscription pruning is part of send, not a separate job.** When `Lib.Net.Http.WebPush` surfaces a 404/410 (`WebPushException` with `Gone`/`NotFound`), delete that subscription row inside the same send path so the registry self-heals.
- **`/api/push/*` is sub-scoped, not household-scoped.** Resolve the user from the JWT `sub` directly (like `HouseholdEndpoints.cs:28`), NOT via `HouseholdScope`, and register each route in `RouteIsolationCoverageTests.Exempt` (`:43-55`) — otherwise the coverage guard fails the build.
- **Comment push goes to the task's runner, not the commenter.** Send to `HouseholdTask.ClaimedById` and skip when the commenter is the claimer (no self-notify) and when the task is unclaimed.
- **Service Worker scope + freshness.** `sw.js` is served from the wwwroot root (`web/public/sw.js` → copied to output root, `angular.json:32-37`) so its scope is `/`. It must contain no app-shell caching; only `push` and `notificationclick` handlers, so a stale SW can never serve stale app assets.
- **VAPID public key reaches the client at runtime.** The client fetches it from `GET /api/push/key` (there is no `environments/` build-constant mechanism, and this keeps the key in one place) before calling `PushManager.subscribe`.

---

## Phase 1: Backend push infrastructure (registry + sender + endpoints)

### Overview

Add the DB-backed subscription registry, VAPID configuration, the push sender abstraction (real + no-op), and the `/api/push` endpoints. No delivery triggers yet — this phase stands up the surface the client subscribes against and proves it with tests.

### Changes Required:

#### 1. `PushSubscription` entity + DbSet + config + migration

**File**: `src/Homdutio.Data/Entities/PushSubscription.cs` (new), `src/Homdutio.Data/ApplicationDbContext.cs`, `src/Homdutio.Data/Migrations/` (new migration)

**Intent**: Persist one row per browser push subscription, per user, so the device registry is account-wide and survives across browsers/devices — modeled on `RefreshToken`.

**Contract**: New POCO `PushSubscription` with `Guid Id`; `string UserId` (raw `AspNetUsers.Id`, no navigation); `string Endpoint` (the push service URL — the unique identity of a subscription); `string P256dh` and `string Auth` (the subscription's public key + auth secret from `PushSubscription.getKey`); `string? DeviceLabel` (human name derived from UA at subscribe time); `DateTime CreatedAtUtc`; `DateTime LastSeenAtUtc`. Add `DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>()` (~`ApplicationDbContext.cs:35`) and a fluent config block (~`:184`): unique index on `Endpoint`, index on `UserId`, sensible `HasMaxLength` on string columns. Add an additive migration via the `MIGRATIONS.md` command (`dotnet ef migrations add AddPushSubscriptions --project src/Homdutio.Data --startup-project src/Homdutio.Api`).

#### 2. `VapidOptions` + configuration

**File**: `src/Homdutio.Api/Push/VapidOptions.cs` (new), `src/Homdutio.Api/appsettings.json`, `Program.cs`

**Intent**: Hold the VAPID public key + subject in config and the private key out-of-band, mirroring `JwtOptions`.

**Contract**: Sealed `VapidOptions { const string SectionName = "Vapid"; string PublicKey; string PrivateKey; string Subject; }`. In `appsettings.json` add a `Vapid` section with `PublicKey` (safe to commit — it is public) and `Subject` (a `mailto:` or app URL); **omit `PrivateKey`** (secret, like `Jwt:SigningKey`). Bind with `builder.Services.Configure<VapidOptions>(...GetSection(VapidOptions.SectionName))` near `Program.cs:66`. Document in `change.md`/README that the dev private key goes in user-secrets and prod in App Service settings.

#### 3. `IPushSender` + real + no-op, conditional registration

**File**: `src/Homdutio.Api/Push/IPushSender.cs`, `WebPushSender.cs`, `NoOpPushSender.cs` (new), `Program.cs`, `src/Homdutio.Api/Homdutio.Api.csproj`

**Intent**: A seam that sends a Web Push to all of a user's subscriptions and prunes dead ones — real when a private key is configured, no-op otherwise (dev/test), mirroring `IEmailSender`.

**Contract**: `IPushSender.SendToUserAsync(string userId, PushMessage message, CancellationToken)` where `PushMessage` carries `Title`, `Body`, and a `Url`/`data` payload for the deep-link. `WebPushSender` (scoped — touches the DbContext) loads the user's subscriptions, sends each via `Lib.Net.Http.WebPush` (`PushServiceClient` + `VapidAuthentication` from `VapidOptions`), and on a `Gone`/`NotFound` `WebPushException` deletes that row; all other errors are logged and swallowed. `NoOpPushSender` logs and returns. Register conditionally at `Program.cs:107-116`-style: real when `Vapid:PrivateKey` is present, else no-op. Add the `Lib.Net.Http.WebPush` PackageReference to the API csproj.

#### 4. `/api/push` endpoints + route-coverage exemption

**File**: `src/Homdutio.Api/Push/PushEndpoints.cs` (new), `Program.cs`, `tests/Homdutio.Api.Tests/RouteIsolationCoverageTests.cs`

**Intent**: Let a signed-in client register/remove its subscription, list the account's devices, and read the public VAPID key.

**Contract**: `MapPushEndpoints(this IEndpointRouteBuilder)`, `var group = app.MapGroup("/api/push").RequireAuthorization()`, invoked among `Program.cs:181-185`. Routes:
- `GET /api/push/key` → `{ publicKey }` from `VapidOptions` (returns empty/disabled marker when no key so the client can degrade gracefully).
- `POST /api/push/subscribe` `{ endpoint, keys:{ p256dh, auth }, deviceLabel? }` → upsert by `Endpoint` for the caller's `sub`; sets `LastSeenAtUtc`.
- `DELETE /api/push/subscribe` `{ endpoint }` → delete that row if it belongs to the caller.
- `GET /api/push/devices` → the caller's subscriptions as a device list (`{ id, label, isCurrent?, createdAtUtc }`) — the source for the Settings list. Resolve the caller from the JWT `sub` directly (not `HouseholdScope`). Add all four route keys to `RouteIsolationCoverageTests.Exempt` (`:43-55`) with a one-line "sub-scoped, per-user" justification.

#### 5. Backend tests

**File**: `tests/Homdutio.Api.Tests/PushEndpointsTests.cs` (new)

**Intent**: Lock the endpoint contract and the coverage exemption.

**Contract**: Subscribe persists a row for the caller; a second subscribe with the same endpoint upserts (no duplicate); `GET /devices` returns only the caller's rows (not another user's); `DELETE` removes only the caller's row; unauthenticated calls are 401; the route-coverage test still passes with the new Exempt entries.

### Success Criteria:

#### Automated Verification:

- Migration is additive and applies: `dotnet ef database update --project src/Homdutio.Data --startup-project src/Homdutio.Api`
- Backend builds: `dotnet build src/Homdutio.Api -c Release`
- Backend tests pass (incl. `PushEndpointsTests` + route-coverage): `dotnet test`

#### Manual Verification:

- With no `Vapid:PrivateKey`, the API starts and `GET /api/push/key` reports "disabled" (no crash; no-op sender registered).
- With a dev VAPID keypair in user-secrets, `GET /api/push/key` returns the public key; a manual `POST /api/push/subscribe` persists a row and `GET /api/push/devices` returns it.

**Implementation Note**: After automated verification passes, pause for human confirmation of the manual steps before Phase 2.

---

## Phase 2: Browser Service Worker + real subscription + DB-backed registry

### Overview

Swap the client from simulation to real Web Push: register a minimal Service Worker, request real permission, subscribe via `PushManager`, persist the subscription to the backend, and render the Settings device list from `/api/push/devices`. Rip out the `localStorage` registry and the fake OS prompt; **keep** the phone-only gate (now UA-based) and the QR (desktop shows it to enable from a phone).

### Changes Required:

#### 1. Minimal Service Worker

**File**: `web/public/sw.js` (new)

**Intent**: Receive push events and route clicks to the task — nothing else (no caching).

**Contract**: `push` handler reads the JSON payload (`title`, `body`, `data.url`) and calls `self.registration.showNotification(...)`. `notificationclick` handler focuses an existing app client if one is open (and navigates it to `data.url`) or opens a new window at `data.url`. No `fetch`/`install`-cache logic. Served from wwwroot root (scope `/`).

#### 2. Register the Service Worker

**File**: `web/src/app/app.config.ts` (or a small `provideAppInitializer`), `web/src/index.html` if needed

**Intent**: Register `sw.js` once at startup where the browser supports it.

**Contract**: On bootstrap, if `'serviceWorker' in navigator`, register `/sw.js`. Keep it out of the way of the dev proxy. No ngsw.

#### 3. Rewrite `NotificationService` to real push + backend registry

**File**: `web/src/app/notifications/notification.service.ts`, new `web/src/app/notifications/push-api.service.ts` (or fold into the service)

**Intent**: Make the service a thin real-push adapter over the browser APIs + the backend, preserving the public signal/method names the consumers already use so the rewire is contained.

**Contract**: Replace the `localStorage` registry and `isMobile` gate with:
- `permission` signal sourced from `Notification.permission` (+ live updates after a request).
- `supported` computed (`'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window`) AND `isMobile` (UA-based) — the enable affordance requires **both** (phone-only): `canActivate = supported && isMobile`.
- `enable()` (replaces `requestNotifs`/`grant`): no-op unless `canActivate`; else `Notification.requestPermission()` → on `granted`, get the SW registration, fetch the public key from `GET /api/push/key`, `pushManager.subscribe({ userVisibleOnly:true, applicationServerKey })`, `POST /api/push/subscribe` with the serialized subscription + a UA-derived `deviceLabel`.
- `disable()` (replaces `deny`'s registry side): `pushManager.getSubscription()?.unsubscribe()` + `DELETE /api/push/subscribe`.
- `devices` signal from `GET /api/push/devices` (marks the current endpoint `isCurrent`); `notifStatusText` derived from it.
- Remove `pushNotify` (delivery is now server-side) — its callers move to Phase 3.

#### 4. Rewire consumers; delete simulation-only pieces

**File**: `web/src/app/shell/settings-dialog/settings-dialog.component.*`, `web/src/app/notifications/notif-banner/notif-banner.component.*`, delete `web/src/app/notifications/system-prompt/*` and `web/src/app/notifications/push-card/*`; **keep** `web/src/app/notifications/qr/*` and `deny-help/*`

**Intent**: Point the UI at the real service while keeping the phone-only + QR model.

**Contract**: Settings "Notifications" section: show `notifStatusText`, render `devices` (with THIS DEVICE badge on the current endpoint and a "Remove" action → `disable()` for that device). On a **phone**: an "Enable notifications" button when `canActivate && permission !== 'granted'` (when `permission === 'denied'` show the deny-help link), plus the install hint when not yet granted. On **desktop**: no enable button — show the "Turn on from your phone" QR frame alongside the (server-side) device list. Board banner: phone soft-ask (Enable → `enable()`) when `canActivate && permission === 'default'` and no current subscription; desktop informational banner (QR reference, **no** activation CTA) when nothing is enabled. Delete the fake `system-prompt` and `push-card`; keep `qr` (real scannable code) and `deny-help`. `sendTest`/in-app preview is removed (real push has no client-side toast).

#### 5. Frontend tests

**File**: colocated `*.spec.ts` for `NotificationService` and the rewired components

**Intent**: Cover the real adapter with the browser APIs mocked (no real push in CI).

**Contract**: Mock `navigator.serviceWorker`, `Notification`, `PushManager`, and `HttpClient`. Assert: `enable()` requests permission, subscribes, and POSTs the subscription; `disable()` unsubscribes and DELETEs; `supported` gates the button; `devices` renders the list and marks the current device. Remove obsolete simulation specs.

### Success Criteria:

#### Automated Verification:

- Frontend builds & type-checks: `cd web && npm run build`
- Unit tests pass: `cd web && npm test`
- Lint passes: `cd web && npm run lint`

#### Manual Verification:

- On a phone browser with a dev VAPID key configured: clicking "Enable notifications" shows the real browser prompt; granting registers the SW and persists a subscription (visible in `GET /api/push/devices`).
- Logging into the same account on desktop shows the phone's device in the Settings list (registry is server-side) — the original bug is fixed.
- "Remove" on a device unsubscribes it (locally if it's this browser) and it disappears from the list everywhere.
- Desktop shows the QR "Turn on from your phone" frame + the device list and **no** enable button; the fake OS prompt no longer appears.

**Implementation Note**: Pause for human confirmation before Phase 3.

---

## Phase 3: Server-side triggers, delivery & deep-link

### Overview

Actually deliver: hook the existing assign and comment endpoints to send a Web Push to the recipient's devices, deep-link the notification click to the task, and confirm dead-subscription pruning. This closes the loop the simulation only faked.

### Changes Required:

#### 1. Push on task assignment

**File**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs` (the `assign` handler and the create-with-assignee handler)

**Intent**: Notify the assignee's registered devices when a task is assigned to them.

**Contract**: After the task change is persisted (today's logic), call `IPushSender.SendToUserAsync(assigneeId, new PushMessage("New task assigned to you", "<assigner> assigned you \"<title>\".", url: <task deep-link>), ct)` inside a try/catch that logs and swallows. Skip the send when the assignee is the assigner (self-assign needs no push). The deep-link `url` targets the board focused on the task (e.g. `/board?task=<id>`).

#### 2. Push on comment

**File**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs` (the `POST /api/tasks/{id}/comments` handler)

**Intent**: Notify the task's runner when someone comments on their task.

**Contract**: After the comment is persisted, if the task has a `ClaimedById` and it is not the commenter, `SendToUserAsync(task.ClaimedById, new PushMessage("New comment", "<commenter> commented on \"<title>\".", url: <task deep-link>), ct)` (try/catch, best-effort). No send for unclaimed tasks or self-comments.

#### 3. App-side deep-link to a task

**File**: `web/src/app/board/board.component.ts`/routing (wherever the board reads query params), and the SW `notificationclick` target from Phase 2

**Intent**: Make clicking a notification open the app focused on the referenced task.

**Contract**: The board reads a `task=<id>` query param on load and opens that task's detail panel (reusing `TaskDetailComponent`) if the task is in the caller's board. The SW `notificationclick` (Phase 2) navigates to `data.url` which carries that param. If the task isn't found (e.g. already gone), fall back to the board with no error.

#### 4. Delivery tests

**File**: `tests/Homdutio.Api.Tests/` (extend assign/comment coverage with a fake `IPushSender`)

**Intent**: Prove the triggers call the sender with the right recipient and never break the action.

**Contract**: Register a capturing fake `IPushSender` in the test host. Assert: assigning to a member calls `SendToUserAsync(assignee, …)`; self-assign does not; commenting on a claimed task notifies the claimer but not the commenter; an unclaimed task or self-comment sends nothing; a sender that throws does NOT fail the assign/comment request (best-effort). Pruning: a `Gone`/`NotFound` from the send path deletes the subscription row (unit test on `WebPushSender` with a mocked push client, or documented as manual if the client isn't easily mockable).

### Success Criteria:

#### Automated Verification:

- Backend builds & tests pass (incl. new trigger tests): `dotnet build src/Homdutio.Api -c Release && dotnet test`
- Frontend builds, tests, lint: `cd web && npm run build && npm test && npm run lint`

#### Manual Verification:

- With a real VAPID keypair: a subscribed desktop Chrome receives a real notification when another user assigns it a task — including when the tab is closed — and clicking it opens the app focused on that task.
- Commenting on a task you are running notifies you on your other subscribed device; commenting on your own task does not notify yourself.
- Unsubscribing a browser (or clearing its subscription) causes the next send to prune the dead row (it disappears from `GET /api/push/devices`).
- A forced push failure (e.g. wrong key) does not break assignment/commenting — the task action still succeeds.

**Implementation Note**: Pause for human confirmation; this is the final phase.

---

## Testing Strategy

### Unit Tests:

- **Backend (xUnit, `tests/Homdutio.Api.Tests`)**: push endpoint contract (subscribe upsert, own-only devices list, delete-own, 401 unauth), route-coverage exemption, and trigger behavior (assign/comment recipient rules, best-effort swallow) via a capturing fake `IPushSender`.
- **Frontend (Vitest, colocated)**: `NotificationService` with `navigator.serviceWorker`/`Notification`/`PushManager`/`HttpClient` mocked (enable→subscribe→POST; disable→unsubscribe→DELETE; `supported` gating; device list render).

### Integration Tests:

- Endpoint tests run against the `AuthApiFactory` host (real routing + auth), covering cross-user isolation of the device list.

### Manual Testing Steps:

1. Configure a dev VAPID keypair (user-secrets). Enable notifications in desktop Chrome → device persists; verify it appears in a second browser's Settings (fixes the reported bug).
2. As an admin (browser A), assign a task to the user signed in on browser B → B receives a real push (tab closed); click deep-links to the task.
3. Comment on a task the other user is running → they get a push; comment on your own task → no self-notify.
4. Remove a device / clear its subscription → next send prunes it.
5. Break the key deliberately → assignment still succeeds (best-effort).

## Performance Considerations

Negligible for a single household. Inline send adds one round-trip per subscription to the push service on assign/comment; with fan-out to a handful of devices this is a few hundred ms, absorbed by the best-effort try/catch (a slow/failed push service never blocks the response beyond the client timeout, and errors are swallowed). Keep the `sw.js` cache-free so it stays tiny.

## Migration Notes

- One additive migration (`AddPushSubscriptions`) — a new table, no changes to existing tables, backward-compatible per `MIGRATIONS.md`. Apply out-of-band to Azure SQL before/with the deploy; no down-time.
- **Prod prerequisite:** generate a VAPID keypair, set `Vapid:PublicKey`/`Vapid:Subject` (App Service settings or committed public value) and `Vapid:PrivateKey` (App Service settings, never the repo) before real delivery works; without the private key the API cleanly runs the no-op sender.
- The old simulation used only `localStorage` — no server data to migrate; removing it just drops client-side keys (`homdutio_notif_perm`, `homdutio_devices`, `homdutio_device_id`), which are harmless to leave orphaned.

## References

- Superseded simulation: `context/changes/push-notifications/plan.md` (esp. §H "Real push delivery" — a misnomer; that plan explicitly built no real push, `:57`)
- Entity template: `src/Homdutio.Data/Entities/RefreshToken.cs` + config `src/Homdutio.Data/ApplicationDbContext.cs:166-183`
- Options/secret pattern: `src/Homdutio.Api/Auth/JwtOptions.cs`, bound `Program.cs:66`
- Sender pattern: `IEmailSender`/`NoOpEmailSender`, registered `Program.cs:107-116`
- Endpoints + caller identity: `src/Homdutio.Api/HouseholdScope.cs:25`, group pattern `TaskEndpoints.cs:21-23`, direct `sub` read `HouseholdEndpoints.cs:28`
- Route-coverage guard / Exempt set: `tests/Homdutio.Api.Tests/RouteIsolationCoverageTests.cs:43-55`
- Migration workflow: `src/Homdutio.Data/MIGRATIONS.md`
- Client HTTP/auth: `web/src/app/auth/bearer.interceptor.ts:10-21`; providers `web/src/app/app.config.ts:14-22`; build/assets `web/angular.json:22-37`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend push infrastructure (registry + sender + endpoints)

#### Automated

- [x] 1.1 Migration applies: `dotnet ef database update --project src/Homdutio.Data --startup-project src/Homdutio.Api` — 044824f
- [x] 1.2 Backend builds: `dotnet build src/Homdutio.Api -c Release` — 044824f
- [x] 1.3 Backend tests pass (PushEndpointsTests + route-coverage): `dotnet test` — 044824f

#### Manual

- [x] 1.4 No `Vapid:PrivateKey` → API starts, `GET /api/push/key` reports disabled, no-op sender registered — 044824f
- [x] 1.5 With dev VAPID key → `/api/push/key` returns public key; manual subscribe persists; `/api/push/devices` returns it — 044824f

### Phase 2: Browser Service Worker + real subscription + DB-backed registry

#### Automated

- [x] 2.1 Frontend builds & type-checks: `cd web && npm run build`
- [x] 2.2 Unit tests pass: `cd web && npm test`
- [x] 2.3 Lint passes: `cd web && npm run lint`

#### Manual

- [x] 2.4 Phone browser + dev key: Enable shows real prompt; grant registers SW + persists subscription
- [x] 2.5 Desktop on the same account shows the phone device in Settings (registry is server-side — bug fixed)
- [x] 2.6 Remove device unsubscribes + disappears everywhere; desktop shows QR + device list + no enable button; no fake prompt

### Phase 3: Server-side triggers, delivery & deep-link

#### Automated

- [ ] 3.1 Backend builds & tests pass (trigger tests incl. best-effort + recipient rules): `dotnet build src/Homdutio.Api -c Release && dotnet test`
- [ ] 3.2 Frontend builds, tests, lint: `cd web && npm run build && npm test && npm run lint`

#### Manual

- [ ] 3.3 Real key: assignee receives a real push (tab closed); click deep-links to the task
- [ ] 3.4 Comment on a task you run → push to your other device; self-comment → no self-notify
- [ ] 3.5 Unsubscribed/dead subscription is pruned on the next send
- [ ] 3.6 Forced push failure does not break assignment/commenting (best-effort)
