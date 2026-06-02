# Invite and Multiplayer Board (S-06) Implementation Plan

## Overview

S-06 turns the single-player board into a genuine **multiplayer** household. A member generates a
**single-use, time-expiring invite link**; a second adult opens it, **registers/logs in if needed** and
**joins the one household** (FR-006/FR-007); and from then on both members see the **shared board refresh
within 5 seconds** (NFR-1) via polling. This is the slice that makes FR-015's cross-member confirm and the
PRD's primary success flow ("both users see the same state at every step") actually verifiable with two real
people instead of one self-attesting admin.

The correctness-sensitive parts are exactly the ones the roadmap flags: **single-use invalidation** (FR-005 —
a consumed or expired token can never join again), **one-household-per-user** (FR-007 — already enforced by a
DB unique index, hardened at the endpoint), and **token scoping** (US-02 — a token grants access to exactly
one household, and a foreign token leaks nothing). These are locked by integration tests.

## Current State Analysis

- **The membership model is already invite-ready.** `HouseholdMember`
  (`src/Homdutio.Data/Entities/HouseholdMember.cs`) has a unique index on `UserId`
  (`ApplicationDbContext.cs:48`) enforcing one-household-per-user (FR-007) at the database level. Joining is
  just inserting a row with `Role = Member`. There is no invite entity yet.
- **The household endpoint pattern is set** (`src/Homdutio.Api/Households/HouseholdEndpoints.cs`): a
  `/api/households` group, all `.RequireAuthorization()`; the acting user comes from the JWT `sub` claim and
  the household is derived server-side (never client-supplied). `POST /api/households` already demonstrates
  the **`alreadyMember` → 409 Conflict** guard this slice reuses for the join. DTOs are `record`s;
  `Results.Ok/ValidationProblem/Created/Conflict/NoContent/NotFound` are the response vocabulary.
- **Register is reusable as-is** (`src/Homdutio.Api/Auth/AuthEndpoints.cs`): `POST /api/auth/register`
  creates an `ApplicationUser` with a card-ready `DisplayName` and returns `200`/`400 ValidationProblem`. The
  join flow reuses it — the recipient registers normally, then accepts the invite. No new account-creation
  endpoint is needed.
- **F-03 (polling) was decided but never built.** `TaskService`
  (`web/src/app/board/task.service.ts`) only refetches after the acting client's own mutations
  (`create`/`claim`/`markDone`/`confirm`/`update`/`delete`/`reorder` each `switchMap(() => load())`). There is
  **no interval poll** — the only `interval/poll` matches in the codebase are *comments* saying "polling is
  S-06". So NFR-1's 5s freshness has to be delivered here.
- **The frontend auth/routing layer will bounce an invitee** without changes. `app.routes.ts` guards every
  real route with `authGuard` + `requireHousehold`/`requireNoHousehold` (`web/src/app/household/membership.guard.ts`):
  an unauthenticated visitor to a guarded route is redirected to `/login`, and a logged-in member is pushed to
  `/board`. A public `/join/:token` route is needed, plus a way to carry the token back after register/login.
- **Login currently lands on `/board` via the guard chain**; register routes to `/login` with a success
  notice + prefilled email (S-01 decision: explicit login, no auto-login). The join flow must thread a
  `returnUrl` (the `/join/:token` path) through both so the recipient comes back to finish joining.
- **The SPA already owns reusable primitives**: `mapValidationProblem` (`web/src/app/auth/validation-problem.ts`),
  the in-memory `AuthService` token signal, `HouseholdService` (membership signal + `loaded` flag + `clearOnLogout`),
  and (from S-04) `@angular/cdk` with a CDK Dialog and drag-drop on the board.
- **Test harness is reusable**: `AuthApiFactory` (throwaway LocalDB + test JWT) and the
  register→login→create-household→bearer pattern (`tests/Homdutio.Api.Tests/`); the `SeedMemberAsync` helper
  seeds a second member directly. vitest with `HttpTestingController` and component stubs (`web/src/app/**/*.spec.ts`).

## Desired End State

A member opens the board and clicks **Invite a member**; the app generates a link
(`/join/<token>`) and copies it to the clipboard to share out-of-band. A second adult opens that link on
their device and sees **"Join [Household name]"**. If they have no account they register and log in (the
invite is preserved across both screens); on return the invite is consumed and they land on the **shared
board**, now a member. The admin, still on their board, sees the joiner's first action (a claim, a new card)
appear **within 5 seconds without refreshing**. A second attempt to open the same link shows
**"This invite is no longer valid."** A recipient who already belongs to a household is told so and is not
joined. A token belonging to household A can never add anyone to household B.

**Verification:** `dotnet test` + `npm test` green; a manual two-device (or two-browser-profile) walk —
generate a link as admin A, open it in a fresh profile, register a new account, join, and watch both boards
stay in sync within 5s as each side claims/creates; re-open the consumed link and see it rejected; sign in as
an existing member of another household, open the link, and see the block.

### Key Discoveries:

- **One-household-per-user is already a DB invariant.** The unique index on `HouseholdMember.UserId`
  (`ApplicationDbContext.cs:48`) means a concurrent double-join fails at the database even if the endpoint
  check races — the endpoint's `alreadyMember`/`DbUpdateException` handling turns that into a clean 409
  rather than a 500.
- **Single-use needs server-side state + a concurrency guard.** A consumed flag alone races: two people
  opening the same link simultaneously could both pass the "is it consumed?" read. A `rowversion` concurrency
  token on the invite makes the consume a single optimistic-concurrency `SaveChanges` — the loser gets a
  concurrency failure mapped to "no longer valid", so the token is consumed exactly once. This fits the
  single-`SaveChanges`-per-transition rule (the `EnableRetryOnFailure` execution strategy disallows
  user-initiated transactions, `Program.cs:25-29`).
- **Preview must be public but minimal.** The recipient needs the household name *before* they have an
  account, so `GET /api/households/invites/{token}` is unauthenticated — but it returns only the household
  name + validity, never membership or task data (US-02: no over-exposure beyond what holding the link implies).
- **Polling already has a natural home and a natural pause.** `TaskService.load()` is the refetch; an
  interval that calls it delivers NFR-1. Pausing on `document.hidden` bounds server load (the F-03 "cap the
  interval" risk), and suppressing a tick during an active drag or open dialog (both already tracked on the
  S-04 board) keeps a poll from yanking the board out from under an in-progress reorder/edit.

## What We're NOT Doing

- **No invite emails** — the link is shown in-app and shared out-of-band (Non-Goal; FR-005/PRD invite flow).
- **No explicit revoke UI and no pending-invites roster** — single-use + time expiry bounds a leaked link;
  an admin-facing list of outstanding invites and a revoke action are out of scope (decided in planning;
  closer to S-09 than this slice).
- **No member roster / "who's in this household" panel** — presence is implicit via the names already on
  cards (created/claimed by). A members list is S-09 (member administration).
- **No leave-and-switch household path** — a recipient already in a household is blocked (FR-007); switching
  households is a v2 Non-Goal.
- **No combined join-and-register endpoint** — the recipient registers via the existing S-01 form, then
  accepts; no duplication of registration/password-policy logic.
- **No SignalR / WebSocket push** — polling meets the ≤5s contract on the B1 single-instance MVP (F-03
  decision); SignalR remains the reversible post-validation upgrade.
- **No role management on join** — a joiner is always a `Member`; promotion/demotion is S-09.
- **No password reset, no refresh-token/session-persistence changes** — those are S-08 / S-10.

## Implementation Approach

Three phases, backend-first so the SPA builds against a real contract (mirrors S-02/S-03/S-04):

1. **Backend** — add the `HouseholdInvite` entity (single-use + expiry + `rowversion`), an additive
   migration, and the three endpoints (generate, public preview, accept) with all guards; integration tests
   lock the lifecycle, single-use, expiry, one-household, and cross-household scoping.
2. **Frontend invite + join** — `InviteService`; a board affordance that generates and copies the link; a
   public `/join/:token` route + `JoinComponent` that previews, threads the token through register/login,
   blocks already-in-a-household recipients, and lands the joiner on the board; routing/guard changes; vitest.
3. **Live board (polling, F-03)** — `TaskService` interval polling that pauses on a hidden tab and is
   suppressed during an active drag / open dialog; board wires the lifecycle; vitest; full build + suites green.

## Critical Implementation Details

- **Single-use is enforced by optimistic concurrency, not just a flag.** The accept path loads the invite,
  verifies it is unconsumed and unexpired, sets `ConsumedAtUtc`/`ConsumedById`, inserts the `HouseholdMember`,
  and commits in **one** `SaveChanges`. A `rowversion` (`[Timestamp]`) concurrency token on `HouseholdInvite`
  means a second concurrent accept fails with a `DbUpdateConcurrencyException` → mapped to `410 Gone`. This is
  the load-bearing correctness guard for FR-005; do not replace it with a bare read-then-write.
- **The token must survive the auth hop.** For a logged-out recipient, `JoinComponent` sends them to
  `/login?returnUrl=/join/<token>` (and register carries the same `returnUrl` through to login). Login, on
  success, navigates to `returnUrl` when present instead of the default `/board`. The token lives in the route
  param the whole time — it is never stored server-side against the session, so dropping it just means the
  recipient re-opens the original link.
- **Join must bypass `requireHousehold`/`requireNoHousehold`.** `/join/:token` is guarded by `authGuard` only
  is wrong for the logged-out preview step — the preview is public. The route carries **no** `requireHousehold`
  guard; `JoinComponent` itself reads auth + membership state and renders the correct branch (preview / login
  prompt / already-in-household block / join button), then calls `HouseholdService` to refresh membership and
  routes to `/board` on success.
- **Token generation is cryptographically random and URL-safe.** Generate ~256 bits from a CSPRNG encoded
  URL-safe (base64url / no padding) so tokens are unguessable and safe in a path segment; persist with a
  unique index. Never derive the token from household id or a counter.

## Phase 1: Backend — invite model + endpoints

### Overview

Add the `HouseholdInvite` entity (single-use + expiry + concurrency token), generate one additive migration,
and expose the three endpoints (generate / public preview / accept) with every guard. Lock everything with
integration tests covering the lifecycle and the cross-household boundary.

### Changes Required:

#### 1. HouseholdInvite entity

**File**: `src/Homdutio.Data/Entities/HouseholdInvite.cs` (new)

**Intent**: Persist a single-use, time-expiring invite to a household so a link can be validated and consumed
exactly once (FR-005), scoped to one household (US-02).

**Contract**: New entity: `Id` (Guid), `HouseholdId` (Guid, FK → `Household`), `Token` (string, URL-safe
random, unique), `CreatedById` (raw `AspNetUsers.Id`, no nav — mirrors `HouseholdTask`), `CreatedAtUtc`,
`ExpiresAtUtc`, nullable `ConsumedAtUtc`, nullable `ConsumedById`, and a `byte[] RowVersion` concurrency token.
Add a `DbSet<HouseholdInvite> HouseholdInvites` to `ApplicationDbContext`.

#### 2. DbContext config + migration

**File**: `src/Homdutio.Data/ApplicationDbContext.cs`,
`src/Homdutio.Data/Migrations/<timestamp>_AddHouseholdInvites.cs` (generated)

**Intent**: Map the entity additively and back the token lookup with a unique index; the concurrency token
makes consume atomic.

**Contract**: In `OnModelCreating`, configure `HouseholdInvite`: `Token` required + `HasMaxLength` + a
**unique** index; `RowVersion` as `IsRowVersion()`; FK to `Household` with cascade delete (mirrors
`HouseholdTask`); `CreatedById`/`ConsumedById` as raw columns with no navigation (avoid multiple cascade paths
through `AspNetUsers`, the documented `HouseholdTask` rule). Generate `AddHouseholdInvites` (project
`Homdutio.Data`, startup `Homdutio.Api`). Review the generated `Up`: it must only **add** the new table +
indexes (no change to existing tables).

#### 3. Generate endpoint

**File**: `src/Homdutio.Api/Households/HouseholdEndpoints.cs`

**Intent**: Let any member (admin or adult member, FR-005) mint a shareable single-use link to their
household.

**Contract**: `POST /api/households/invites` (`.RequireAuthorization()`). Resolve the caller's membership
from the JWT `sub` (no membership → 404, consistent with the household scoping rule). Create a
`HouseholdInvite` for the caller's household with a fresh CSPRNG token and `ExpiresAtUtc = now + <window>`
(window a single named constant, e.g. 7 days), one `SaveChanges`. Return `201` with
`InviteResponse(string Token, DateTime ExpiresAtUtc)`. The SPA builds the `/join/<token>` URL; the API returns
the token, not a hard-coded host.

#### 4. Public preview endpoint

**File**: `src/Homdutio.Api/Households/HouseholdEndpoints.cs`

**Intent**: Let a recipient see which household they're being invited to *before* they have an account, while
leaking nothing beyond the household name (US-02).

**Contract**: `GET /api/households/invites/{token}` — **no** `.RequireAuthorization()` (public). Look up the
invite by token. Unknown token → `404`; consumed or expired → `410 Gone`; otherwise `200` with
`InvitePreviewResponse(string HouseholdName)`. Returns no membership, task, or member-list data. (Map this
endpoint outside the authorized group, or chain `.AllowAnonymous()`.)

#### 5. Accept endpoint

**File**: `src/Homdutio.Api/Households/HouseholdEndpoints.cs`

**Intent**: Consume the invite and add the authenticated caller to the household as a `Member`, exactly once
(FR-005/FR-006), never violating one-household-per-user (FR-007).

**Contract**: `POST /api/households/invites/{token}/accept` (`.RequireAuthorization()`). Resolve caller from
`sub`. Look up invite: unknown → `404`; consumed/expired → `410`. If the caller already belongs to a
household → `409 Conflict` (reuse the `alreadyMember` pattern), token untouched. Otherwise, in **one**
`SaveChanges`: set `ConsumedAtUtc`/`ConsumedById`, insert a `HouseholdMember` (`Role = Member`,
`JoinedAtUtc = now`). The invite's `rowversion` guards against a concurrent consume → on
`DbUpdateConcurrencyException` return `410`; on the `UserId` unique-index `DbUpdateException` return `409`.
Return `200` with the existing `HouseholdResponse` (id, name, role) so the SPA can cache membership directly.

#### 6. Integration tests

**File**: `tests/Homdutio.Api.Tests/HouseholdInviteEndpointsTests.cs` (new)

**Intent**: Lock the invite lifecycle and the cross-household boundary — the roadmap's named risk.

**Contract**: Reuse `AuthApiFactory` + the register→login→create-household→bearer helpers and `SeedMemberAsync`.
Cover: (a) **generate** returns a token + future expiry; an adult member (non-admin) can also generate;
(b) **preview** returns the household name for a valid token, `404` for unknown, `410` for consumed/expired;
(c) **accept (new member)** — a second user with no household joins, gets `200` `HouseholdResponse` with role
`Member`, and now appears via `GET /api/households/me`; the inviting household's board is reachable by the new
member; (d) **single-use** — a second `accept` of the same token → `410`, and no second membership row is
created; (e) **expiry** — an invite past `ExpiresAtUtc` (seeded directly) → `410` on both preview and accept;
(f) **one-household (FR-007)** — a caller already in a household accepting → `409`, token stays unconsumed;
(g) **scoping (US-02)** — a token for household A adds the caller to A, never B; an unknown/foreign token
never grants access; (h) **unauthenticated** generate/accept → `401`, while preview is reachable anonymously.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- Migration is additive only (one new `HouseholdInvites` table + indexes; no existing table changed) — confirmed by reviewing the generated `Up`
- API integration tests pass: `dotnet test`
- Migration applies cleanly to a fresh DB (via the test run)

#### Manual Verification:

- `POST /api/households/invites` returns a token; `GET /api/households/invites/{token}` returns the household name
- A second account `POST .../accept` joins and `GET /api/households/me` then returns that household as `Member`
- Re-accepting the same token → 410; accepting while already in a household → 409
- A preview/accept of an expired or unknown token → 410 / 404

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual
confirmation before Phase 2.

---

## Phase 2: Frontend — invite generation + join flow

### Overview

Add an `InviteService`; a board affordance that generates and copies the invite link; and a public
`/join/:token` route with a `JoinComponent` that previews the household, threads the token through the
existing register/login screens for logged-out recipients, blocks already-in-a-household users, and lands a
successful joiner on the shared board. Cover with vitest specs.

### Changes Required:

#### 1. InviteService

**File**: `web/src/app/household/invite.service.ts` (new)

**Intent**: Wrap the three invite endpoints for the board (generate) and the join page (preview, accept).

**Contract**: `providedIn: 'root'`. `generate()` → `POST /api/households/invites` → `Observable<{ token,
expiresAtUtc }>`; `preview(token)` → `GET /api/households/invites/{token}` → `Observable<{ householdName }>`
(propagates 404/410 as errors the caller branches on); `accept(token)` → `POST
/api/households/invites/{token}/accept` → `Observable<Household>`. Build the shareable URL from
`window.location.origin` + `/join/<token>` (a small pure helper, unit-testable).

#### 2. Board: invite affordance

**File**: `web/src/app/board/board.component.{ts,html,scss}`

**Intent**: Give a member an explicit way to generate and share an invite from the board (FR-005).

**Contract**: Add an **Invite a member** control in the board header. On click, call `InviteService.generate()`,
then copy the built `/join/<token>` URL to the clipboard (`navigator.clipboard`) and surface a brief
confirmation (and the link as selectable text as a fallback). Mobile-first at ≤400px (NFR-2). Keep it distinct
from the task affordances; no change to task rendering.

#### 3. Public join route + JoinComponent

**File**: `web/src/app/join/join.component.{ts,html,scss}` (new), `web/src/app/app.routes.ts`

**Intent**: One public entry that takes a recipient from an invite link to joined, handling the logged-out,
logged-in-no-household, and already-in-household cases.

**Contract**: Add a route `join/:token` with **no** `requireHousehold`/`requireNoHousehold` guard (it must be
reachable while logged out). `JoinComponent` reads the `:token` param and calls `InviteService.preview`:
- Invalid token (404/410) → show "This invite is no longer valid."
- Valid + **not authenticated** → show "Join [Household]" with **Log in** / **Register** actions that navigate
  to `/login?returnUrl=/join/<token>` (register carries the same `returnUrl` through to login).
- Valid + **authenticated, no household** → a **Join** button → `InviteService.accept(token)` → on success
  cache membership via `HouseholdService` and `router.navigate(['/board'])`.
- Valid + **authenticated, already in a household** → show "You already belong to a household." (no join).
Map a join-time `409`/`410` to the matching message and re-check state. Mobile-first ≤400px.

#### 4. Thread returnUrl through login/register

**File**: `web/src/app/auth/login/login.component.ts`, `web/src/app/auth/register/register.component.ts`

**Intent**: Let a logged-out invitee return to `/join/:token` after authenticating, so the token survives the
auth hop.

**Contract**: `LoginComponent` reads an optional `returnUrl` query param and, on successful login, navigates
there when present (else the existing default). `RegisterComponent` forwards a `returnUrl` it receives onward
to `/login?returnUrl=...` alongside its existing success-notice + prefilled-email behavior. No change to the
auth API or token handling.

#### 5. Frontend tests

**File**: `web/src/app/household/invite.service.spec.ts` (new),
`web/src/app/join/join.component.spec.ts` (new), `web/src/app/board/board.component.spec.ts`,
`web/src/app/auth/login/login.component.spec.ts`

**Intent**: Cover the new service, the join branches, the board affordance, and the returnUrl redirect.

**Contract**: Service — generate/preview/accept hit the right routes; URL helper builds `/join/<token>`.
Join — renders the household name on a valid preview; shows the invalid message on 404/410; an authenticated
no-household user's Join calls `accept` and routes to `/board`; an already-in-household user sees the block;
a logged-out user's actions navigate to `/login?returnUrl=...`. Board — the invite control calls `generate`
and copies the link (spy on `navigator.clipboard`). Login — a `returnUrl` param is honored on success.
Existing specs stay green.

### Success Criteria:

#### Automated Verification:

- Frontend builds: `npm run build` (in `web/`)
- Existing + new vitest specs pass: `npm test` (in `web/`)

#### Manual Verification:

- Clicking **Invite a member** generates a link and copies it; the link opens a `/join/<token>` page showing the household name
- Opening the link logged-out offers Register/Log in and, after authenticating, returns to the join page and joins → lands on the board
- Re-opening a consumed link shows "no longer valid"; opening it while already in a household shows the block
- The join page is usable at a 400px viewport

**Implementation Note**: Pause for manual confirmation before Phase 3.

---

## Phase 3: Live board — polling transport (F-03)

### Overview

Stand up the polling transport NFR-1 needs: `TaskService` refetches the board on a short interval, pausing on
a hidden tab and suppressing a tick while the user is mid-drag or has the task dialog open. The board owns the
lifecycle. Cover with vitest specs.

### Changes Required:

#### 1. TaskService: interval polling

**File**: `web/src/app/board/task.service.ts`

**Intent**: Refresh the board from the server every few seconds so a change one member makes appears for the
other within 5s (NFR-1), without hammering the API.

**Contract**: Add `startPolling(intervalMs)` / `stopPolling()`. While running, every interval it calls the
existing `load()` **unless** the tab is hidden (`document.hidden`) or a `paused` flag is set. Expose a way for
the board to set/clear `paused` (a method or a writable signal). A failed poll is swallowed (the next tick
retries); polling never throws into the UI. Reuse the rxjs already in the file (`timer`/`interval` +
`switchMap`); ensure `stopPolling()` tears the subscription down (no leak after navigation/logout).

#### 2. Board: drive the poll lifecycle + suppression

**File**: `web/src/app/board/board.component.ts`

**Intent**: Run polling only while the board is on screen, and never let a poll disrupt an in-progress
reorder or edit.

**Contract**: On init, `startPolling(<=5000ms)`; on destroy, `stopPolling()`. Set `paused` true on drag start
and while the task-detail dialog is open; clear it on drop/drag-end and dialog close. (The board already knows
both: the S-04 `drop` handler and the `openDetail` dialog lifecycle.) No change to the existing
mutation-then-refetch behavior — polling is additive.

#### 3. Frontend tests

**File**: `web/src/app/board/task.service.spec.ts`, `web/src/app/board/board.component.spec.ts`

**Intent**: Cover the polling tick, the pause/visibility gating, and the board lifecycle wiring.

**Contract**: Service — using fake timers, an interval tick calls `load()` (GET `/api/tasks`); a tick while
`paused` (or `document.hidden`) does **not**; `stopPolling()` halts further ticks. Board — `startPolling` is
called on init and `stopPolling` on destroy; `paused` is set during a drag and while the dialog is open, and
cleared after. Existing specs stay green.

### Success Criteria:

#### Automated Verification:

- Frontend builds: `npm run build` (in `web/`)
- All vitest specs pass: `npm test` (in `web/`)
- Release build bundles the SPA: `dotnet build -c Release` (exercises `BuildAngularSpa`)
- Full backend suite still green: `dotnet test`

#### Manual Verification:

- With two browser profiles signed in as two members, a task one creates/claims appears on the other's board within 5 seconds with no manual refresh
- Backgrounding the tab pauses polling (no requests); refocusing resumes and catches up
- A poll landing mid-drag or while the dialog is open does not reset the in-progress interaction
- No console errors / leaked intervals after navigating away or logging out

**Implementation Note**: Final phase — confirm the end-to-end two-member manual walk before closing the change.

---

## Testing Strategy

### Unit / Component Tests (vitest, `web/`):

- `InviteService`: generate/preview/accept hit the right routes; the URL helper builds `/join/<token>`
- `JoinComponent`: valid-preview render; invalid (404/410) message; authenticated-no-household Join → accept → `/board`; already-in-household block; logged-out actions navigate with `returnUrl`
- `BoardComponent`: the invite control calls `generate` + copies the link; polling starts on init / stops on destroy; `paused` set during drag and dialog
- `LoginComponent`: a `returnUrl` param is honored on success
- `TaskService`: an interval tick calls `load()`; a paused/hidden tick does not; `stopPolling()` halts ticks

### Integration Tests (xUnit, `tests/Homdutio.Api.Tests`):

- Generate (admin + adult member); preview (valid / 404 / 410)
- Accept: new member joins (200 + appears in `me`); single-use re-consume → 410; expired → 410; already-in-household → 409; token scoping (A not B); unauthenticated generate/accept → 401, preview anonymous

### Manual Testing Steps:

1. As admin A, click **Invite a member**; confirm a link is generated and copied
2. In a fresh browser profile, open the link; confirm it shows the household name
3. Register a new account from the join page; after login, confirm you return to the join page and join → land on the board
4. With both boards open, have each side create/claim a task; confirm the other side reflects it within 5 seconds
5. Re-open the consumed link → "no longer valid"; sign in as a member of another household and open a link → block
6. Background one tab; confirm polling pauses (no network) and resumes on refocus
7. Shrink the join page to 400px; confirm no horizontal scroll

## Performance Considerations

Polling is the only new recurring cost: one `GET /api/tasks` per active board per interval, paused on hidden
tabs — bounded and cheap at single-household scale on the B1 instance (the F-03 "cap the interval" guidance).
Invite generate/preview/accept are single-row operations; the token lookup is a unique-index hit. The accept's
optimistic-concurrency retry touches at most the one invite row plus one membership insert in a single
`SaveChanges`.

## Migration Notes

`AddHouseholdInvites` is **additive** — one new `HouseholdInvites` table (with a unique index on `Token` and a
`rowversion` column) and its FK to `Households`. No existing table is altered, so it is backward-compatible
(F-01) and safe for the F-04 migrate-first deploy.

## References

- Roadmap slice: `context/foundation/roadmap.md` (S-06, lines 192-202; F-03 polling decision, lines 101-113)
- PRD: US-02, FR-005 (single-use invite), FR-006 (join + in-flow account), FR-007 (one household per user), NFR-1 (5s freshness); role table + invite flow (`context/foundation/prd.md`)
- Household endpoint + scoping pattern: `src/Homdutio.Api/Households/HouseholdEndpoints.cs`
- Membership entity + one-household index: `src/Homdutio.Data/Entities/HouseholdMember.cs`, `src/Homdutio.Data/ApplicationDbContext.cs:44-62`
- Register (reused by join): `src/Homdutio.Api/Auth/AuthEndpoints.cs`
- Board service / polling home: `web/src/app/board/task.service.ts`; routing/guards: `web/src/app/app.routes.ts`, `web/src/app/household/membership.guard.ts`
- Prior slice (conventions): `context/changes/task-management-and-priority/plan.md`
- Test harness: `tests/Homdutio.Api.Tests/AuthApiFactory.cs`, `HouseholdEndpointsTests.cs`, `TaskEndpointsTests.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend — invite model + endpoints

#### Automated

- [x] 1.1 Solution builds: `dotnet build` — aa1fbba
- [x] 1.2 Migration is additive only (new `HouseholdInvites` table + indexes) — confirmed by reviewing the generated `Up` — aa1fbba
- [x] 1.3 API integration tests pass: `dotnet test` — aa1fbba
- [x] 1.4 Migration applies cleanly to a fresh DB (via test run) — aa1fbba

#### Manual

- [x] 1.5 Generate returns a token; preview returns the household name — aa1fbba
- [x] 1.6 A second account accepts and `GET /api/households/me` returns that household as `Member` — aa1fbba
- [x] 1.7 Re-accepting the same token → 410; accepting while already in a household → 409 — aa1fbba
- [x] 1.8 Preview/accept of an expired or unknown token → 410 / 404 — aa1fbba

### Phase 2: Frontend — invite generation + join flow

#### Automated

- [x] 2.1 Frontend builds: `npm run build` (in `web/`) — 26f2e31
- [x] 2.2 Existing + new vitest specs pass: `npm test` (in `web/`) — 26f2e31

#### Manual

- [x] 2.3 Invite a member generates + copies a link; opening it shows the household name — 26f2e31
- [x] 2.4 Logged-out open → Register/Log in → return to join → join → board — 26f2e31
- [x] 2.5 Re-opening a consumed link shows "no longer valid"; already-in-household shows the block — 26f2e31
- [x] 2.6 The join page is usable at a 400px viewport — 26f2e31

### Phase 3: Live board — polling transport (F-03)

#### Automated

- [x] 3.1 Frontend builds: `npm run build` (in `web/`)
- [x] 3.2 All vitest specs pass: `npm test` (in `web/`)
- [x] 3.3 Release build bundles the SPA: `dotnet build -c Release`
- [x] 3.4 Full backend suite still green: `dotnet test`

#### Manual

- [x] 3.5 A change by one member appears on the other's board within 5 seconds (two profiles)
- [x] 3.6 Backgrounding the tab pauses polling; refocus resumes and catches up
- [x] 3.7 A poll mid-drag / mid-dialog does not reset the in-progress interaction
- [x] 3.8 No console errors / leaked intervals after navigating away or logging out
