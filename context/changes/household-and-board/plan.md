# Household and Board (S-02) Implementation Plan

## Overview

S-02 turns a logged-in user with no household into a household admin looking at an empty,
mobile-first kanban board. It introduces the **first real domain schema** — `Household` and
`HouseholdMember` (with a `Role`) — a membership-scoped API to create and read a household, and the
Angular `create-household → board` flow. There is no `Task` entity yet (that arrives in S-03), so the
board is structurally empty: three columns, zero cards. The data-model and household-scoping decisions
made here are inherited by every later slice (S-03–S-09).

Delivers FR-004 (create household, become first admin), FR-017 (three-column board: To do / In
progress / Done), and NFR-2 (fully usable at ≤ 400 CSS px, no horizontal scroll).

## Current State Analysis

- **Data layer** holds Identity tables only. `ApplicationDbContext` extends `IdentityDbContext<ApplicationUser>`
  with no domain `DbSet`s (`src/Homdutio.Data/ApplicationDbContext.cs`). `ApplicationUser` is empty and
  carries a comment explicitly anticipating "the household membership link per FR-007, arriving with S-02"
  (`src/Homdutio.Data/Entities/ApplicationUser.cs:5-12`). Two migrations exist (`InitialCreate`, `AddIdentity`).
- **API layer** is minimal-API endpoint groups. `AuthEndpoints.MapAuthEndpoints()` maps `/api/auth/*`
  using `record` DTOs and `Results.Ok/ValidationProblem/Unauthorized` (`src/Homdutio.Api/Auth/AuthEndpoints.cs`).
  Endpoints are mapped in `Program.cs:91-94` **before** `MapFallbackToFile("index.html")`. JWT bearer is
  wired with `MapInboundClaims=false`, so the principal carries raw `sub` and `email` claims
  (`Program.cs:46-69`). `RequireAuthorization()` gates protected routes.
- **Auth test harness** exists and is reusable: `AuthApiFactory : WebApplicationFactory<Program>` spins a
  throwaway uniquely-named LocalDB, injects test JWT config, exposes `EnsureDatabaseMigrated()`, and drops
  the DB on dispose (`tests/Homdutio.Api.Tests/AuthApiFactory.cs`). `AuthEndpointsTests` shows the
  register→login→bearer pattern for authenticated calls (`tests/Homdutio.Api.Tests/AuthEndpointsTests.cs`).
- **Frontend** (S-01) is the pattern to mirror exactly: a root `@Injectable` service holds state in
  `signal`s and is the single source of truth (`web/src/app/auth/auth.service.ts`); a **functional** guard
  reads the signal synchronously and returns `true` or a `UrlTree` (`web/src/app/auth/auth.guard.ts`);
  routes are lazy `loadComponent` (`web/src/app/app.routes.ts`); components are standalone with reactive
  forms and `signal`-based `pending`/`error`/`notice` state (`web/src/app/auth/login/login.component.ts`);
  `mapValidationProblem` flattens RFC-7807 bodies (`web/src/app/auth/validation-problem.ts`). The
  placeholder `HomeComponent` is explicitly disposable — "S-02 will replace it"
  (`web/src/app/home/home.component.ts:7-8`). Login navigates to `/home` on success
  (`login.component.ts:61`). The dev proxy forwards `/api` to `http://localhost:5252` (`web/proxy.conf.json`);
  prod is genuinely same-origin (SPA served from `wwwroot`).
- **Tests** run via `dotnet test` (xUnit) and `npm test` (vitest, in `web/`).

## Desired End State

A logged-in user with no household is routed to a **create-household** screen, enters a household name,
submits, becomes that household's first **admin**, and lands on an empty three-column board (To do / In
progress / Done) headed by the household name and a role badge. A user who already has a household goes
straight to the board; attempting to create a second household is impossible both in the UI (guard) and
at the API (409). The board has no horizontal scroll at ≤ 400px. A `HouseholdMember` row links the user to
the household with a `Role`, enforced one-per-user by a unique index, ready for S-06 (invite/join inserts a
row) and S-09 (promote updates `Role`).

**Verification:** `dotnet test` and `npm test` green; manual walk of register → create-household → board
on a ≤400px viewport; a second `POST /api/households` for the same user returns 409.

### Key Discoveries:

- The board carries **no task data in S-02** — only membership identity is real, so the single read
  endpoint is `GET /api/households/me`, not a board/task endpoint (that lands in S-03).
- `MapInboundClaims=false` means the acting user id is read via `principal.FindFirstValue("sub")` — the
  same accessor `AuthEndpoints` uses for `/me` (`AuthEndpoints.cs:51-53`).
- Endpoint groups must be mapped **before** `MapFallbackToFile` in `Program.cs` or they return `index.html`.
- The migration is **purely additive** (two new tables, no change to `AspNet*` tables), satisfying F-01's
  "keep migrations backward-compatible" rule by construction.
- `AuthApiFactory` is auth-agnostic infrastructure (host + DB + JWT config) and can back the household tests.

## What We're NOT Doing

- **No `Task` entity, no task CRUD, no board mutations** — that is S-03 (accountability-loop). The board
  renders three empty columns from static markup.
- **No invite/join flow, no member list, no second member** — S-06.
- **No promote/demote or remove member** — S-09 (the `Role` column exists and stores `Admin`, but no UI/endpoint mutates it).
- **No cross-household isolation hardening pass** — the *pattern* (server-derived household, never trust a
  client id) is established here; the systematic verification sweep is S-07.
- **No drag-and-drop / reorder** — S-04.
- **No i18n** — labels ship in English (matching live S-01 UI); no localization layer.
- **No multi-household, no household switcher, no rename/delete household** — out of v1 scope (FR-007).
- **No refresh-token / persistent auth changes** — unchanged from S-01 (in-memory token).

## Implementation Approach

Three phases, backend-first so the frontend builds against a real contract:

1. **Backend** — add the domain entities + an additive migration, then two endpoints (`GET
   /api/households/me`, `POST /api/households`) that derive the acting household from the JWT `sub` claim
   and never trust a client-supplied id. Integration tests lock the one-household invariant and the
   server-side scoping.
2. **Frontend plumbing + create flow** — a root `HouseholdService` (signal state mirroring `AuthService`),
   an async **membership guard** that loads membership once and decides create-vs-board, the route changes
   (`/create-household`, `/board`, retargeted login), the `CreateHouseholdComponent`, and a minimal board
   placeholder as the guard's redirect target.
3. **Board UI** — flesh out `BoardComponent` with the header (name + role badge) and the three responsive
   columns (vertical stack on mobile → side-by-side above a breakpoint), remove the obsolete `HomeComponent`,
   and add the vitest specs.

## Critical Implementation Details

- **Membership guard is asynchronous.** Unlike `authGuard` (which reads an already-present in-memory token
  synchronously), the membership guard runs right after login when household state is unknown, so it must
  trigger `HouseholdService.loadMine()` and resolve a `boolean | UrlTree` from the result
  (`Observable`/`Promise` return). It must cache the loaded state so it does not refetch on every
  navigation, and it must treat a `204`/empty membership as "no household" (→ `/create-household`), not an error.
- **Guard direction is bidirectional.** `/board` requires a household (else → `/create-household`);
  `/create-household` requires the *absence* of one (else → `/board`). A single guard parameterized by the
  required state, or two thin guards, both work — keep them consistent so the two routes can never both be
  dead-ended.
- **`Role` is stored, not inferred.** The creator's row is written with `Role = Admin`. Persist the enum
  explicitly (string conversion preferred for readable rows) so S-09 can update it without a schema change.

## Phase 1: Domain schema + backend endpoints

### Overview

Add the `Household` and `HouseholdMember` entities and a `Role` enum, wire them into the DbContext with a
unique index enforcing one household per user, generate an additive migration, and expose the two
household endpoints with JWT-derived scoping and integration tests.

### Changes Required:

#### 1. Domain entities + role enum

**File**: `src/Homdutio.Data/Entities/Household.cs`, `src/Homdutio.Data/Entities/HouseholdMember.cs`,
`src/Homdutio.Data/Entities/HouseholdRole.cs`

**Intent**: Introduce the first domain tables. `Household` carries identity (name + creation timestamp);
`HouseholdMember` is the join row linking an `ApplicationUser` to a `Household` with a role and join
timestamp — the representation that lets S-06 add members and S-09 change roles without restructuring.

**Contract**:
- `Household`: `Guid Id`, `string Name` (required), `DateTime CreatedAtUtc`. Optional `ICollection<HouseholdMember> Members`.
- `HouseholdMember`: `Guid Id`, `Guid HouseholdId` (FK → `Household`), `string UserId` (FK → `AspNetUsers.Id`), `HouseholdRole Role`, `DateTime JoinedAtUtc`.
- `HouseholdRole`: `enum { Admin, Member }`.
- One household per user is a **DB invariant**: a unique index on `HouseholdMember.UserId` (v1 enforcement of FR-007).

#### 2. DbContext configuration

**File**: `src/Homdutio.Data/ApplicationDbContext.cs`

**Intent**: Register the new sets and configure relationships, the unique `UserId` index, and the `Role`
storage so the schema is explicit rather than convention-derived.

**Contract**: Add `DbSet<Household>` and `DbSet<HouseholdMember>`. In `OnModelCreating` (call `base` first —
Identity needs it): unique index on `HouseholdMember.UserId`; FK `HouseholdMember.HouseholdId → Household`;
FK `HouseholdMember.UserId → ApplicationUser.Id`; `Role` stored via `HasConversion<string>()`; `Name`
`IsRequired()` with a sane `HasMaxLength`.

#### 3. EF migration

**File**: `src/Homdutio.Data/Migrations/<timestamp>_AddHouseholds.cs` (generated)

**Intent**: Create the two tables additively, with no change to existing `AspNet*` tables, so the migration
is backward-compatible (F-01 rule) and safe for the CI migrate-first deploy (F-04).

**Contract**: `dotnet ef migrations add AddHouseholds` against `Homdutio.Data` (startup project
`Homdutio.Api`). Review the generated `Up` to confirm it only **adds** `Households` and `HouseholdMembers`
(+ the unique index + FKs) and touches no Identity table.

#### 4. Household endpoints

**File**: `src/Homdutio.Api/Households/HouseholdEndpoints.cs`

**Intent**: Expose membership read + household creation. The acting user comes from the JWT `sub` claim;
the household is derived server-side (the create/read never accept a client-supplied household id),
establishing the S-07 isolation pattern at the first domain endpoint.

**Contract**:
- `MapHouseholdEndpoints(this IEndpointRouteBuilder)`, group `/api/households`, all `.RequireAuthorization()`.
- `GET /api/households/me` → `200 { id, name, role }` when the caller has a membership; `204 No Content` when not (preferred over 404 for an authenticated "you have none" on a /me-style endpoint). Resolve via `sub` claim → `HouseholdMember` lookup → include `Household`.
- `POST /api/households` body `{ name }` → `400 ValidationProblem` if name missing/blank; `409 Conflict` if the caller already has a membership; otherwise create `Household`, insert a `HouseholdMember { Role = Admin }` for the caller, return `201`/`200 { id, name, role }`.
- DTO `record`s alongside the endpoints (mirror `AuthEndpoints` DTO style): `CreateHouseholdRequest(string Name)`, `HouseholdResponse(Guid Id, string Name, string Role)`.
- Read `sub` via `principal.FindFirstValue("sub")` (matches `AuthEndpoints.cs:51-53`).

#### 5. Wire endpoints into the host

**File**: `src/Homdutio.Api/Program.cs`

**Intent**: Map the household group into the pipeline alongside the existing groups.

**Contract**: Add `app.MapHouseholdEndpoints();` next to `app.MapAuthEndpoints();` (`Program.cs:92`) —
**before** `MapFallbackToFile`.

#### 6. Integration tests

**File**: `tests/Homdutio.Api.Tests/HouseholdEndpointsTests.cs`

**Intent**: Lock the invariants this slice introduces: create→read round-trip, name validation,
one-household-per-user, auth requirement, and server-side scoping.

**Contract**: Reuse `AuthApiFactory` (host + throwaway DB + JWT). Cover: (a) authed `POST` creates a
household and the caller is `Admin`; (b) `GET /me` returns it; (c) a second `POST` for the same user → `409`;
(d) blank name → `400`; (e) unauthenticated `POST`/`GET /me` → `401`; (f) a fresh user's `GET /me` →
`204`. Follow the register→login→bearer helper pattern from `AuthEndpointsTests`.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- Migration is additive only (no `AspNet*` table changes) — confirmed by reviewing the generated `Up`
- API integration tests pass: `dotnet test`
- Migration applies cleanly to a fresh DB (exercised by `AuthApiFactory.EnsureDatabaseMigrated()` in the test run)

#### Manual Verification:

- `POST /api/households` with a bearer token creates a household and a second call returns 409
- `GET /api/households/me` returns the household for a member and 404/204 for a fresh user
- A request with no/invalid token returns 401

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual
confirmation before proceeding to Phase 2.

---

## Phase 2: Frontend household service, routing & create flow

### Overview

Add the root `HouseholdService` (signal state), the async membership guard, the route changes that replace
the placeholder home with `/create-household` + `/board`, the create-household form, and a minimal board
placeholder that the guard can redirect to.

### Changes Required:

#### 1. Household service

**File**: `web/src/app/household/household.service.ts`

**Intent**: Single source of truth for the caller's household, mirroring `AuthService`. Holds membership in
a signal, loads it once after login, and creates a household.

**Contract**: `@Injectable({ providedIn: 'root' })`. Signal `_household = signal<Household | null>(null)`
plus a `_loaded` flag so the guard can tell "not loaded yet" from "loaded, none". `Household` interface
`{ id: string; name: string; role: 'Admin' | 'Member' }`. Methods: `loadMine(): Observable<Household | null>`
(`GET /api/households/me`, maps 204 → `null`, caches), `create(name): Observable<Household>`
(`POST /api/households`, sets the signal on success), `current` readonly signal, `clearOnLogout()` to reset
state. `clearOnLogout()` MUST reset **both** `_household` and `_loaded` — resetting only `_household`
leaves `_loaded = true`, so a different user logging in on the same page load is treated as "loaded, no
household" and mis-routed to `/create-household` (the guard never refetches). Wire the reset into
`AuthService.logout()` (or have the service react) so a logout/login as a different user doesn't leak the
prior household.

#### 2. Membership guard

**File**: `web/src/app/household/membership.guard.ts`

**Intent**: Gate `/board` (requires a household) and `/create-household` (requires none), loading membership
on first use. This is the create-vs-board router.

**Contract**: Functional `CanActivateFn`(s) returning `boolean | UrlTree | Observable<boolean | UrlTree>`.
If membership not yet loaded, call `loadMine()` then decide. `requireHousehold`: household present → `true`,
else `UrlTree('/create-household')`. `requireNoHousehold`: household absent → `true`, else
`UrlTree('/board')`. Both run after `authGuard` (chain on the routes). See Critical Implementation Details
re async + caching + 404-as-empty.

#### 3. Routing changes

**File**: `web/src/app/app.routes.ts`

**Intent**: Replace the placeholder `/home` with the two real routes and a sensible default.

**Contract**: `''` → redirect to `board`. `create-household` → `canActivate: [authGuard, requireNoHousehold]`,
lazy-loads `CreateHouseholdComponent`. `board` → `canActivate: [authGuard, requireHousehold]`, lazy-loads
`BoardComponent`. `**` → redirect to `board`. Remove the `home` route. (`login`/`register` unchanged.)

#### 4. Retarget post-login navigation

**File**: `web/src/app/auth/login/login.component.ts`

**Intent**: After login, send the user to `/board`; the membership guard bounces a no-household user onward
to `/create-household`.

**Contract**: Change the success `navigate(['/home'])` (`login.component.ts:61`) to `navigate(['/board'])`.
Also update `web/src/app/auth/login/login.component.spec.ts:35,43` — the existing test asserts
`navigate(['/home'])` (and is named "navigates to /home on a successful login"); retarget the assertion
and the test name to `/board`, or criterion 2.3 ("existing vitest specs still pass") will fail.

#### 5. Create-household component

**File**: `web/src/app/household/create-household/create-household.component.{ts,html,scss}`

**Intent**: A reactive form with one required `name` field; on submit, create the household and navigate to
the board. Mirror `LoginComponent`'s structure (signals for `pending`/`error`, `mapValidationProblem`).

**Contract**: Standalone, `ReactiveFormsModule`. `name` control `[Validators.required]`. Submit → guard
against invalid/pending → `HouseholdService.create(name)` → on success `navigate(['/board'])`; on `409`
navigate to `/board` (already has one); on `400` show mapped messages via `mapValidationProblem`. Mobile-first
SCSS reusing the S-01 card/page styling. A one-line intro heading so the bare form isn't context-free.

#### 6. Minimal board placeholder

**File**: `web/src/app/board/board.component.{ts,html,scss}`

**Intent**: Exist as the guard's redirect target so Phase 2 is verifiable end-to-end; Phase 3 fleshes it out.

**Contract**: Standalone component reading `HouseholdService.current`; render the household name and a "board
coming together" stub. (Phase 3 replaces the template with the real columns.)

### Success Criteria:

#### Automated Verification:

- Frontend builds: `npm run build` (in `web/`)
- Existing + updated vitest specs pass: `npm test` (in `web/`)

#### Manual Verification:

- A newly registered user logging in is routed to `/create-household`
- Submitting a name creates the household and lands on `/board` (placeholder) showing the name
- Reloading on `/board` (token gone) redirects to `/login`; logging back in returns to the board (membership reloads)
- Navigating to `/create-household` while already in a household redirects to `/board`

**Implementation Note**: Pause for manual confirmation before Phase 3.

---

## Phase 3: Empty board UI

### Overview

Replace the board placeholder with the real empty board: a header (household name + role badge) and three
responsive columns that stack vertically at ≤ 400px and sit side-by-side on wider screens, with empty-state
placeholders. Remove the obsolete `HomeComponent` and add vitest coverage.

### Changes Required:

#### 1. Board component + header

**File**: `web/src/app/board/board.component.{ts,html}`

**Intent**: Render the household identity and the three named columns from the service state.

**Contract**: Header shows `current().name` and a role badge (`Admin`/`Member`). Three columns labelled
**To do**, **In progress**, **Done** (English), each with an empty-state placeholder (e.g. "No tasks yet").
No task data — markup only.

#### 2. Responsive board layout

**File**: `web/src/app/board/board.component.scss`

**Intent**: Satisfy NFR-2 — columns stack full-width into a single vertical scroll on phones and switch to
side-by-side above a breakpoint, with **no horizontal scroll** at ≤ 400px. This layout is inherited by every
later board slice, so get it right here.

**Contract**: Default (mobile) = single-column vertical stack; a `min-width` media query (e.g. ≥ 640px) =
three side-by-side columns (flex/grid). No fixed widths that force overflow below 400px.

#### 3. Remove obsolete placeholder home

**File**: delete `web/src/app/home/home.component.{ts,html,scss,spec.ts}`

**Intent**: The placeholder home is superseded by the board; remove it and any dangling references.

**Contract**: Delete the `home/` component files; ensure no route or import references `HomeComponent`
(the route was already removed in Phase 2). Update `app.spec.ts` if it asserts on the old route/landing.

#### 4. Frontend tests

**File**: `web/src/app/household/household.service.spec.ts`,
`web/src/app/household/membership.guard.spec.ts`,
`web/src/app/household/create-household/create-household.component.spec.ts`,
`web/src/app/board/board.component.spec.ts`

**Intent**: Cover the new conventions and the create-vs-board routing at the right layers (mirroring the S-01
spec set).

**Contract**: Service — `loadMine` maps 404 → null and caches; `create` sets state. Guard — present →
`true`, absent → redirect `UrlTree`, and the bidirectional pair. Create form — required validation,
success-navigates, 409-redirects, 400-maps. Board — renders name, role badge, and the three column labels.

### Success Criteria:

#### Automated Verification:

- Frontend builds: `npm run build` (in `web/`)
- All vitest specs pass: `npm test` (in `web/`)
- Release build bundles the SPA: `dotnet build -c Release` (exercises `BuildAngularSpa`)
- Full backend suite still green: `dotnet test`

#### Manual Verification:

- At a 400px viewport the board shows the three columns with **no horizontal scroll**
- The header shows the household name and the correct role badge (Admin for the creator)
- Above the breakpoint the columns sit side-by-side; below, they stack
- No references to the removed placeholder home remain; full flow register → create → board works end-to-end

**Implementation Note**: Final phase — confirm the end-to-end manual walk before closing the change.

---

## Testing Strategy

### Unit / Component Tests (vitest, `web/`):

- `HouseholdService`: `loadMine` 404→null + caching; `create` updates the signal; logout resets state
- `membership.guard`: household present → `true`; absent → `UrlTree` redirect; both directions
- `CreateHouseholdComponent`: required-name validation; success navigation; 409 redirect; 400 mapped errors
- `BoardComponent`: renders name, role badge, and the three column labels

### Integration Tests (xUnit, `tests/Homdutio.Api.Tests`):

- Create → `GET /me` round-trip with the caller as `Admin`
- Second create for the same user → 409
- Blank name → 400; unauthenticated → 401; fresh user `GET /me` → 404/204

### Manual Testing Steps:

1. Register a new user, log in → confirm redirect to `/create-household`
2. Submit a household name → confirm landing on `/board` with the name in the header and an Admin badge
3. Shrink the viewport to 400px → confirm three columns, no horizontal scroll
4. Reload on `/board` → confirm redirect to `/login`; log back in → confirm return to the board
5. Manually navigate to `/create-household` while in a household → confirm redirect to `/board`
6. (API) Fire a second `POST /api/households` with the same token → confirm 409

## Performance Considerations

Negligible. The membership lookup is a single indexed query per the (rare) guard load; the board renders
static markup with no task data. The unique index on `HouseholdMember.UserId` also serves the `GET /me`
lookup. Azure SQL Basic (5 DTU) is untroubled by this volume.

## Migration Notes

`AddHouseholds` is **additive** — two new tables plus a unique index and FKs, no change to existing
`AspNet*` tables — so it is backward-compatible (F-01 rule) and safe for the F-04 migrate-first deploy. No
data backfill is needed; existing users simply have no `HouseholdMember` row until they create a household.

## References

- Roadmap slice: `context/foundation/roadmap.md` (S-02, lines 143-153)
- PRD: FR-004, FR-007, FR-017, NFR-2; role model (`context/foundation/prd.md`)
- Prior slice (conventions mirrored): `context/changes/account-access/plan.md`
- Backend endpoint pattern: `src/Homdutio.Api/Auth/AuthEndpoints.cs`
- Test harness: `tests/Homdutio.Api.Tests/AuthApiFactory.cs`, `AuthEndpointsTests.cs`
- Frontend service/guard pattern: `web/src/app/auth/auth.service.ts`, `web/src/app/auth/auth.guard.ts`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Domain schema + backend endpoints

#### Automated

- [x] 1.1 Solution builds: `dotnet build` — b2173c2
- [x] 1.2 Migration is additive only (no `AspNet*` table changes) — b2173c2
- [x] 1.3 API integration tests pass: `dotnet test` — b2173c2
- [x] 1.4 Migration applies cleanly to a fresh DB (via test run) — b2173c2

#### Manual

- [x] 1.5 Create → second-create 409 verified with a bearer token — b2173c2
- [x] 1.6 `GET /me` returns household for a member, 404/204 for a fresh user — b2173c2
- [x] 1.7 No/invalid token → 401 — b2173c2

### Phase 2: Frontend household service, routing & create flow

#### Automated

- [x] 2.1 Frontend builds: `npm run build` — 9523d78
- [x] 2.2 Existing + updated vitest specs pass: `npm test` — 9523d78

#### Manual

- [x] 2.3 New user logging in is routed to `/create-household` — 9523d78
- [x] 2.4 Submitting a name creates the household and lands on `/board` with the name — 9523d78
- [x] 2.5 Reload on `/board` redirects to `/login`; re-login returns to the board — 9523d78
- [x] 2.6 Visiting `/create-household` while in a household redirects to `/board` — 9523d78

### Phase 3: Empty board UI

#### Automated

- [x] 3.1 Frontend builds: `npm run build`
- [x] 3.2 All vitest specs pass: `npm test`
- [x] 3.3 Release build bundles the SPA: `dotnet build -c Release`
- [x] 3.4 Full backend suite still green: `dotnet test`

#### Manual

- [x] 3.5 At 400px the board shows three columns with no horizontal scroll
- [x] 3.6 Header shows household name and the correct role badge (Admin for creator)
- [x] 3.7 Columns side-by-side above the breakpoint, stacked below
- [x] 3.8 No references to the removed placeholder home; full flow works end-to-end
