# Real Web Push (VAPID) with a DB-backed Subscription Registry — Plan Brief

> Full plan: `context/changes/real-web-push/plan.md`
> Supersedes: `context/changes/push-notifications/` (the localStorage-only simulation)

## What & Why

The shipped "push notifications" feature is a **simulation**: consent, the device registry, and the "push" toast all live in one browser's `localStorage`, so a device enabled on your phone is invisible on your computer and nothing is ever really delivered. This change makes it **real** — a database-backed subscription registry (visible on every browser/device of the account) and true Web Push delivered even when the app is closed.

## Starting Point

Zero real push infrastructure exists (confirmed both sides). Frontend: no Service Worker, no PWA manifest, no `@angular/service-worker`; `NotificationService` is `localStorage`-backed; the bearer interceptor already authorizes `/api/*`. Backend: .NET 9 + EF/SQL Server with clean templates to copy — `RefreshToken` (per-user entity), `JwtOptions` (config + out-of-band secret), `IEmailSender`/`NoOpEmailSender` (real-vs-noop outbound sender), and per-feature `MapGroup` endpoints. Task **assignment** and **comments** already exist as endpoints to hook.

## Desired End State

Enable notifications on any supported browser (desktop included) → the device registers server-side and shows in Settings on every browser/device you log into. Assigning a task, or commenting on a task someone is running, sends that person a real push (even with the app closed); clicking it opens the app focused on the task. Removing a device unsubscribes it; dead subscriptions self-prune.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Push approach | Real Web Push (VAPID) | The explicit ask — deliver to the device, not a same-tab fake | Plan |
| Registry location | Database (per-user `PushSubscription`) | So devices are visible across browsers/devices (the reported bug) | Plan |
| Trigger events | Assigned-to-you + comment-on-your-task | The two moments a person needs to know; others deferred | Plan |
| iOS / PWA | Out of scope this change | iOS needs an installed PWA; deliberately deferred to a follow-up | Plan |
| Service Worker | Minimal custom `sw.js` (push + click only) | Predictable, no app-shell caching to conflict with wwwroot/cache-busting | Plan |
| Activation surface | Any supported browser incl. desktop | Real push works on desktop; drop the simulation's phone-only + QR | Plan |
| VAPID keys | Public in appsettings, private out-of-band, NoOp in dev | Mirrors `Jwt:SigningKey`; dev runs with no keys | Plan |
| Fan-out & pruning | All devices + auto-prune 404/410 | Standard self-healing Web Push practice | Plan |
| Send timing | Inline, best-effort (never fails the action) | No background-job infra exists; simplest correct path | Plan |
| Simulation | Rip & replace | One source of truth; removes the confusion you hit | Plan |
| Deep-link | To the specific task | One click to the thing the notification is about | Plan |
| Tests | Backend xUnit + frontend Vitest; no real push in CI | Consistent with existing tests; push gesture verified manually | Plan |
| Push library | `Lib.Net.Http.WebPush` (`WebPush` fallback) | Maintained, `HttpClient`-integrated; `IPushSender` seam isolates it | Plan |

## Scope

**In scope:** `PushSubscription` table + migration; `VapidOptions`; `IPushSender` real + no-op; `/api/push` subscribe/unsubscribe/devices/key endpoints; minimal `sw.js`; real permission + `PushManager` subscribe persisted to backend; Settings device list from the DB; assign + comment push triggers; notification-click deep-link; dead-subscription pruning; backend + frontend tests.

**Out of scope:** iOS/iPhone + any PWA install pipeline (manifest/icons/Add-to-Home-Screen); `@angular/service-worker`/ngsw; background job/queue; triggers beyond assign+comment; Azure Key Vault; changes to `templates/Homdutio Pro.html`; re-doing task assignment (reused as-is).

## Architecture / Approach

Backend-first. **Phase 1**: EF entity + additive migration, VAPID config, `IPushSender` (real via `Lib.Net.Http.WebPush` + no-op selected by config like `IEmailSender`), `/api/push` endpoints (sub-scoped — added to the route-coverage **Exempt** set, not `HouseholdScope`). **Phase 2**: rewire the client from simulation to a minimal `sw.js` + real `Notification.requestPermission` + `PushManager.subscribe`, persist to the backend, render the device list from `/api/push/devices`, delete the fake prompt/QR/phone-only gate. **Phase 3**: hook the existing assign + comment handlers to send push inline/best-effort, deep-link the click to the task, and confirm pruning.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend infra | `PushSubscription` + migration, VAPID config, `IPushSender`, `/api/push` endpoints | Route-coverage guard fails if `/api/push/*` isn't added to Exempt |
| 2. Client SW + registry | `sw.js`, real subscribe, DB-backed device list; simulation removed | SW registration/scope + serializing the subscription correctly across browsers |
| 3. Triggers + delivery | Push on assign/comment, deep-link, pruning | Best-effort send must never fail the task action; real delivery only testable manually |

**Prerequisites:** a generated VAPID keypair in user-secrets (dev) for real delivery — without it the app runs the no-op sender; a household admin + a second member/browser to test cross-user/cross-device.
**Estimated effort:** ~3 phases across ~3-4 sessions; Phase 1 backend-heavy, Phase 2 frontend-heavy, Phase 3 thin full-stack wiring.

## Open Risks & Assumptions

- **iPhone will NOT receive push after this change** — it needs an installed PWA, deliberately deferred. This contradicts the original "test on my iPhone" scenario; flagged and accepted. A follow-up PWA change closes it.
- Real end-to-end delivery is only verifiable manually (VAPID + a live push service); CI mocks the browser/sender.
- `Lib.Net.Http.WebPush` is the chosen library; if it disappoints, swap behind `IPushSender` (fallback `WebPush`) with no other change.
- Inline best-effort send adds a small per-request cost on assign/comment; acceptable for a single household, swallowed on failure.

## Success Criteria (Summary)

- Enabling on one browser makes the device visible in Settings on **every** browser/device of the account (the reported bug is fixed).
- Assigning a task / commenting on a task delivers a real push to the recipient's devices (app closed included), and the click opens the task.
- A push failure never breaks the task action; removed/dead subscriptions disappear from the registry.
