# Per-Device Push Notifications & Admin Task Assignment — Implementation Plan

## Overview

Two connected features, built in the real Homdutio app (Angular 21 SPA + ASP.NET Core 9 API), using `templates/Homdutio Pro.html` **only as a visual/behavioral reference**:

1. **Admin task assignment** — an admin can assign a household member to a task. Assignment flips the task `ToDo → InProgress` and records the assignee as the claimer. This is a real full-stack change.
2. **Per-device notification UX** — a faithful *simulation* of the reference: notifications are per-device (not per-account), activatable only on a phone/PWA, with a soft-ask banner, a simulated OS permission prompt, a Settings "Notifications" section (device list + QR), and an in-app push toast. All state is client-side (`localStorage`); there is **no** real Service Worker / Web Push / backend registry in this plan.

The two connect: assigning a task triggers a notification — a push toast if I assigned it to *myself* (and this device is enabled), or a transient flash telling the admin the assignee "will be notified on any device where they've turned notifications on."

## Current State Analysis

**Task model** (`src/Homdutio.Api/Tasks/TaskEndpoints.cs`, `src/Homdutio.Data/Entities/HouseholdTask.cs`):
- Tasks have **no assignee** — only a self-service **claimer** (`HouseholdTask.ClaimedById`, `HouseholdTask.cs:42`), always set to the caller.
- Status enum `HouseholdTaskStatus { ToDo, InProgress, Done }` (`HouseholdTaskStatus.cs`), serialized as strings; frontend mirror `TaskStatus = 'ToDo' | 'InProgress' | 'Done'` (`task.service.ts:6`).
- Transitions are dedicated, server-guarded action endpoints, each appending a `TaskEvent`: `claim` (`TaskEndpoints.cs:129`, hardcodes `ClaimedById = caller.UserId` at `:149`), `done` (`:164`), `confirm` (admin, `:203`), `unclaim` (`:246`), `sendback` (admin, `:288`). Existing `TaskEventType` values: `Created, Claimed, MarkedDone, Confirmed, Unclaimed, SentBack`.
- Affordance flags are computed server-side in `ToResponse` (`:674-716`) and rendered blindly by the SPA (`canClaim`, `canEdit`, …).
- Create (`POST /api/tasks`, `CreateTaskRequest` `:719`) accepts only `{ title, description, tags }` and always creates an unassigned `ToDo` (`:103`).
- Create/edit modals (`create-task.component.*`, `task-detail.component.*`) collect only title/description/tags — **no member picker exists anywhere in the board module**.

**Identity & roles** (`household.service.ts`, `member.service.ts`, `auth.service.ts`):
- Admin check: `HouseholdService.current()?.role === 'Admin'` (`household.service.ts:29`, `Household.role: 'Admin' | 'Member'`).
- Member roster: `MemberService.list(): Observable<Member[]>` (`member.service.ts:35`) → `GET /api/households/members` (`HouseholdEndpoints.cs:247`). `Member` = `{ userId, displayName, email, role, isSelf, canManage, avatarUrl? }` (`member.service.ts:14`). Any member may read the roster.
- Current user: `AuthService.displayName` signal (`auth.service.ts:63`); **no client-side user id** — `Member.isSelf` (server-computed) identifies "me".
- Admin mutations are enforced server-side (`caller.Role != HouseholdRole.Admin → Forbid()`, e.g. `HouseholdEndpoints.cs:293`) via `HouseholdScope.ResolveCallerAsync`.

**Notification / UI infrastructure — none exists:**
- No Service Worker, no `@angular/service-worker` (only in `package-lock`), no PWA manifest (`web/public/` has only `favicon.ico`), no `Notification`/`PushManager`/`navigator.serviceWorker` usage.
- No toast/snackbar/flash utility. Transient UX today is CDK dialogs only. `@angular/cdk` overlay CSS is already loaded (`angular.json:37`), so CDK `Overlay` is available to build a toast on.
- No JS breakpoint detection (`BreakpointObserver`/`matchMedia`); responsiveness is CSS-only, cutoff `@media (max-width: 999px)` (`board.component.scss:49`). The mobile signal `board.component.ts:45` does not measure the viewport.
- `localStorage` used raw (no wrapper) in `auth.service.ts` (:221-229).
- No QR-code library.

**UI homes:**
- Settings is a single-panel CDK dialog (`settings-dialog.component.ts`), a flat `<form>` with "Profile photo" + "Display name" blocks, opened from `avatar-menu.component.ts:45`. Uses global `.modal`/`.field`/`.btn` primitives from `src/styles.scss`.
- Board (`board.component.html`): `.board-header` (`:2-5`) → `.board-tabs` (`:8-23`) → `.columns` (`:25-47`). A soft-ask banner goes between the header (`:5`) and the tabs (`:8`); `.board` is a centered `max-width: 70rem` box.

## Desired End State

- An admin sees an "Assign" affordance on tasks and a person picker (with "Anyone") in the create/edit task modals. Assigning a member moves the task to In Progress with that member as claimer, and records an `Assigned` audit event. Non-admins never see the picker, and the server rejects assignment from non-admins.
- On assign: if the admin assigned it to themselves, a push toast appears (only when this device has notifications enabled); otherwise a flash message confirms the assignee "will be notified on any device where they've turned notifications on."
- On a phone-sized viewport with notifications not granted, the Board shows a dismissible soft-ask banner; tapping "Enable" opens a simulated OS prompt whose Allow/Don't-Allow set a persisted per-device permission. On a desktop-sized viewport, the Board shows an informational banner (no activation CTA).
- Settings has a "Notifications" section: an account-status line, a per-device list (On/Off, THIS DEVICE badge, an "enable here" button only for the current phone), and — on desktop — a "Turn on from your phone" frame with a QR code, plus a PWA install hint on mobile.

**Verification:** `dotnet build src/Homdutio.Api -c Release` and `cd web && npm run build` succeed; `cd web && npm test` and `npm run lint` pass; the flows above work when driven in the browser (dev server `:4200` + API).

### Key Discoveries:

- Claim hardcodes `ClaimedById = caller.UserId` (`TaskEndpoints.cs:149`) — assigning another person **requires a new endpoint**, not a tweak to claim.
- `ClaimedById` already exists and is rendered as `claimerName`/`claimerAvatarUrl` (`task-card.component.html:87`, `task-detail.component.html:96`) — assignment can reuse it with **no schema migration**.
- No client-side user id exists; identify "me" via `Member.isSelf`, and admin via `HouseholdService.current()?.role`.
- Zero notification infra → the simulated approach has nothing to integrate with or conflict against; but the toast, breakpoint detection, and QR are all **net-new** primitives.

## What We're NOT Doing

- **No real Web Push**: no Service Worker, no VAPID, no browser `Notification`/`PushManager`, no backend push subscription registry, no Azure push delivery. The OS prompt, permission state, and "other devices" are simulated client-side.
- **No real cross-user/cross-device delivery**: assigning to another user does not actually push to them; it shows the admin a flash (mirrors the reference).
- **No PWA install pipeline**: the "Install Homdutio…" hint is informational copy only (no `manifest.webmanifest`, no `beforeinstallprompt` wiring).
- **No separate `AssignedToId` column**: assignment reuses `ClaimedById`.
- **No changes to the design reference** `templates/Homdutio Pro.html` (it is a read-only mockup).
- **No backend for the device list**: seeded/local-only.

## Implementation Approach

Assignment is a vertical full-stack slice first (Phase 1) because it is the real functional value and is independent of the notification substrate. Then build the notification permission model + primitives (Phase 2), the Board banners (Phase 3), and finally the Settings section plus the assign→push wiring that closes the loop (Phase 4). Follow existing conventions throughout: server-guarded action endpoints + `TaskEvent` audit; server-computed affordance flags rendered blindly; standalone Angular components with CDK dialogs; SCSS with the global `.modal`/`.field`/`.btn` primitives.

## Critical Implementation Details

- **Assignment is admin-only and must be enforced server-side**, mirroring `HouseholdEndpoints.cs:293` — never trust the client's role. The client `canAssign` flag / picker visibility is UI-only.
- **Device detection must branch in TypeScript** (the reference's `isMobile = width < 1000`). Add `BreakpointObserver` from `@angular/cdk/layout` (CDK already a dependency) using the established **999px** cutoff, exposed as a signal — do not re-measure ad hoc.
- **Never call any real permission API.** The "system prompt" is a CDK dialog; Allow/Don't-Allow write `granted`/`denied` to `localStorage['homdutio_notif_perm']`. The prompt only ever opens from a user click (the soft-ask CTA or the Settings "enable here" button), and only on mobile.
- **The push toast fires only when `isMobile && permission === 'granted'`** — this gate is the whole point of "per-device"; apply it in one place in `NotificationService`.

---

## Phase 1: Task assignment (full slice)

### Overview

Add admin-only assignment end-to-end: a new assign endpoint + optional assignee on create (backend), a member picker in the create/edit modals wired to `TaskService.assign` (frontend), and a reusable flash message for assignment feedback.

### Changes Required:

#### 1. Assign endpoint + audit event (backend)

**File**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs`, `src/Homdutio.Data/Entities/TaskEvent.cs` (or wherever `TaskEventType` is defined)

**Intent**: Let an admin set a task's owner to another member and start it, following the existing action-endpoint pattern.

**Contract**: New `POST /api/tasks/{id}/assign` taking `{ assigneeId: string }`. Guard: caller must be household admin (else `Forbid()`); task must be `ToDo` (reject otherwise, mirroring claim's guard at `:149`); `assigneeId` must be a member of the same household. On success: set `ClaimedById = assigneeId`, `ClaimedAtUtc = now`, `Status = InProgress`, rebalance `SortOrder` as claim does (`:151-153`), append a `TaskEvent` of new type `Assigned` (add the enum value). Add a `canAssign` boolean to `TaskResponse` (`:737`), computed in `ToResponse` (`:674`) as `callerIsAdmin && status == ToDo`.

#### 2. Optional assignee on create (backend)

**File**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs`

**Intent**: Allow the create modal to assign at creation time (the reference assigns from the add-task modal).

**Contract**: Extend `CreateTaskRequest` (`:719`) with optional `assigneeId: string?`. In the create handler (`:103`), if `assigneeId` is present and the caller is admin and the id is a valid member, create the task directly as `InProgress` with `ClaimedById = assigneeId` and append `Created` + `Assigned` events; otherwise behave exactly as today (unassigned `ToDo`). Ignore `assigneeId` (treat as unassigned) for non-admin callers.

#### 3. Task service + model (frontend)

**File**: `web/src/app/board/task.service.ts`

**Intent**: Expose the assign action and the new flag to the SPA.

**Contract**: Add `assign(id: string, assigneeId: string): Observable<Task>` → `POST /api/tasks/{id}/assign`. Add optional `assigneeId` to the create payload. Add `canAssign?: boolean` to the `Task` interface (`:9`).

#### 4. Member picker in create/edit modals (frontend)

**File**: `web/src/app/board/create-task/create-task.component.ts`/`.html`, `web/src/app/board/task-detail/task-detail.component.ts`/`.html`

**Intent**: Give admins a person selector ("Anyone" = unassigned) mirroring the reference's assign chips; hide it entirely for non-admins.

**Contract**: Inject `MemberService` + `HouseholdService`. Show the picker only when `HouseholdService.current()?.role === 'Admin'`. Options: a leading "Anyone" (no assignment) + one chip per member (`userId` value, `displayName` label, `isSelf` → append " (you)"). In create: include `assigneeId` in the create call when a member is chosen. In task-detail (existing `ToDo` task): call `TaskService.assign(id, assigneeId)`. Card/detail already render `claimerName`/`claimerAvatarUrl`, so no display change is required.

#### 5. Reusable flash message (frontend)

**File**: new `web/src/app/shared/flash/flash.service.ts` (+ small overlay component), used by the assignment result

**Intent**: Provide the transient "<name> will be notified on any device where they've turned notifications on." feedback (and a generic flash for reuse). Built on CDK `Overlay` (CSS already loaded).

**Contract**: `FlashService.show(message: string)` renders a dismissible transient message via CDK overlay, auto-hiding after a few seconds. On assign success where `assignee !== me`, call it with the reference copy. (The self-assign push toast is added in Phase 4.)

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build src/Homdutio.Api -c Release`
- Frontend builds & type-checks: `cd web && npm run build`
- Frontend unit tests pass: `cd web && npm test`
- Lint passes: `cd web && npm run lint`

#### Manual Verification:

- As an admin, creating a task with a member selected lands it in In Progress with that member shown as the owner; "Anyone" creates a normal To-do.
- As an admin, assigning a To-do task via the detail modal moves it to In Progress with the chosen owner and shows the flash message (for another person).
- As a non-admin, no picker is visible, and a direct `POST /assign` is rejected (403).
- Assigning a non-member id or a non-`ToDo` task is rejected with a clear error.

**Implementation Note**: After automated verification passes, pause for human confirmation of the manual steps before Phase 2.

---

## Phase 2: Notification permission model + simulated system prompt

### Overview

Build the shared client-side substrate: a `NotificationService` holding per-device permission, device detection, and a seeded device registry; a simulated OS permission prompt; and the push-toast primitive.

### Changes Required:

#### 1. NotificationService (frontend)

**File**: new `web/src/app/notifications/notification.service.ts`

**Intent**: Single source of truth for the simulated per-device notification state and actions.

**Contract**: Signals/state: `permission: 'default' | 'granted' | 'denied'` (read/written to `localStorage['homdutio_notif_perm']`, initialized on construction); `isMobile` (from `BreakpointObserver` at 999px); seeded `notifDevices` = `[{ id:'nd1', name:'iPhone Rafała', type:'mobile', enabled:false }, { id:'nd2', name:'iPad kuchnia', type:'mobile', enabled:false }]`. Methods: `requestNotifs()` — returns immediately if `!isMobile` or already `granted`, else opens the simulated prompt; `grant()`/`deny()` — persist `granted`/`denied`; `dismissSoftAsk()` (session-scoped, not persisted); computed `anyEnabled`, `deviceList` (`isMobile ? [currentDevice, ...others] : others`), `notifStatusText`. `pushNotify(title, body)` — no-op unless `isMobile && permission === 'granted'`, else shows the push toast.

#### 2. Simulated OS prompt (frontend)

**File**: new `web/src/app/notifications/system-prompt/*` (CDK dialog)

**Intent**: Mimic the browser's permission dialog without touching any real API.

**Contract**: A CDK dialog with the reference copy and two buttons — "Don't Allow" → `NotificationService.deny()` (+ opens deny-help) and "Allow" → `NotificationService.grant()` (+ toast "Notifications on for this device"). Only opened by `requestNotifs()` (user-initiated, mobile-only).

#### 3. Push toast primitive (frontend)

**File**: `web/src/app/shared/flash/*` (extend Phase 1's overlay) or a sibling `notifications/push-toast/*`

**Intent**: Render the notification-styled toast used by `pushNotify`.

**Contract**: A transient overlay styled as a push notification (title + body), auto-dismiss. Distinct visual variant from the plain flash.

### Success Criteria:

#### Automated Verification:

- Frontend builds & type-checks: `cd web && npm run build`
- Unit tests pass (incl. a `NotificationService` spec covering the `isMobile`/`granted` gate and localStorage round-trip): `cd web && npm test`
- Lint passes: `cd web && npm run lint`

#### Manual Verification:

- On a narrow viewport, triggering `requestNotifs()` opens the simulated prompt; Allow persists `granted` (survives reload) and shows the confirmation toast; Don't-Allow persists `denied`.
- On a wide viewport, `requestNotifs()` does nothing (no prompt).
- `pushNotify` shows a toast only when the viewport is narrow and permission is `granted`.

**Implementation Note**: Pause for human confirmation before Phase 3.

---

## Phase 3: Board notification banners

### Overview

Surface the soft-ask (mobile) and informational (desktop) banners on the Board, plus the deny-help instructions, driven by `NotificationService`.

### Changes Required:

#### 1. Soft-ask + desktop banner (frontend)

**File**: `web/src/app/board/board.component.html`/`.ts`/`.scss` (+ small banner component if cleaner)

**Intent**: Prompt activation on mobile, inform on desktop, matching the reference visibility rules.

**Contract**: Insert between `.board-header` (`:5`) and `.board-tabs` (`:8`). Show soft-ask when `isMobile && permission !== 'granted' && !softAskDismissed`: normal state = "Turn on notifications?" + "Enable notifications" CTA (→ `requestNotifs`); denied state = "Notifications are blocked" + "How to unblock" CTA (→ deny-help). Show desktop banner when `!isMobile && !anyEnabled && !softAskDismissed`: "Get notified on your phone", **no activation CTA**. Both dismissible (session-scoped via `dismissSoftAsk`).

#### 2. Deny-help panel (frontend)

**File**: `web/src/app/notifications/deny-help/*` (dialog or inline panel)

**Intent**: Explain how to re-enable notifications in the browser after a denial.

**Contract**: Opened from the denied-state CTA / prompt deny; static instructional steps.

### Success Criteria:

#### Automated Verification:

- Frontend builds & type-checks: `cd web && npm run build`
- Unit tests pass: `cd web && npm test`
- Lint passes: `cd web && npm run lint`

#### Manual Verification:

- Narrow viewport, not granted → soft-ask appears above the columns; Enable opens the prompt; dismiss hides it for the session; denied state shows the "blocked" copy + How-to-unblock.
- Wide viewport, nothing enabled → informational banner with no activation button.
- Once granted (mobile), the soft-ask no longer appears.

**Implementation Note**: Pause for human confirmation before Phase 4.

---

## Phase 4: Settings notifications section + assign→push wiring

### Overview

Add the "Notifications" section to Settings (device list, status, QR, install hint) and close the assignment loop by firing the push toast on self-assignment.

### Changes Required:

#### 1. Notifications section in Settings (frontend)

**File**: `web/src/app/shell/settings-dialog/settings-dialog.component.html`/`.ts`/`.scss`

**Intent**: Management surface for per-device notifications, mirroring the reference (management, not activation).

**Contract**: A new block in the existing dialog. Show `notifStatusText`; render `deviceList` rows with On/Off badge and a THIS DEVICE badge on the current device; the only activation button is "Turn on" on the current phone when unsupported-elsewhere and not yet enabled (→ `requestNotifs`). On desktop: omit the dead current-device row, and show a "Turn on from your phone" frame with a QR code; on mobile without consent, show the "Install Homdutio…" hint. No enable toggle on desktop.

#### 2. QR code generation (frontend)

**File**: `web/src/app/notifications/qr/*` (or a small util)

**Intent**: Render a scannable code pointing at the app so a phone can open it and enable notifications.

**Contract**: Generate an SVG QR encoding the app origin URL (e.g. `location.origin`), sized ~150px. Prefer a tiny dependency-free SVG QR generator or a small library; keep it self-contained. (The reference draws a decorative SVG; a real scannable QR is the improvement.)

#### 3. Assign→push wiring (frontend)

**File**: `web/src/app/board/create-task/*`, `web/src/app/board/task-detail/*`

**Intent**: Complete the notification loop from Phase 1.

**Contract**: On successful assign/create-with-assignee, if the assignee is the current user (`Member.isSelf`), call `NotificationService.pushNotify('New task assigned to you', '<me> assigned you "<title>".')` (fires only if this device is enabled); otherwise keep the Phase 1 flash. 

### Success Criteria:

#### Automated Verification:

- Frontend builds & type-checks: `cd web && npm run build`
- Unit tests pass: `cd web && npm test`
- Lint passes: `cd web && npm run lint`

#### Manual Verification:

- Settings shows the account status line and device list; the current phone shows THIS DEVICE and (when not enabled) a Turn-on button; desktop shows no current-device row.
- Desktop Settings shows a QR code that, when scanned, opens the app origin; mobile without consent shows the install hint.
- Assigning a task to yourself (as admin, on an enabled mobile view) shows the push toast; assigning to someone else shows the flash.

**Implementation Note**: Pause for human confirmation; this is the final phase.

---

## Testing Strategy

### Unit Tests (Vitest, colocated `*.spec.ts`):

- `NotificationService`: `isMobile && granted` gate on `pushNotify`; `permission` localStorage round-trip and init; `deviceList` desktop-omit logic; `notifStatusText` variants; `requestNotifs` mobile-only + already-granted short-circuits.
- Create/task-detail: picker visible only for admin; "Anyone" → unassigned; member selection includes `assigneeId`.
- `TaskService.assign` posts to the right endpoint.

### Integration / API:

- No backend test project exists yet (AGENTS.md); if one is added at `src/Homdutio.Api.Tests/`, cover: assign rejects non-admins (403), rejects non-`ToDo` tasks and non-members, sets `ClaimedById`/`InProgress` and appends `Assigned`. Otherwise verify via manual API calls.

### Manual Testing Steps:

1. Admin creates a task assigned to a member → In Progress, owner shown, flash appears.
2. Admin self-assigns on an enabled mobile viewport → push toast.
3. Non-admin sees no picker; direct `POST /assign` → 403.
4. Mobile soft-ask → Allow persists across reload; Don't-Allow → denied + how-to-unblock.
5. Desktop → informational banner (no CTA) + Settings QR + no current-device row.

## Performance Considerations

Negligible — all notification state is client-side and small. The one new backend endpoint mirrors existing claim cost. Keep the QR generator dependency-free or tiny to avoid bundle bloat.

## Migration Notes

None. Assignment reuses the existing `ClaimedById` column; adding a `TaskEventType.Assigned` enum value requires no schema migration (events are stored by string name, consistent with existing types).

## References

- Design reference (read-only mockup): `templates/Homdutio Pro.html`
- Change identity & full spec: `context/changes/push-notifications/change.md`
- Task endpoints & claim pattern: `src/Homdutio.Api/Tasks/TaskEndpoints.cs:129` (claim), `:674` (`ToResponse`), `:719` (`CreateTaskRequest`)
- Admin-guard pattern: `src/Homdutio.Api/Households/HouseholdEndpoints.cs:293`
- Member roster: `web/src/app/household/member.service.ts:35`; admin role: `web/src/app/household/household.service.ts:29`
- Settings dialog: `web/src/app/shell/settings-dialog/settings-dialog.component.ts`; Board layout: `web/src/app/board/board.component.html:5-25`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Task assignment (full slice)

#### Automated

- [x] 1.1 Backend builds: `dotnet build src/Homdutio.Api -c Release` — 2e65bae
- [x] 1.2 Frontend builds & type-checks: `cd web && npm run build` — 2e65bae
- [x] 1.3 Frontend unit tests pass: `cd web && npm test` — 2e65bae
- [x] 1.4 Lint passes: `cd web && npm run lint` — 2e65bae

#### Manual

- [x] 1.5 Admin create-with-member → In Progress + owner; "Anyone" → normal To-do — 2e65bae
- [x] 1.6 Admin assigns a To-do via detail modal → In Progress + owner + flash — 2e65bae
- [x] 1.7 Non-admin sees no picker; direct `POST /assign` → 403 — 2e65bae
- [x] 1.8 Assigning a non-member id or non-`ToDo` task is rejected clearly — 2e65bae

### Phase 2: Notification permission model + simulated system prompt

#### Automated

- [x] 2.1 Frontend builds & type-checks: `cd web && npm run build` — 81921bc
- [x] 2.2 Unit tests pass incl. `NotificationService` gate + localStorage spec: `cd web && npm test` — 81921bc
- [x] 2.3 Lint passes: `cd web && npm run lint` — 81921bc

#### Manual

- [x] 2.4 Narrow viewport: prompt opens; Allow persists `granted` across reload + toast; Don't-Allow persists `denied` — 81921bc
- [x] 2.5 Wide viewport: `requestNotifs()` does nothing — 81921bc
- [x] 2.6 `pushNotify` toast only when narrow + `granted` — 81921bc

### Phase 3: Board notification banners

#### Automated

- [x] 3.1 Frontend builds & type-checks: `cd web && npm run build` — ae78d18
- [x] 3.2 Unit tests pass: `cd web && npm test` — ae78d18
- [x] 3.3 Lint passes: `cd web && npm run lint` — ae78d18

#### Manual

- [x] 3.4 Narrow + not granted: soft-ask above columns; Enable opens prompt; dismiss hides for session; denied state shows blocked copy + how-to-unblock — ae78d18
- [x] 3.5 Wide + nothing enabled: informational banner, no activation button — ae78d18
- [x] 3.6 After grant (mobile): soft-ask no longer appears — ae78d18

### Phase 4: Settings notifications section + assign→push wiring

#### Automated

- [x] 4.1 Frontend builds & type-checks: `cd web && npm run build`
- [x] 4.2 Unit tests pass: `cd web && npm test`
- [x] 4.3 Lint passes: `cd web && npm run lint`

#### Manual

- [x] 4.4 Settings shows status + device list; current phone shows THIS DEVICE + Turn-on; desktop omits current-device row
- [x] 4.5 Desktop QR opens app origin when scanned; mobile-without-consent shows install hint
- [x] 4.6 Self-assign on enabled mobile → push toast; assign to other → flash
