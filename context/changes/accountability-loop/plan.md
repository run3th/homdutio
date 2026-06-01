# Accountability Loop (S-03) Implementation Plan

## Overview

S-03 is the product's **north star**: a household member creates a task, claims it, marks it done, and
an admin confirms it — at which point the task closes off the board while a durable record of who did
what, and when, persists. This is the first `HouseholdTask` entity, the first board *mutations*, and the
first interactive board UI. The confirm step is the felt accountability event the whole product rides on.

The record is **event-sourced**: an append-only `TaskEvent` log is the durable audit trail (NFR-3), while
the `HouseholdTask` row carries a current-state projection (status + claimer/confirmer + timestamps) for
cheap board rendering. Closure is a state transition (`ClosedAtUtc` set), never a delete. Every transition
is a guarded endpoint that rejects illegal/stale moves with 409, and each task DTO carries server-computed
affordance flags (`canClaim` / `canMarkDone` / `canConfirm`) so the SPA stays dumb about authorization.

Delivers US-01, FR-010 (create), FR-013 (claim), FR-014 (mark done), FR-015 (admin confirm), FR-016
(self-attested), FR-018 (card display), NFR-3 (durable record). A `DisplayName` is added to accounts so
cards read as names, not raw emails.

## Current State Analysis

- **Data layer** holds Identity + the S-02 household domain. `ApplicationDbContext` extends
  `IdentityDbContext<ApplicationUser>` with `DbSet<Household>` and `DbSet<HouseholdMember>`
  (`src/Homdutio.Data/ApplicationDbContext.cs`). `ApplicationUser` is still empty
  (`src/Homdutio.Data/Entities/ApplicationUser.cs`). Three migrations exist (`InitialCreate`,
  `AddIdentity`, `AddHouseholds`). `HouseholdRole { Admin, Member }` stored as a string.
- **Household scoping pattern is set and must be reused exactly**: the acting user is read from the JWT
  `sub` claim via `principal.FindFirstValue("sub")`; the household is derived server-side by looking up the
  caller's `HouseholdMember` — endpoints never accept a client-supplied household id
  (`src/Homdutio.Api/Households/HouseholdEndpoints.cs:21-33,46-48`). This is the cross-household isolation
  pattern S-07 later hardens; S-03 must follow it for every new endpoint.
- **API style** is minimal-API endpoint groups with `record` DTOs and `Results.Ok/ValidationProblem/
  Created/Conflict/NoContent` (`HouseholdEndpoints.cs`, `Auth/AuthEndpoints.cs`). Groups are mapped in
  `Program.cs:92-94` **before** `MapFallbackToFile("index.html")`. `RequireAuthorization()` gates routes.
- **Roles are stored, not inferred** — confirm authorization (FR-015 admin-only) reads `HouseholdMember.Role`.
- **Frontend** is signal-based and standalone. `HouseholdService` (`providedIn: 'root'`) holds the
  household in a signal, loads once, and resets on logout (`web/src/app/household/household.service.ts`);
  `AuthService` holds the in-memory token + **email only — not the user id**
  (`web/src/app/auth/auth.service.ts:32-34`). `BoardComponent` today renders three **static** columns from
  `HouseholdService.current` with no task data and no API call (`web/src/app/board/board.component.ts:23`,
  `board.component.html`). The register form lives at `web/src/app/auth/register/`.
- **No live-update transport** — F-03 (polling) is a separate, later slice; the roadmap sequences
  cross-member freshness into S-06. S-03's loop is verifiable on **one device**, and FR-016's self-attested
  path lets a lone admin run the whole loop end-to-end.
- **Test harness** is reusable: `AuthApiFactory : WebApplicationFactory<Program>` spins a uniquely-named
  throwaway LocalDB, injects test JWT config, exposes `EnsureDatabaseMigrated()`, drops the DB on dispose
  (`tests/Homdutio.Api.Tests/AuthApiFactory.cs`); `HouseholdEndpointsTests` and `AuthEndpointsTests` show
  the register→login→bearer pattern. Tests run via `dotnet test` (xUnit) and `npm test` (vitest, in `web/`).

## Desired End State

On the board, a logged-in household member sees a **create-task** affordance and three live columns. They
add a task ("take out bins") — it appears in **To do** unassigned, showing the creator and a creation
timestamp. Any member claims it — it moves to **In progress** carrying the claimer's display name. The
claimer marks it done — it moves to **Done**. An admin confirms it — the card disappears from the board.
Behind the card, a `HouseholdTask` row (status `Done`, `ClosedAtUtc` set) and an immutable `TaskEvent`
chain (Created → Claimed → MarkedDone → Confirmed, the last flagged `self-attested` when the admin
confirmed their own claim) persist for the lifetime of the household. Illegal moves (claiming an
already-claimed task, a non-claimer marking done, a member confirming, confirming a non-Done task) are
rejected server-side; a task id from another household returns 404 (no existence leak).

**Verification:** `dotnet test` and `npm test` green; a manual single-device walk of create → claim → mark
done → confirm, watching the card move across columns and vanish on confirm; a closed task absent from
`GET /api/tasks` but its row + events still queryable; a second `POST /claim` on a claimed task → 409.

### Key Discoveries:

- **`Task` is a reserved-feeling name in C#** (`System.Threading.Tasks.Task`). Entities/enums use the
  `HouseholdTask` / `HouseholdTaskStatus` prefix to avoid ambiguity in an `async` codebase.
- **The board renders no task data today** — S-03 is the first time `BoardComponent` calls an API.
  Closed (confirmed) tasks simply don't come back from `GET /api/tasks`, so they vanish with no client
  bookkeeping.
- **The SPA has no user id, only email** (`AuthService`). Server-computed affordance flags sidestep this:
  the client never compares identities, it just renders the buttons the DTO permits.
- **One household per user is a DB invariant** (unique index on `HouseholdMember.UserId`), so resolving the
  caller's household is a single indexed lookup reused by every task endpoint.
- **Migrations must stay additive** (F-01 rule; F-04 migrate-first deploy): the new tables + the
  `DisplayName` column touch no existing table's shape destructively.

## What We're NOT Doing

- **No edit, no delete, no reorder** — FR-011/012/021 are S-04. Tasks order by `CreatedAtUtc` only; there
  is no manual-order field and no drag-and-drop.
- **No unclaim, no admin send-back** — FR-022/023 are S-05. The `TaskEvent` log is shaped so S-05 can
  *append* `Unclaimed` / `SentBack` events without restructuring, but neither transition ships here.
- **No cross-member live updates / polling** — F-03 + S-06. The acting client refetches its own board; a
  second member sees changes only when they act or reload. By design.
- **No second member / invite flow** — S-06. FR-015's cross-member confirm is fully exercised only once a
  second member exists; here the self-attested path (FR-016) covers single-device verification.
- **No cross-household isolation *sweep*** — the per-endpoint scoping pattern (server-derived household,
  foreign id → 404) is applied here, but the systematic verification is S-07.
- **No `self-attested` UI surfacing beyond a confirm-time hint** — the flag is recorded durably (NFR-3);
  per PRD Open Q#5 no view ever surfaces it. The board does not badge self-attested closures.
- **No aggregation / "who did what this week"** — v2 (Non-Goal). The event log makes it possible later; no
  view reads it in aggregate now.
- **No category taxonomy** — `Category` is an optional free-text string; no predefined list, no filtering.

## Implementation Approach

Three phases, backend-first so the SPA builds against a real contract (mirrors S-02):

1. **Backend domain + lifecycle API** — add `HouseholdTask` + `TaskEvent` (+ enums) and a `DisplayName`
   column on `ApplicationUser`; one additive migration; four guarded transition endpoints under
   `/api/tasks` that derive the household from the JWT `sub`, append a `TaskEvent` per transition, and
   return DTOs with server-computed affordance flags; integration tests lock the full loop, the
   self-attested path, every guard, and the foreign-household 404.
2. **Frontend task service + display-name + render/create** — a root `TaskService` (signal state,
   refetch-on-action, reset-on-logout) mirroring `HouseholdService`; capture a display name at register;
   `BoardComponent` reads live tasks into the three columns rendering FR-018 cards; the create-task form.
3. **Lifecycle actions in the UI + closure** — affordance-driven Claim / Mark done / Confirm buttons,
   refetch after each action, confirmed cards leaving the board, a self-attested confirm hint, responsive
   card layout at ≤400px (NFR-2), and the vitest spec set.

## Critical Implementation Details

- **Naming.** Use `HouseholdTask` (entity) and `HouseholdTaskStatus` (enum) — never a bare `Task` — to
  avoid colliding with `System.Threading.Tasks.Task` throughout an `async` API.
- **Closure is `ClosedAtUtc`, not a status value.** The status enum stays at `Done` after confirmation;
  closure is the non-null `ClosedAtUtc`. `GET /api/tasks` **must** filter `ClosedAtUtc == null` — forgetting
  this leaves confirmed cards on the board. This two-field shape is the answer chosen during planning
  (status enum + separate `ClosedAtUtc`) and is why the board filter is on the timestamp, not the status.
- **Projection and event commit together.** Each transition mutates the `HouseholdTask` projection *and*
  appends one `TaskEvent` in a **single `SaveChanges`** so the audit log can never diverge from current
  state. Do **not** wrap multiple `SaveChanges` calls in a manual `BeginTransaction` — the DbContext uses
  `EnableRetryOnFailure`, whose execution strategy disallows user-initiated transactions spanning the retry
  boundary (`Program.cs:24-28`). One atomic `SaveChanges` per transition sidesteps this entirely.
- **`self-attested` is computed server-side at confirm time** as `confirmerId == ClaimedById`, recorded on
  both the `Confirmed` `TaskEvent` and the `HouseholdTask` projection. It is never trusted from the client.
- **Foreign-household task id → 404, not 403.** A task whose household ≠ the caller's resolves as
  not-found (no existence leak), establishing the US-02/FR-019 pattern at every task endpoint (S-07 verifies
  it systematically).

## Phase 1: Backend domain + lifecycle API

### Overview

Add the `HouseholdTask` and `TaskEvent` entities (+ `HouseholdTaskStatus` and `TaskEventType` enums) and a
`DisplayName` on `ApplicationUser`; configure them in the DbContext; generate one additive migration; expose
the four guarded `/api/tasks` transition endpoints with household scoping, event appends, and affordance
flags; and lock the behaviour with integration tests.

### Changes Required:

#### 1. Task + event entities and enums

**File**: `src/Homdutio.Data/Entities/HouseholdTask.cs`,
`src/Homdutio.Data/Entities/TaskEvent.cs`,
`src/Homdutio.Data/Entities/HouseholdTaskStatus.cs`,
`src/Homdutio.Data/Entities/TaskEventType.cs`

**Intent**: Introduce the task entity as a current-state projection plus an append-only event log as the
durable audit record (NFR-3). The `HouseholdTask` columns make board rendering one query; the `TaskEvent`
chain is the honest who-did-what record that outlives the visible card.

**Contract**:
- `HouseholdTask`: `Guid Id`; `Guid HouseholdId` (FK → `Household`, the scoping key); `string Title`
  (required); `string? Description`; `string? Category`; `HouseholdTaskStatus Status`;
  `string CreatedById` (FK → `AspNetUsers.Id`); `DateTime CreatedAtUtc`; `string? ClaimedById`;
  `DateTime? ClaimedAtUtc`; `DateTime? DoneAtUtc`; `string? ConfirmedById`; `DateTime? ClosedAtUtc`;
  `bool SelfAttested`. Optional `ICollection<TaskEvent> Events`.
- `TaskEvent`: `Guid Id`; `Guid TaskId` (FK → `HouseholdTask`); `TaskEventType Type`; `string ActorId`
  (FK → `AspNetUsers.Id`, who performed it); `DateTime OccurredAtUtc`; `bool SelfAttested` (meaningful on
  `Confirmed`, default `false`). Append-only — no update/delete path in v1.
- `HouseholdTaskStatus`: `enum { ToDo, InProgress, Done }`. Closure is `ClosedAtUtc != null`, **not** a
  status value.
- `TaskEventType`: `enum { Created, Claimed, MarkedDone, Confirmed }`. (S-05 appends `Unclaimed`/`SentBack`.)

#### 2. DisplayName on ApplicationUser

**File**: `src/Homdutio.Data/Entities/ApplicationUser.cs`

**Intent**: Give accounts a human-friendly name so task cards read as names, not raw emails (the decision
made during planning). Existing rows are backfilled from the email local-part in the migration.

**Contract**: Add `string DisplayName { get; set; } = string.Empty;`. Configured `IsRequired()` with a sane
`HasMaxLength` in the DbContext. Populated at registration (see change 6); falls back to the email local-part
when blank.

#### 3. DbContext configuration

**File**: `src/Homdutio.Data/ApplicationDbContext.cs`

**Intent**: Register the new sets, the household FK (scoping), the task→events relationship, and the
`DisplayName` constraint so the schema is explicit.

**Contract**: Add `DbSet<HouseholdTask>` and `DbSet<TaskEvent>`. In `OnModelCreating` (after `base`):
FK `HouseholdTask.HouseholdId → Household` (cascade); FK `TaskEvent.TaskId → HouseholdTask` (cascade);
`Status` stored via `HasConversion<string>()`; `TaskEventType` via `HasConversion<string>()`; `Title`
`IsRequired()` + `HasMaxLength`; index on `HouseholdTask.HouseholdId` (backs the board query). Configure
`ApplicationUser.DisplayName` `IsRequired().HasMaxLength(...)`. Leave the `CreatedById`/`ClaimedById`/
`ConfirmedById`/`ActorId` string FKs unmapped-to-navigation (raw id columns are enough; no reverse nav
needed) — or map them without a required cascade to avoid multiple cascade paths through `AspNetUsers`.

#### 4. EF migration

**File**: `src/Homdutio.Data/Migrations/<timestamp>_AddTasksAndDisplayName.cs` (generated)

**Intent**: Create `HouseholdTasks` + `TaskEvents` and add `AspNetUsers.DisplayName` additively, backfilling
existing users' display name, so the migration is backward-compatible (F-01) and safe for the F-04
migrate-first deploy.

**Contract**: `dotnet ef migrations add AddTasksAndDisplayName` (project `Homdutio.Data`, startup
`Homdutio.Api`). Review the generated `Up`: it must only **add** the two tables (+ FKs + the `HouseholdId`
index) and the `DisplayName` column. Add a backfill so existing rows are not left blank — set
`DisplayName` to the portion of `Email` before `@` for rows where it is empty (a raw `migrationBuilder.Sql`
`UPDATE`, or add the column nullable → backfill → leave a non-null default). No existing table is dropped or
retyped.

#### 5. Task lifecycle endpoints

**File**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs`

**Intent**: Expose the board read + the four guarded transitions. Every endpoint derives the caller's
household from the JWT `sub`, scopes by it, guards the current state + actor eligibility, appends a
`TaskEvent`, and returns DTOs with server-computed affordance flags. This is where the state machine lives.

**Contract**:
- `MapTaskEndpoints(this IEndpointRouteBuilder)`, group `/api/tasks`, all `.RequireAuthorization()`.
- A private helper resolves the caller's `HouseholdMember` (via `sub`) → `(householdId, role, userId)`;
  no membership → `404` (caller has no board — surfaced as not-found, consistent with the foreign-household
  rule, no existence leak). A second helper loads a task **scoped to the caller's
  household**, returning not-found for a foreign or missing id (no existence leak).
- `GET /api/tasks` → `200` list of `TaskResponse`, scoped to the caller's household, **excluding
  `ClosedAtUtc != null`**, ordered by `Status` then `CreatedAtUtc`. Each item carries the affordance flags
  computed for the caller.
- `POST /api/tasks` body `{ title, description?, category? }` → `400 ValidationProblem` on blank title;
  else create `HouseholdTask` (`Status = ToDo`, `CreatedById = caller`), append a `Created` `TaskEvent`,
  one `SaveChanges`, return `201` `TaskResponse`.
- `POST /api/tasks/{id}/claim` → guard `Status == ToDo` (else `409`); set `ClaimedById = caller`,
  `ClaimedAtUtc`, `Status = InProgress`; append `Claimed`; return `200` `TaskResponse`.
- `POST /api/tasks/{id}/done` → guard `Status == InProgress` (wrong state → `409`) **and** `ClaimedById ==
  caller` (not the claimer → `403`); set `DoneAtUtc`, `Status = Done`; append `MarkedDone`; return `200`.
- `POST /api/tasks/{id}/confirm` → guard caller `Role == Admin` (else `403`) **and** `Status == Done`
  (else `409`); set `ConfirmedById = caller`, `ClosedAtUtc = now`, `SelfAttested = (ClaimedById == caller)`;
  append `Confirmed` (with `SelfAttested`); return `200` (the task is now closed → the SPA drops it).
- DTOs (mirror `HouseholdEndpoints` `record` style): `CreateTaskRequest(string Title, string? Description,
  string? Category)`; `TaskResponse(Guid Id, string Title, string? Description, string? Category, string
  Status, string CreatedByName, string? ClaimerName, DateTime CreatedAtUtc, bool CanClaim, bool CanMarkDone,
  bool CanConfirm, bool WillSelfAttest)`. `CreatedByName`/`ClaimerName` resolve to `DisplayName`.
- Affordance rules (server-computed, the single source of truth): `CanClaim = Status == ToDo`;
  `CanMarkDone = Status == InProgress && ClaimedById == caller`; `CanConfirm = role == Admin && Status ==
  Done`; `WillSelfAttest = CanConfirm && ClaimedById == caller`.
- Read `sub` via `principal.FindFirstValue("sub")` (matches `HouseholdEndpoints`/`AuthEndpoints`).

#### 6. Capture DisplayName at registration

**File**: `src/Homdutio.Api/Auth/AuthEndpoints.cs`

**Intent**: Populate `DisplayName` when an account is created so cards have a name from day one.

**Contract**: Extend `RegisterRequest` with `string? DisplayName`. On register, set
`ApplicationUser.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? localPartOf(request.Email)
: request.DisplayName.Trim()`. Keep the existing `Results.Ok()` / `ValidationProblem` contract unchanged.

#### 7. Wire endpoints into the host

**File**: `src/Homdutio.Api/Program.cs`

**Intent**: Map the task group alongside the existing groups.

**Contract**: Add `app.MapTaskEndpoints();` next to `app.MapHouseholdEndpoints();` (`Program.cs:94`) —
**before** `MapFallbackToFile`.

#### 8. Integration tests

**File**: `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs`

**Intent**: Lock the lifecycle, the audit record, the guards, and the scoping introduced here.

**Contract**: Reuse `AuthApiFactory` + the register→login→create-household→bearer helper pattern. Cover:
(a) **full loop** — create → claim → mark done → confirm closes the task and `GET /api/tasks` no longer
returns it; (b) the closed task's row + its four `TaskEvent`s remain queryable (NFR-3); (c) **self-attested**
— the same admin claims, marks done, confirms → the `Confirmed` event and the projection carry
`SelfAttested = true`; (d) guards — claim a non-`ToDo` task → 409; mark done as a non-claimer → 403; mark done on a non-`InProgress`
task → 409; confirm as a non-admin member → 403; confirm a non-`Done` task → 409; (e) **foreign-household** task id →
404 (create a task in household A, fetch/transition it as a member of household B); (f) blank title → 400;
(g) unauthenticated → 401; (h) affordance flags — a `ToDo` task reports `CanClaim` for any member; a `Done`
task reports `CanConfirm` only for an admin, with `WillSelfAttest` true when the admin is the claimer.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- Migration is additive only (two new tables + `DisplayName` column; no destructive change) — confirmed by reviewing the generated `Up`
- API integration tests pass: `dotnet test`
- Migration applies cleanly to a fresh DB (exercised by `AuthApiFactory.EnsureDatabaseMigrated()` in the test run)

#### Manual Verification:

- With a bearer token: `POST /api/tasks` → claim → done → confirm drives a task to closed; `GET /api/tasks` omits it afterward
- A second `POST /api/tasks/{id}/claim` on a claimed task returns 409
- A member (non-admin) `POST /confirm` returns 403; an admin self-confirm records `SelfAttested = true`
- A task id from another household returns 404

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual
confirmation before Phase 2.

---

## Phase 2: Frontend task service, display-name & render/create

### Overview

Add the root `TaskService` (signal state mirroring `HouseholdService`, refetch-on-action, reset-on-logout)
and the `Task` model; capture a display name at register; turn `BoardComponent` from static markup into a
live board that loads tasks into the three columns and renders FR-018 cards; and add the create-task form.

### Changes Required:

#### 1. Task service + model

**File**: `web/src/app/board/task.service.ts`

**Intent**: Single source of truth for the board's tasks, mirroring `HouseholdService`. Loads tasks, runs
the four mutations, and refetches after each so the acting client re-renders (no cross-member push in S-03).

**Contract**: `@Injectable({ providedIn: 'root' })`. `Task` interface mirroring `TaskResponse`
(camelCase): `{ id; title; description?; category?; status: 'ToDo' | 'InProgress' | 'Done'; createdByName;
claimerName?; createdAtUtc; canClaim; canMarkDone; canConfirm; willSelfAttest }`. A `_tasks =
signal<Task[]>([])` plus a `current = this._tasks.asReadonly()`. Methods: `load()` (`GET /api/tasks`, sets
the signal), `create({title, description?, category?})` (`POST /api/tasks` → then `load()`), `claim(id)`,
`markDone(id)`, `confirm(id)` (each `POST` the action sub-route → then `load()`). `clearOnLogout()` resets
the signal; wire it into `AuthService.logout()` alongside `households.clearOnLogout()`.

#### 2. Capture display name at register

**File**: `web/src/app/auth/register/register.component.{ts,html}`, `web/src/app/auth/auth.service.ts`

**Intent**: Collect a display name during registration so it flows to `ApplicationUser.DisplayName`.

**Contract**: Add an optional `displayName` control to the register reactive form (no `required` — backend
defaults from the email local-part when blank). Extend `AuthService.register` to send `displayName` in the
body (`AuthRequest`/a register-specific shape). Mirror the existing field styling; keep the
register→`/login` success flow unchanged.

#### 3. Board reads live tasks

**File**: `web/src/app/board/board.component.{ts,html}`

**Intent**: Replace the static "No tasks yet" columns with live data grouped by status.

**Contract**: `BoardComponent` injects `TaskService`, calls `load()` on init, and exposes the tasks grouped
into the three columns by `status` (`ToDo` → "To do", `InProgress` → "In progress", `Done` → "Done").
Each card renders **title, creator name (`createdByName`), claimer name (if any), and creation timestamp**
(FR-018); show the empty-state
placeholder per column when it has no tasks. (Action buttons land in Phase 3.) Keep the header (household
name + role badge) from S-02.

#### 4. Create-task form

**File**: `web/src/app/board/create-task/create-task.component.{ts,html,scss}` (or an inline form on the board)

**Intent**: Let a member add a task; on submit it lands in "To do".

**Contract**: Standalone, `ReactiveFormsModule`. Controls: `title` (`Validators.required`), optional
`description`, optional `category`. Submit → guard invalid/pending → `TaskService.create(...)` → the
service's `load()` refreshes the board (the new card appears in "To do"); map a `400` via the existing
`mapValidationProblem` helper. Mobile-first styling reusing the S-01/S-02 card/page styles. Available to any
member (admin or adult member — both create per FR-010).

### Success Criteria:

#### Automated Verification:

- Frontend builds: `npm run build` (in `web/`)
- Existing + updated vitest specs pass: `npm test` (in `web/`)

#### Manual Verification:

- Registering with a display name; the value reaches the account (visible later as the card name)
- Creating a task makes it appear in "To do" showing the creator's name and a creation timestamp
- The board loads existing tasks into the correct columns on navigation
- An empty column shows its placeholder; a non-empty one shows cards

**Implementation Note**: Pause for manual confirmation before Phase 3.

---

## Phase 3: Lifecycle actions in the UI & closure

### Overview

Wire the affordance-driven action buttons (Claim / Mark done / Confirm) onto the cards, refetch after each
action, make confirmed cards leave the board, show a self-attested confirm hint, finalise the responsive
≤400px card layout (NFR-2), and add the vitest spec set.

### Changes Required:

#### 1. Action buttons driven by affordance flags

**File**: `web/src/app/board/board.component.{ts,html}`

**Intent**: Render exactly the actions the server permits per card, and run them.

**Contract**: On each card, conditionally render: **Claim** when `task.canClaim` → `TaskService.claim(id)`;
**Mark done** when `task.canMarkDone` → `markDone(id)`; **Confirm** when `task.canConfirm` →
`confirm(id)`. When `task.willSelfAttest`, the confirm control carries a short hint (e.g. "Confirm
(self-attested)") so the admin knows they're closing their own work. After any action the service's
`load()` re-renders; a confirmed task no longer returns from `GET /api/tasks` and so disappears. Handle a
`409`/`403` from a stale affordance by refetching (`load()`) so the board self-heals.

#### 2. Responsive card layout

**File**: `web/src/app/board/board.component.scss`, card styles

**Intent**: Satisfy NFR-2 — cards and their action buttons are fully usable at ≤400px with no horizontal
scroll, within the S-02 column layout (stack on mobile, side-by-side above the breakpoint).

**Contract**: Cards are full-width within their column; the action button(s) wrap/stack rather than overflow
at ≤400px; tap targets stay reachable. No fixed widths that force overflow below 400px. Reuse the S-02
breakpoint.

#### 3. Frontend tests

**File**: `web/src/app/board/task.service.spec.ts`,
`web/src/app/board/board.component.spec.ts`,
`web/src/app/board/create-task/create-task.component.spec.ts`

**Intent**: Cover the service, the affordance-driven rendering, and the create form at the right layers
(mirroring the S-01/S-02 spec sets).

**Contract**: Service — `load` sets tasks; each mutation POSTs the right route then refetches;
`clearOnLogout` resets. Board — groups tasks into the three columns; renders Claim/Mark-done/Confirm only
when the matching affordance flag is set; an action triggers the service call and a refetch; a confirmed
task drops out after reload. Create form — required-title validation; success refreshes; 400 maps. Update
`board.component.spec.ts` from its S-02 static-column assertions to the live-data behaviour.

### Success Criteria:

#### Automated Verification:

- Frontend builds: `npm run build` (in `web/`)
- All vitest specs pass: `npm test` (in `web/`)
- Release build bundles the SPA: `dotnet build -c Release` (exercises `BuildAngularSpa`)
- Full backend suite still green: `dotnet test`

#### Manual Verification:

- Single-device full loop: create → Claim → Mark done → Confirm moves the card To do → In progress → Done → gone
- Only the permitted buttons show: Claim on To-do cards; Mark done only on the card the caller claimed; Confirm only for an admin on Done cards
- An admin confirming their own claimed task sees the self-attested hint and the card closes
- At a 400px viewport the cards and action buttons fit with no horizontal scroll

**Implementation Note**: Final phase — confirm the end-to-end single-device manual walk before closing the change.

---

## Testing Strategy

### Unit / Component Tests (vitest, `web/`):

- `TaskService`: `load` sets the signal; `create`/`claim`/`markDone`/`confirm` POST the right route then refetch; `clearOnLogout` resets
- `BoardComponent`: groups tasks into the three columns; renders only the affordance-permitted buttons; an action calls the service + refetches; a confirmed task drops out
- `CreateTaskComponent`: required-title validation; success refreshes the board; 400 maps via `mapValidationProblem`
- `RegisterComponent`: the optional display-name field is sent

### Integration Tests (xUnit, `tests/Homdutio.Api.Tests`):

- Full loop create → claim → done → confirm; closed task absent from `GET /api/tasks`; row + four events persist
- Self-attested: admin claims + confirms own → `SelfAttested = true` on event + projection
- Guards: claim non-ToDo → 409; mark done as non-claimer → 403; mark done on non-InProgress → 409; member confirm → 403; confirm non-Done → 409
- Scoping: foreign-household task id → 404; unauthenticated → 401; blank title → 400
- Affordances: flags correct for ToDo (CanClaim) and Done (CanConfirm admin-only, WillSelfAttest)

### Manual Testing Steps:

1. Register two accounts with display names; create a household with the first (admin)
2. As the admin, create a task → confirm it appears in "To do" with the creator name + timestamp
3. Claim it → confirm it moves to "In progress" with the claimer name; the "Mark done" button shows only on it
4. Mark it done → confirm it moves to "Done"
5. Confirm it → confirm the card disappears from the board
6. (API) Confirm the closed task's row + events are still queryable; `GET /api/tasks` omits it
7. (API) Re-POST `/claim` on a claimed task → 409; `POST /confirm` as a non-admin → 403
8. Shrink the viewport to 400px → confirm cards + buttons fit with no horizontal scroll

## Performance Considerations

Negligible at single-household scale. The board read is one indexed query (`HouseholdId` index, filtered to
open tasks); each transition is a single-row update + one event insert in one `SaveChanges`. The append-only
`TaskEvent` table grows by ≤4 rows per task lifecycle — well within Azure SQL Basic (5 DTU). No N+1: resolve
`DisplayName`s with a join/projection in the board query.

## Migration Notes

`AddTasksAndDisplayName` is **additive** — two new tables (+ FKs + the `HouseholdId` index) and one new
`AspNetUsers.DisplayName` column with a backfill from the email local-part — so it is backward-compatible
(F-01 rule) and safe for the F-04 migrate-first deploy. No existing user is left with a blank display name.
Existing users (pre-S-03) simply have no tasks until they create one.

## References

- Roadmap slice: `context/foundation/roadmap.md` (S-03, lines 156-166)
- PRD: US-01, FR-010, FR-013–FR-016, FR-018, NFR-3; role model + Business Logic (`context/foundation/prd.md`)
- Prior slice (conventions mirrored): `context/changes/household-and-board/plan.md`
- Backend endpoint + scoping pattern: `src/Homdutio.Api/Households/HouseholdEndpoints.cs`
- Test harness: `tests/Homdutio.Api.Tests/AuthApiFactory.cs`, `HouseholdEndpointsTests.cs`
- Frontend service/signal pattern: `web/src/app/household/household.service.ts`, `web/src/app/auth/auth.service.ts`
- Board to replace: `web/src/app/board/board.component.{ts,html}`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend domain + lifecycle API

#### Automated

- [x] 1.1 Solution builds: `dotnet build` — d0b3b71
- [x] 1.2 Migration is additive only (two new tables + `DisplayName` column) — confirmed by reviewing the generated `Up` — d0b3b71
- [x] 1.3 API integration tests pass: `dotnet test` — d0b3b71
- [x] 1.4 Migration applies cleanly to a fresh DB (via test run) — d0b3b71

#### Manual

- [ ] 1.5 Full loop create → claim → done → confirm closes the task; `GET /api/tasks` omits it afterward
- [ ] 1.6 Second `/claim` on a claimed task → 409
- [ ] 1.7 Non-admin `/confirm` → 403; admin self-confirm records `SelfAttested = true`
- [ ] 1.8 Foreign-household task id → 404

### Phase 2: Frontend task service, display-name & render/create

#### Automated

- [x] 2.1 Frontend builds: `npm run build` (in `web/`) — 8e4a601
- [x] 2.2 Existing + updated vitest specs pass: `npm test` (in `web/`) — 8e4a601

#### Manual

- [ ] 2.3 Registering with a display name; the value reaches the account
- [ ] 2.4 Creating a task makes it appear in "To do" with creator name + timestamp
- [ ] 2.5 The board loads existing tasks into the correct columns on navigation
- [ ] 2.6 Empty columns show the placeholder; non-empty show cards

### Phase 3: Lifecycle actions in the UI & closure

#### Automated

- [x] 3.1 Frontend builds: `npm run build` (in `web/`) — 9a3e3a2
- [x] 3.2 All vitest specs pass: `npm test` (in `web/`) — 9a3e3a2
- [x] 3.3 Release build bundles the SPA: `dotnet build -c Release` — 9a3e3a2
- [x] 3.4 Full backend suite still green: `dotnet test` — 9a3e3a2

#### Manual

- [ ] 3.5 Single-device full loop moves the card To do → In progress → Done → gone
- [ ] 3.6 Only permitted buttons show (Claim / Mark done only for the claimer / Confirm admin-only)
- [ ] 3.7 Admin self-confirm shows the self-attested hint and closes the card
- [ ] 3.8 At 400px the cards + buttons fit with no horizontal scroll
