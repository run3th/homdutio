# Task Management and Priority (S-04) Implementation Plan

## Overview

S-04 makes the backlog *manageable*. On top of the S-03 lifecycle, a member can **edit** a task's
title / description / category and **delete** it — both only while it sits in **"To do"** — and **drag to
reorder** tasks within a column so the top of "To do" reads as the household's priority. Per the PRD,
column order (FR-021) is the *only* priority surface in v1; there is no priority field. The shared order
must be consistent for every member.

Edit and delete are deliberately constrained to "To do" (FR-011/012): a task that has been claimed has
entered the accountability lifecycle, so its content and existence are frozen — the abandon case is S-05's
unclaim, not edit/delete. This keeps the management surface small and keeps the honest record (NFR-3)
untouched: the only deletable tasks are never-claimed ones with nothing but a `Created` event.

## Current State Analysis

- **No manual order exists.** `HouseholdTask` (`src/Homdutio.Data/Entities/HouseholdTask.cs`) has no
  ordering column; `GET /api/tasks` sorts `OrderBy(Status).ThenBy(CreatedAtUtc)`
  (`src/Homdutio.Api/Tasks/TaskEndpoints.cs`). The S-03 plan explicitly deferred "no manual-order field and
  no drag-and-drop" to this slice. FR-021 requires a new persisted, shared order.
- **The endpoint pattern is set and must be reused** (`TaskEndpoints.cs`): minimal-API group `/api/tasks`,
  all `.RequireAuthorization()`; a `ResolveMemberAsync` helper derives `(householdId, role, userId)` from the
  JWT `sub` (no membership → 404); a `LoadScopedTaskAsync` helper returns a task **scoped to the caller's
  household**, foreign/missing id → not-found (no existence leak, US-02/FR-019). DTOs are `record`s;
  `Results.Ok/ValidationProblem/Created/NoContent/Conflict/Forbid` are the response vocabulary.
- **Affordance flags are server-computed.** `TaskResponse` carries `CanClaim`/`CanMarkDone`/`CanConfirm`/
  `WillSelfAttest`; the SPA renders exactly the controls the DTO permits and never compares identities. S-04
  extends this with `CanEdit`/`CanDelete` (both `= Status == ToDo`).
- **The event log is append-only** (`TaskEvent`, NFR-3) and carries only lifecycle events
  (Created/Claimed/MarkedDone/Confirmed). Edit/delete/reorder are **management** actions, not lifecycle
  transitions — by decision they append **no** events; delete is a **hard delete** (the lone `Created` event
  cascades away).
- **Single `SaveChanges` per transition** is the S-03 rule (the `EnableRetryOnFailure` execution strategy
  disallows user-initiated transactions spanning the retry boundary, `Program.cs:24-28`). Every S-04 mutation
  is likewise one atomic `SaveChanges`.
- **Frontend** (`web/src/app/board/`): `TaskService` (`providedIn: 'root'`) holds the open tasks in a signal,
  refetches after every mutation (`load()`), resets on logout. `BoardComponent` groups `tasks.current()` by
  status into three columns and renders FR-018 cards with affordance-driven action buttons; a stale 403/409
  triggers a refetch to self-heal. **No drag-drop library is installed** (`web/package.json` has no
  `@angular/cdk`). No modal/overlay primitive exists yet. No cross-member live push until S-06 (polling).
- **Test harness is reusable**: `AuthApiFactory` (throwaway LocalDB + test JWT) and the
  register→login→create-household→bearer pattern (`tests/Homdutio.Api.Tests/TaskEndpointsTests.cs`); vitest
  with `HttpTestingController` and component stubs (`web/src/app/board/*.spec.ts`).

## Desired End State

A member opens the board and sees, on each card, an affordance to open a **task-detail dialog**. For a task
in **"To do"** the dialog lets them edit title/description/category (Save persists, the board re-renders) and
**Delete** it behind an inline confirm (the card vanishes, the row is gone). For a claimed/done task the
dialog is read-only (no edit, no delete). Within any column, the member can **drag a card** to a new position;
on drop the new order persists and is the order every household member sees on their next load. The top of
"To do" is now the priority. Illegal moves are rejected server-side: editing or deleting a non-"To do" task →
409; a foreign-household task id → 404; a blank title → 400.

**Verification:** `dotnet test` + `npm test` green; a manual walk — create three tasks, drag to reorder To do,
reload and see the order hold; edit a To-do task's title via the dialog and see the card update; delete a
To-do task and see it gone with its row/event removed; confirm a claimed task's dialog offers no edit/delete;
a drag at a 400px viewport works by touch with no horizontal scroll.

### Key Discoveries:

- **`SortOrder` only needs to be correct *within* a status group.** `GET` sorts by `Status` first, so
  `SortOrder` values may repeat across columns harmlessly — reindexing one column to `0..n-1` never collides
  with another column's meaning. This is why a single household-wide integer column suffices with no
  per-status partitioning in the schema.
- **Lifecycle transitions must place the task in its new column's order.** A task carries its `SortOrder`
  across a claim/done transition; to avoid it landing at an arbitrary position among its new neighbours,
  claim/done set `SortOrder` to the **bottom of the destination column** (`max(dest-status SortOrder)+1`).
  This is a small additive change to the existing S-03 transition endpoints.
- **Reorder is within-column only.** Moving between columns is a *lifecycle* action (claim/done/confirm), not
  a drag. So the CDK drop lists are **independent** (not connected) — dragging a card out of its column is
  not a feature, which also sidesteps conflating drag with a status change.
- **`@angular/cdk` covers both needs.** The same dependency provides `@angular/cdk/drag-drop` (touch-capable
  reorder for NFR-2) *and* `@angular/cdk/dialog` (focus-trap/backdrop/a11y modal) — one install serves the
  reorder and the task-detail panel.
- **Hard delete is safe here.** NFR-3 protects *closed*-task audit; a deletable task is "To do" (never
  claimed), so it has no honest record to preserve. The `Created` event cascades away with the row.

## What We're NOT Doing

- **No edit or delete outside "To do"** — FR-011/012 are explicit; claimed/done tasks are frozen. The abandon
  path for an in-progress task is S-05 (unclaim), not edit/delete.
- **No cross-column drag** — moving columns is the lifecycle (claim/done/confirm), never a drag. Drop lists
  are not connected.
- **No priority field / no urgency algorithm** — Non-Goal. Column order (FR-021) is the only priority surface.
- **No assignment** — assigning a task to a person is *not* v1 (the model is self-claim, FR-013). The
  task-detail dialog is **designed to accept** an assignee field later, but S-04 ships no assignment.
- **No delete/edit/reorder audit events** — management actions don't append `TaskEvent`s; the event log stays
  the lifecycle record. No soft-delete, no `DeletedAtUtc`.
- **No cross-member live reorder** — reorder persists on drop + refetch (last-write-wins); a second member
  sees a new order only on their next load. Live propagation is S-06 (polling).
- **No optimistic/undo machinery** — no toast/undo primitive; delete is guarded by an inline confirm instead.
- **No reorder concurrency guard** — no version columns or conflict UI; LWW is accepted pre-S-06.

## Implementation Approach

Three phases, backend-first so the SPA builds against a real contract (mirrors S-02/S-03):

1. **Backend** — add `SortOrder` (+ additive migration with backfill), maintain it on create and on
   claim/done, sort the board by it, and add the three guarded management endpoints (edit, delete, reorder)
   plus the `CanEdit`/`CanDelete` affordance flags; integration tests lock the guards, scoping, ordering, and
   the reindex.
2. **Frontend edit/delete** — add `@angular/cdk`; extend `TaskService` + the `Task` model; build a reusable
   CDK Dialog task-detail/edit panel (extensible toward assignment) with an inline-confirm delete; open it
   from a card affordance; vitest specs.
3. **Frontend reorder** — `cdkDropList` per column + drag handles, a drop handler that persists the new order
   and refetches, touch-usable at ≤400px; vitest specs.

## Critical Implementation Details

- **`SortOrder` semantics.** A household-wide `int` ordering tasks *within their current status column*
  (`GET` partitions by `Status` first). **Create** → bottom of "To do". **claim/done** → bottom of the
  destination column. **reorder** → the server reassigns contiguous `0..n-1` to the column's tasks from the
  client-supplied ordered id list. **confirm** closes the task (off the board); `SortOrder` becomes moot.
- **Reorder endpoint validates membership + status cohesion.** `PUT /api/tasks/order` accepts a target
  `status` + an ordered list of task ids; it must verify every id belongs to the caller's household **and** is
  currently in that `status` before reindexing — a foreign or mismatched id is rejected (no partial reindex,
  no existence leak). One atomic `SaveChanges`.
- **Drag vs. open-dialog must not conflict.** The card's open-detail affordance is an explicit control (e.g.
  a button/the title), distinct from the CDK drag handle, so a drag never accidentally opens the dialog and a
  tap-to-open never starts a drag.

## Phase 1: Backend — ordering model + edit/delete/reorder endpoints

### Overview

Add `SortOrder` to `HouseholdTask`, generate one additive migration that backfills existing open tasks per
column by `CreatedAtUtc`, maintain `SortOrder` on create and on the claim/done transitions, sort the board by
it, and expose the three guarded management endpoints with `CanEdit`/`CanDelete` flags. Lock everything with
integration tests.

### Changes Required:

#### 1. SortOrder on HouseholdTask

**File**: `src/Homdutio.Data/Entities/HouseholdTask.cs`

**Intent**: Give each task a persisted position so the shared, manual column order (FR-021) survives reloads
and is identical for every member.

**Contract**: Add `int SortOrder { get; set; }`. Orders tasks *within* their current status column (the board
query partitions by `Status` first, so values may repeat across columns). No navigation change.

#### 2. DbContext + migration

**File**: `src/Homdutio.Data/ApplicationDbContext.cs`,
`src/Homdutio.Data/Migrations/<timestamp>_AddTaskSortOrder.cs` (generated)

**Intent**: Persist `SortOrder` additively and seed existing open tasks so current boards keep a sensible
order on first deploy (F-01 additive rule; F-04 migrate-first).

**Contract**: No special EF configuration needed beyond the column (a plain `int`, default 0). `dotnet ef
migrations add AddTaskSortOrder` (project `Homdutio.Data`, startup `Homdutio.Api`). Review the generated `Up`:
it must only **add** the `SortOrder` column. Append a backfill `migrationBuilder.Sql` that sets `SortOrder`
per `(HouseholdId, Status)` partition ordered by `CreatedAtUtc` (window function `ROW_NUMBER() OVER (PARTITION
BY HouseholdId, Status ORDER BY CreatedAtUtc)` minus 1), so pre-S-04 tasks render in their existing order. No
existing column is dropped or retyped.

#### 3. Maintain SortOrder on create and lifecycle transitions

**File**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs`

**Intent**: Keep a task sensibly positioned as it enters a column — appended to the bottom — so a manual order
is never scrambled by creation or by a claim/done transition.

**Contract**: Add a private helper `NextSortOrderAsync(db, householdId, status)` → `(max SortOrder among the
household's tasks in that status) + 1`, or 0 when none. On **create**: set `SortOrder = NextSortOrder(ToDo)`.
On **claim** (→ InProgress) and **done** (→ Done): set `SortOrder = NextSortOrder(destination status)` as part
of the same single `SaveChanges`. `confirm` is unchanged (the task closes). Each helper call and the mutation
remain within one atomic `SaveChanges` per request.

#### 4. Board query orders by SortOrder

**File**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs`

**Intent**: Render the manual order.

**Contract**: Change `GET /api/tasks` ordering from `OrderBy(Status).ThenBy(CreatedAtUtc)` to
`OrderBy(Status).ThenBy(SortOrder).ThenBy(CreatedAtUtc)` (the timestamp is now only a stable tiebreaker).

#### 5. Edit endpoint

**File**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs`

**Intent**: Let a member fix a task's title/description/category while it is still un-claimed (FR-011).

**Contract**: `PUT /api/tasks/{id:guid}` body `UpdateTaskRequest(string Title, string? Description, string?
Category)`. Resolve caller (no membership → 404); load scoped task (foreign/missing → 404); guard
`Status == ToDo` (else `409`); blank title → `400 ValidationProblem`. Update the three fields (trim;
empty description/category → null), one `SaveChanges`, **no event**. Return `200` `TaskResponse`.

#### 6. Delete endpoint

**File**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs`

**Intent**: Let a member remove a mistaken/obsolete task while it is still un-claimed (FR-012).

**Contract**: `DELETE /api/tasks/{id:guid}`. Resolve caller (→ 404); load scoped task (foreign/missing →
404); guard `Status == ToDo` (else `409`). **Hard delete** the row (its lone `Created` `TaskEvent` cascades
away via the existing FK), one `SaveChanges`, no event. Return `204 NoContent`.

#### 7. Reorder endpoint

**File**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs`

**Intent**: Persist a new within-column order from a drag, shared across the household (FR-021).

**Contract**: `PUT /api/tasks/order` body `ReorderRequest(string Status, Guid[] OrderedIds)`. Resolve caller
(→ 404). Parse `Status` to `HouseholdTaskStatus` (invalid → `400`). Load the caller's household tasks whose id
∈ `OrderedIds`; **validate** that every supplied id resolves to one of the caller's tasks **and** that its
current status equals the requested `Status` — any mismatch (foreign id, wrong-status id, unknown id) →
`404`/`400` with no partial write. Reassign `SortOrder = index` across `OrderedIds`, one `SaveChanges`.
Return `204 NoContent` (the client refetches). No event.

#### 8. CanEdit / CanDelete affordance flags

**File**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs`

**Intent**: Keep the SPA authorization-dumb — the server says whether edit/delete are allowed.

**Contract**: Extend `TaskResponse` with `bool CanEdit, bool CanDelete` (append to the record). Compute both
as `Status == HouseholdTaskStatus.ToDo` in `ToResponse`. (Reorder needs no per-task flag — any member may
reorder any column.)

#### 9. Integration tests

**File**: `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs`

**Intent**: Lock the ordering behaviour, the three endpoints, their guards, and scoping.

**Contract**: Reuse `AuthApiFactory` + the existing helpers. Cover: (a) **create appends** — three tasks land
in creation order at the bottom of "To do"; (b) **reorder** — `PUT /api/tasks/order` with a permuted id list
reorders the column, and a subsequent `GET` returns that order; (c) reorder **rejects** a foreign-household id
and a wrong-status id (→ 404/400, order unchanged); (d) **edit** — success updates fields; blank title → 400;
non-"To do" task → 409; foreign id → 404; (e) **delete** — success returns 204, the row **and** its `Created`
event are gone; non-"To do" → 409; foreign id → 404; (f) **transition placement** — claiming a task appends it
to the bottom of "In progress" (its `SortOrder` ≥ existing InProgress tasks); (g) **affordances** — a "To do"
task reports `CanEdit`/`CanDelete` true; a claimed task reports both false; (h) unauthenticated edit/delete/
reorder → 401.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- Migration is additive only (one `SortOrder` column + backfill; no destructive change) — confirmed by reviewing the generated `Up`
- API integration tests pass: `dotnet test`
- Migration applies cleanly to a fresh DB (via the test run)

#### Manual Verification:

- `PUT /api/tasks/order` with a reordered id list changes the order returned by `GET /api/tasks`
- Editing a "To do" task via `PUT /api/tasks/{id}` updates it; editing a claimed task → 409
- Deleting a "To do" task → 204 and it no longer appears; deleting a claimed task → 409
- A reorder request containing another household's task id → 404 (no partial reindex)

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual
confirmation before Phase 2.

---

## Phase 2: Frontend — edit & delete via task-detail dialog

### Overview

Add `@angular/cdk`; extend `TaskService` and the `Task` model with update/delete + `canEdit`/`canDelete`;
build a reusable CDK Dialog task-detail/edit panel (designed to grow toward an assignee field) with an inline
two-step delete; open it from an explicit card affordance. Cover with vitest specs.

### Changes Required:

#### 1. Add the Angular CDK dependency

**File**: `web/package.json`

**Intent**: Bring in the toolkit that provides both the dialog (this phase) and drag-drop (Phase 3).

**Contract**: Add `@angular/cdk` (matching the Angular 21 major). `npm install` so the lockfile updates.
No other config — CDK modules are imported per-component.

#### 2. TaskService: update, delete, and model flags

**File**: `web/src/app/board/task.service.ts`

**Intent**: Add the two new mutations (refetch-on-success, mirroring the existing methods) and expose the new
affordance flags to the board.

**Contract**: Extend `Task` with `canEdit: boolean; canDelete: boolean`. Add `update(id, { title,
description?, category? })` → `PUT /api/tasks/{id}` then `load()`; `delete(id)` → `DELETE /api/tasks/{id}`
then `load()`. Both return `Observable<Task[]>` like the existing mutations.

#### 3. Task-detail / edit dialog component

**File**: `web/src/app/board/task-detail/task-detail.component.{ts,html,scss}`

**Intent**: A single reusable per-task panel — read-only for claimed/done tasks, an edit form (+ delete) for
"To do" tasks — that future slices extend (assignee, S-05 send-back comment) without reworking the card.

**Contract**: Standalone component opened via `@angular/cdk/dialog` `Dialog.open(...)`, receiving the `Task`
via `DIALOG_DATA`. Reactive form (`title` required, optional `description`/`category`) **enabled only when
`task.canEdit`**; Save → `TaskService.update(...)` → close on success; 400 mapped via `mapValidationProblem`.
When `task.canDelete`, a **Delete** control with an inline two-step confirm → `TaskService.delete(id)` → close
on success. Read-only mode (no `canEdit`/`canDelete`) shows the fields as static text. Mobile-first at ≤400px;
closes on backdrop/escape (CDK defaults). Designed so an assignee field can be added later without structural
change.

#### 4. Card affordance to open the dialog

**File**: `web/src/app/board/board.component.{ts,html}`

**Intent**: Give every card an explicit way to open its detail panel, distinct from any drag handle (Phase 3).

**Contract**: Inject `Dialog`. Add an explicit open control on the card (e.g. the title as a button, or a
"Details" affordance) → `openDetail(task)` opens `TaskDetailComponent` with the task as data. After the dialog
closes, the service's `load()` has already refreshed state (mutations refetch), so no extra wiring is needed
beyond opening. Keep the existing lifecycle action buttons.

#### 5. Frontend tests

**File**: `web/src/app/board/task.service.spec.ts`,
`web/src/app/board/task-detail/task-detail.component.spec.ts`,
`web/src/app/board/board.component.spec.ts`

**Intent**: Cover the new mutations, the dialog's edit/delete behaviour and read-only gating, and the open
affordance.

**Contract**: Service — `update`/`delete` PUT/DELETE the right routes then refetch (extend the existing spec).
Dialog — renders an editable form when `canEdit`, static text when not; Save calls `update` and closes; the
delete confirm calls `delete` and closes; a 400 maps. Board — the open affordance opens the dialog with the
task (spy on `Dialog.open`); existing board specs stay green (the `Task` stub gains `canEdit`/`canDelete`).

### Success Criteria:

#### Automated Verification:

- Frontend builds: `npm run build` (in `web/`)
- Existing + new vitest specs pass: `npm test` (in `web/`)

#### Manual Verification:

- Opening a "To do" card's dialog shows an editable form; Save updates the card on the board
- Editing the title to blank shows the mapped validation error and does not close
- The dialog's Delete (after confirm) removes the card; a claimed/done card's dialog is read-only (no edit/delete)
- The dialog is usable at a 400px viewport (no horizontal scroll, controls reachable)

**Implementation Note**: Pause for manual confirmation before Phase 3.

---

## Phase 3: Frontend — drag-and-drop reorder

### Overview

Make each column a CDK drop list with draggable cards; on drop, persist the new within-column order and
refetch. Ensure the interaction is touch-usable at ≤400px (NFR-2). Cover with vitest specs.

### Changes Required:

#### 1. TaskService: reorder

**File**: `web/src/app/board/task.service.ts`

**Intent**: Persist a column's new order and re-render from the server (last-write-wins).

**Contract**: Add `reorder(status: TaskStatus, orderedIds: string[])` → `PUT /api/tasks/order` body `{ status,
orderedIds }` then `load()`. Returns `Observable<Task[]>`.

#### 2. Drop lists + drag handles on the board

**File**: `web/src/app/board/board.component.{ts,html,scss}`

**Intent**: Let a member drag a card to a new slot within its column and persist the result.

**Contract**: Import `@angular/cdk/drag-drop` (`DragDropModule`). Wrap each column's card list in a
`cdkDropList` (one independent list per column — **not** connected, so cross-column drag is impossible);
make each card `cdkDrag`. On `(cdkDropListDropped)`, reorder the column's task array locally
(`moveItemInArray`) to compute the new `orderedIds`, then call `TaskService.reorder(status, orderedIds)`
(which refetches). A failed persist falls back to a `load()` self-heal (mirrors the S-03 stale-affordance
pattern). The open-detail affordance from Phase 2 stays distinct from the drag interaction.

#### 3. Responsive / touch reorder

**File**: `web/src/app/board/board.component.scss`

**Intent**: Satisfy NFR-2 — drag works by touch at ≤400px with no horizontal scroll and a clear drag preview.

**Contract**: Style the CDK drag states (`.cdk-drag-preview`, `.cdk-drag-placeholder`, `.cdk-drag-animating`)
for the card; ensure touch targets and the drag handle are reachable; preserve the S-02/S-03 column layout
(stack on mobile, side-by-side above the breakpoint). No fixed widths that force overflow below 400px.

#### 4. Frontend tests

**File**: `web/src/app/board/task.service.spec.ts`,
`web/src/app/board/board.component.spec.ts`

**Intent**: Cover the reorder mutation and the drop handler wiring.

**Contract**: Service — `reorder` PUTs `/api/tasks/order` with `{ status, orderedIds }` then refetches.
Board — a simulated `cdkDropListDropped` (or a direct call to the drop handler with a mock `CdkDragDrop`
event) computes the reordered ids and calls `TaskService.reorder` with the right `status` + order; a
refetch follows.

### Success Criteria:

#### Automated Verification:

- Frontend builds: `npm run build` (in `web/`)
- All vitest specs pass: `npm test` (in `web/`)
- Release build bundles the SPA: `dotnet build -c Release` (exercises `BuildAngularSpa`)
- Full backend suite still green: `dotnet test`

#### Manual Verification:

- Dragging a card within "To do" reorders it; reloading the board preserves the new order
- The same drag works in "In progress" and "Done"; a card cannot be dragged between columns
- At a 400px viewport the drag works by touch with no horizontal scroll and a visible drag preview
- A reorder by one member is visible to another member after that member reloads (no live push expected)

**Implementation Note**: Final phase — confirm the end-to-end single-device manual walk before closing the change.

---

## Testing Strategy

### Unit / Component Tests (vitest, `web/`):

- `TaskService`: `update`/`delete` PUT/DELETE the right routes then refetch; `reorder` PUTs `/api/tasks/order` with `{ status, orderedIds }` then refetches
- `TaskDetailComponent`: editable form when `canEdit`, static text otherwise; Save calls `update` + closes; delete-confirm calls `delete` + closes; 400 maps
- `BoardComponent`: the open affordance opens the dialog with the task; a drop event calls `reorder` with the right status + ordered ids; existing assertions stay green with the enriched `Task` stub

### Integration Tests (xUnit, `tests/Homdutio.Api.Tests`):

- Create appends to the bottom of "To do"; claim/done append to the bottom of the destination column
- `PUT /api/tasks/order` reorders a column (reflected by `GET`); rejects foreign / wrong-status ids with no partial write
- Edit: success; blank title → 400; non-"To do" → 409; foreign id → 404
- Delete: 204 + row and `Created` event gone; non-"To do" → 409; foreign id → 404
- Affordances: `CanEdit`/`CanDelete` true for "To do", false once claimed; unauthenticated → 401

### Manual Testing Steps:

1. Create three tasks; confirm they appear at the bottom of "To do" in creation order
2. Drag the third to the top of "To do"; reload — the order holds
3. Open a "To do" card's dialog, edit its title, Save — the card updates
4. In the dialog, Delete (confirm) — the card disappears
5. Claim a task; open its dialog — it is read-only (no edit/delete); the claimed card sits at the bottom of "In progress"
6. (API) `PUT /api/tasks/order` with another household's task id in the list → 404, order unchanged
7. Shrink to a 400px viewport; drag-reorder by touch — no horizontal scroll, visible drag preview

## Performance Considerations

Negligible at single-household scale. The board read is one indexed query (`HouseholdId` index) plus an
`ORDER BY`. A reorder rewrites at most one column's rows (a handful) in one `SaveChanges`; the integer
reindex is trivially cheap and avoids the precision-rebalancing a fractional scheme would eventually need.
Edit/delete are single-row operations.

## Migration Notes

`AddTaskSortOrder` is **additive** — one `SortOrder` column with a `ROW_NUMBER()` backfill partitioned by
`(HouseholdId, Status)` ordered by `CreatedAtUtc` — so existing boards keep their current order and the
migration is backward-compatible (F-01) and safe for the F-04 migrate-first deploy. No existing column is
dropped or retyped.

## References

- Roadmap slice: `context/foundation/roadmap.md` (S-04, lines 168-178)
- PRD: FR-011 (edit), FR-012 (delete), FR-021 (reorder = priority); role table + Non-Goals (`context/foundation/prd.md`)
- Prior slice (conventions + the loop this builds on): `context/changes/accountability-loop/plan.md`
- Backend endpoint + scoping + affordance pattern: `src/Homdutio.Api/Tasks/TaskEndpoints.cs`
- Entity + DbContext: `src/Homdutio.Data/Entities/HouseholdTask.cs`, `src/Homdutio.Data/ApplicationDbContext.cs`
- Frontend service/board: `web/src/app/board/task.service.ts`, `web/src/app/board/board.component.{ts,html,scss}`
- Test harness: `tests/Homdutio.Api.Tests/AuthApiFactory.cs`, `TaskEndpointsTests.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend — ordering model + edit/delete/reorder endpoints

#### Automated

- [x] 1.1 Solution builds: `dotnet build` — 9349a6f
- [x] 1.2 Migration is additive only (one `SortOrder` column + backfill) — confirmed by reviewing the generated `Up` — 9349a6f
- [x] 1.3 API integration tests pass: `dotnet test` — 9349a6f
- [x] 1.4 Migration applies cleanly to a fresh DB (via test run) — 9349a6f

#### Manual

- [x] 1.5 `PUT /api/tasks/order` changes the order returned by `GET /api/tasks` — 9349a6f
- [x] 1.6 Edit a "To do" task succeeds; editing a claimed task → 409 — 9349a6f
- [x] 1.7 Delete a "To do" task → 204 and it's gone; deleting a claimed task → 409 — 9349a6f
- [x] 1.8 A reorder request with another household's task id → 404 (no partial reindex) — 9349a6f

### Phase 2: Frontend — edit & delete via task-detail dialog

#### Automated

- [x] 2.1 Frontend builds: `npm run build` (in `web/`) — 34ac8b6
- [x] 2.2 Existing + new vitest specs pass: `npm test` (in `web/`) — 34ac8b6

#### Manual

- [x] 2.3 A "To do" card's dialog edits and Save updates the card — 34ac8b6
- [x] 2.4 Blank title shows the mapped validation error and does not close — 34ac8b6
- [x] 2.5 Dialog Delete (after confirm) removes the card; a claimed/done card's dialog is read-only — 34ac8b6
- [x] 2.6 The dialog is usable at a 400px viewport — 34ac8b6

### Phase 3: Frontend — drag-and-drop reorder

#### Automated

- [x] 3.1 Frontend builds: `npm run build` (in `web/`)
- [x] 3.2 All vitest specs pass: `npm test` (in `web/`)
- [x] 3.3 Release build bundles the SPA: `dotnet build -c Release`
- [x] 3.4 Full backend suite still green: `dotnet test`

#### Manual

- [x] 3.5 Dragging a card within "To do" reorders it; reload preserves the order
- [x] 3.6 Drag works in all three columns; a card cannot be dragged between columns
- [x] 3.7 At a 400px viewport the drag works by touch with no horizontal scroll
- [x] 3.8 A reorder by one member is visible to another after they reload
