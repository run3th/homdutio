# Per-Device Push Notifications & Admin Task Assignment — Plan Brief

> Full plan: `context/changes/push-notifications/plan.md`

## What & Why

Give admins a way to **assign household tasks to specific members** (real full-stack), and add a **per-device notification UX** faithfully *simulated* from the `templates/Homdutio Pro.html` design reference. Notifications are per-device, not per-account: only a phone can turn them on, desktop only informs. The two connect — assigning a task nudges the assignee (push toast if it's me, a flash confirmation otherwise).

## Starting Point

Tasks today have only a **self-service claimer** (`ClaimedById`, always the caller) — no assignee, and the claim endpoint hardcodes claimer=self. Create/edit modals collect only title/description/tags; there's no member picker. Admin is knowable via `HouseholdService.current()?.role`; members via `MemberService.list()`. There is **zero** notification infrastructure — no service worker, PWA, push, toast, or JS breakpoint detection.

## Desired End State

Admins pick an assignee ("Anyone" = unassigned) when creating/editing a task; assignment moves it To-do→In-Progress with that member as owner and an `Assigned` audit event. On a phone without permission, the Board shows a dismissible soft-ask that opens a simulated OS prompt (Allow/Don't-Allow persisted in `localStorage`); on desktop it only informs. Settings gains a "Notifications" section: device list (On/Off, THIS DEVICE), account status, a QR "turn on from your phone" frame on desktop, and a PWA install hint on mobile.

## Key Decisions Made

> ⚠️ The user stepped away mid-questioning. Rows below marked **(assumed)** are my recommended defaults — confirm or flip on review; flipping "Push approach" to real Web Push would substantially reshape the plan.

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Push approach | Simulated UX, design-faithful **(assumed)** | Matches the reference; app has zero push infra and real Web Push is a separate, far larger initiative | Plan |
| Device list | Seeded / local-only **(assumed)** | Consistent with simulation; no backend device registry | Plan |
| Assign persistence | Reuse `ClaimedById` + new `Assigned` event **(assumed)** | Spec says "claimer = assignee"; avoids a schema migration | Plan |
| Scope | Both features, phased, assignment first **(assumed)** | Assignment is the real functional value; notification UX layers on it | Plan |
| Assign endpoint | New admin-guarded `POST /api/tasks/{id}/assign` | Claim hardcodes self; assignment needs its own server-enforced action | Plan |
| Device detection | `BreakpointObserver` @ 999px | Reference branches on width in JS; reuse the app's established CSS breakpoint | Plan |
| QR target | App origin URL | Scanning on a phone opens the app to enable notifications | Plan |

## Scope

**In scope:** admin task assignment (backend endpoint + create-time assignee + frontend picker + audit); simulated per-device notification permission model; soft-ask/desktop Board banners; simulated OS prompt; Settings notifications section (device list, status, QR, install hint); in-app flash + push toast.

**Out of scope:** real Web Push / service worker / VAPID / backend subscription registry / Azure delivery; real cross-user/cross-device delivery; PWA install pipeline; a separate `AssignedToId` column; any edit to the design reference; a backend device registry.

## Architecture / Approach

Backend: one new admin-guarded action endpoint (`/assign`) + optional `assigneeId` on create, both reusing `ClaimedById` and appending a `TaskEvent`, following the existing claim/done/confirm pattern with server-computed `canAssign`. Frontend: a `NotificationService` (permission in `localStorage`, `isMobile` via `BreakpointObserver`, seeded devices, `pushNotify` gated on `isMobile && granted`), a simulated OS-prompt CDK dialog, CDK-overlay flash + push-toast primitives, Board banners, and a Settings section — all standalone components using the app's existing `.modal`/`.field`/`.btn` primitives.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Task assignment (full slice) | Admin assigns members; backend endpoint + picker + flash | Reusing `ClaimedById` makes an assigned task visually identical to a self-claimed one |
| 2. Notification model + prompt | `NotificationService`, simulated OS prompt, push toast | Getting the per-device `isMobile && granted` gate right; localStorage init timing |
| 3. Board banners | Soft-ask (mobile) + informational (desktop) banners | Correct visibility across viewport + permission states |
| 4. Settings section + push wiring | Device list, QR, install hint; self-assign push toast | QR generation without heavy dependency; desktop row-omit logic |

**Prerequisites:** running app (API + `npm start` on `:4200`); an account that is a household **admin** with at least one other member to assign to.
**Estimated effort:** ~4 phases across ~3-4 sessions; Phase 1 is the only full-stack phase, Phases 2-4 are frontend-only.

## Open Risks & Assumptions

- **Biggest fork:** "Simulated UX" is assumed. If real Web Push is wanted, Phases 2-4 change fundamentally (service worker, VAPID, backend registry, Azure) and effort grows to weeks.
- Assigned vs self-claimed tasks look the same on the card (only the event log differs) — acceptable per the reference, but flag if a distinct "assigned" state is wanted.
- No backend test project exists; assign-endpoint guards are verified manually unless one is added at `src/Homdutio.Api.Tests/`.
- QR should stay dependency-free/tiny to avoid bundle bloat.

## Success Criteria (Summary)

- An admin can assign a member to a task; it starts In Progress with that owner; non-admins are blocked (client and server).
- On mobile without permission, the soft-ask + simulated prompt persist a per-device permission; desktop only informs.
- Settings manages per-device notifications (list, status, QR, install hint); self-assignment fires a push toast only on an enabled device.
