# Loop Recovery + Task Comments Implementation Plan

## Overview

Deliver roadmap **S-05** — the two loop-recovery transitions that close the failure modes the
S-03 lifecycle creates — and, by explicit product decision (2026-06-11), a **full free-form
task-comments feature** layered on the same surfaces:

- **Unclaim (FR-022, extended):** the claimer **or an admin** returns an *In progress* task to
  *To do*, unassigned — closing the "stuck in progress" / absent-claimer deadlock.
- **Send-back (FR-023):** an admin returns a *Done* task to *In progress* with a required short
  comment, keeping the original claimer attached — closing the "sloppy work" dispute.
- **Free-form comments (scope expansion):** any household member can post immutable comments on a
  task at any time; a 💬 count badge sits on the card and the full author + timestamp thread shows
  in the detail dialog. The send-back reason is just a comment posted atomically with the transition.
- **Admin-anytime editing (scope expansion):** task-field editing becomes admin-only in any
  column; non-admin members see read-only fields (but can still comment).

## Current State Analysis

The codebase is unusually well-prepared for S-05 — the slice was anticipated when S-03/S-11 shipped:

- **`src/Homdutio.Api/Tasks/TaskEndpoints.cs`** is the template: every endpoint resolves the
  caller's household server-side from the JWT `sub` (`ResolveMemberAsync`, line 324), guards
  current state + actor eligibility, appends one `TaskEvent` in the **same** `SaveChanges` as the
  projection mutation (so the audit log never diverges), and returns a `TaskResponse` carrying
  server-computed affordance flags (`ToResponse`, line 373). `NextSortOrderAsync` (line 310) drops
  a transitioning task at the bottom of its destination column.
- **`src/Homdutio.Data/Entities/TaskEventType.cs:6`** already documents the plan:
  *"S-05 will append `Unclaimed` / `SentBack` without restructuring the log."* `Type` is stored as
  a string (`ApplicationDbContext.cs:91`), so new enum members need **no migration**.
- **`src/Homdutio.Data/Entities/TaskEvent.cs:25`** shows the precedent for event-specific metadata
  (`SelfAttested`, "meaningful only on Confirmed").
- **`web/src/app/board/task-card/task-card.component.html:42`** already renders **disabled
  "Unclaim" / "Send back" menu slots** ("Coming soon (S-05)"). The kebab menu, single-open +
  outside-click/Escape handling, and the card→column→board event re-emit chain all exist.
- **`web/src/app/board/task-detail/task-detail.component.ts:21`** was *"deliberately structured so
  a future slice can add … S-05's send-back comment,"* and already drives editable vs read-only
  entirely from the server's `canEdit` flag.
- **`tests/Homdutio.Api.Tests/TaskEndpointsTests.cs`** is a comprehensive integration-test template
  (register→login→create-household→bearer helper, `SeedMemberAsync` for non-admin paths, guard +
  affordance-flag + foreign-household-404 assertions) to mirror.
- **Polling (F-03):** the board pauses polling during drags and while the dialog is open
  (`board.component.ts` `setPaused`); any new dialog (send-back) must do the same.

## Desired End State

On the live board:

- A claimer (or any admin) clicks **Unclaim** in a card's ⋯ menu and the in-progress task returns
  to To do, unassigned, instantly.
- An admin clicks **Send back** on a Done card, types a required reason, and the task returns to In
  progress with the original claimer still attached; the reason appears in the task's comment thread.
- Any member opens a task and sees a thread of comments (each with author + time), can post a new
  one, and sees a 💬 count on the card. Members see task fields read-only; admins can edit fields in
  any column.
- Every transition and the send-back reason persist durably (NFR-3): `Unclaimed` / `SentBack`
  events join the append-only log; comments are immutable rows.
- The PRD and roadmap record the Non-Goal override and FR-011 change; S-05 is marked done.

Verified by: the new xUnit integration tests + vitest specs pass, `dotnet build` + `ng build`
(Release) are green, and a manual walk of unclaim / send-back / comment on a two-member household.

### Key Discoveries:

- The send-back reason and the comments feature share **one** model — a send-back creates a
  `TaskComment` of `Kind = SendBack`; no `Comment` column on `TaskEvent` is needed.
- New `TaskEventType` values require **no migration** (string-converted `Type` column).
- The only schema change is the additive `TaskComments` table → one migration.
- `canEdit` semantics change (admin-anytime) ripples into existing edit tests
  (`Editing_a_claimed_task_returns_409` must be re-expressed) — see Phase 2.

## What We're NOT Doing

- **No comment edit/delete** — comments are append-only/immutable (matches the NFR-3 audit grain).
  No edit/delete endpoints, no ownership-mutation UI.
- **No new delete-permission model** — `DELETE /api/tasks/{id}` stays exactly as FR-012 (To-do-only,
  any member). Only *editing* fields moves to admin-anytime. (Called out so the boundary is deliberate,
  not an oversight; revisit later if the edit/delete split feels inconsistent.)
- **No notifications** — a sent-back claimer learns of it from the board/thread, not a push/email
  (PRD Non-Goal stands).
- **No comment threading/replies, mentions, attachments, or reactions** — a flat, text-only,
  ≤280-char immutable list.
- **No cross-household exposure** — every new endpoint reuses the household-scoping + foreign-id-404
  pattern (US-02/FR-019).
- **No events-history UI beyond comments** — the `TaskEvent` log stays server-side; only comments
  (incl. the send-back reason) surface to users.

## Implementation Approach

Build backend-first so the contract is locked before the SPA consumes it, and land the shared
comments store (Phase 1) before the send-back transition that reuses it (Phase 2). Each phase is
independently verifiable. Frontend splits along the same seam: loop-recovery actions (Phase 3) then
the comments thread + badge + admin-edit (Phase 4). Phase 5 reconciles the foundation docs.

## Critical Implementation Details

- **Send-back atomicity:** the `SentBack` `TaskEvent`, the `SendBack`-kind `TaskComment`, the
  status flip (Done → In progress), the `DoneAtUtc` clear, and the `SortOrder` move must all land in
  one `SaveChanges` — mirroring how confirm writes its event + projection together, so the thread can
  never show a reason for a transition that didn't persist.
- **Unclaim clears the claimer; send-back keeps it.** Unclaim nulls `ClaimedById`/`ClaimedAtUtc`
  (task becomes unassigned); send-back leaves `ClaimedById` intact (FR-023 "original claimer
  remains attached") and only clears `DoneAtUtc`. The `Claimed` event stays in the log either way.
- **Board-payload hygiene:** the board DTO carries only a `commentCount` (a cheap per-task COUNT);
  full comment bodies load lazily via `GET /api/tasks/{id}/comments` when the dialog opens, so the
  4 s poll never drags large comment payloads.
- **Polling pause:** the send-back dialog must set `tasks.setPaused(true)` on open / `false` on
  close (like the detail + delete dialogs), so a tick can't refetch mid-comment-entry.

---

## Phase 1: Comments backend foundation

### Overview

Introduce the `TaskComment` entity, its table, and the post/list endpoints — the shared store the
send-back reason (Phase 2) reuses. Expose a `commentCount` on the board DTO for the card badge.

### Changes Required:

#### 1. TaskComment entity + kind enum

**File**: `src/Homdutio.Data/Entities/TaskComment.cs` (new), `src/Homdutio.Data/Entities/TaskCommentKind.cs` (new)

**Intent**: A flat, immutable comment on a task — the discussion thread plus the home of the
send-back reason. `Kind` distinguishes a free-form member comment from a lifecycle send-back reason
so the UI can label/lock the latter.

**Contract**: `TaskComment { Guid Id; Guid TaskId; HouseholdTask? Task; string AuthorId; string Body;
TaskCommentKind Kind; DateTime CreatedAtUtc }`. `TaskCommentKind { Member, SendBack }`. `AuthorId`
is a raw `AspNetUsers.Id` with **no** navigation (follows the existing `CreatedById`/`ActorId`
convention in `HouseholdTask`/`TaskEvent` to avoid multiple cascade paths through `AspNetUsers`).

#### 2. DbContext mapping + migration

**File**: `src/Homdutio.Data/ApplicationDbContext.cs`, `src/Homdutio.Data/Migrations/` (new migration)

**Intent**: Register `DbSet<TaskComment>` and configure it; generate the additive migration.

**Contract**: `Body` required, `HasMaxLength(280)`; `Kind` `HasConversion<string>().HasMaxLength(20)`
(legible rows, growable enum — matching `Status`/`Type`); FK `Task` → `HouseholdTask` with
`OnDelete(DeleteBehavior.Cascade)`; index on `TaskId` (backs the thread + count queries). New
migration name: `AddTaskComments`. Must be backward-compatible (additive table only).

#### 3. Comment endpoints + board count

**File**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs` (+ a `CommentResponse` / `CreateCommentRequest` record)

**Intent**: Let any household member post a comment and load a task's thread, and surface a per-task
comment count on the board so the card can show its badge.

**Contract**:
- `POST /api/tasks/{id:guid}/comments` — body `{ body }`; household-scoped (`LoadScopedTaskAsync`,
  foreign/missing → 404); blank or >280 body → 400 `ValidationProblem`; creates a `Member`-kind
  comment; returns 201 with the created `CommentResponse { id, body, kind, authorName, createdAtUtc }`.
- `GET /api/tasks/{id:guid}/comments` — household-scoped (404 pattern); returns the task's comments
  ordered by `CreatedAtUtc` as `CommentResponse[]`, author display-names resolved in one query
  (extend/parallel `ResolveNamesAsync`).
- `GET /api/tasks` board DTO gains `int CommentCount` — a grouped COUNT of comments per returned
  task (single query keyed by `TaskId`, no N+1), added to `TaskResponse` + `ToResponse`.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- Migration applies cleanly to a fresh DB (the test fixture's `EnsureDatabaseMigrated`): `dotnet test`
- New xUnit tests pass: post-comment returns 201 + persists; list returns comments in order with
  author names; blank/oversized body → 400; foreign-household task id → 404; unauthenticated → 401;
  board DTO reports the correct `commentCount`.

#### Manual Verification:

- A posted comment persists across an app restart (durable row).
- `GET /api/tasks` payload stays lean (no full comment bodies, only the count).

**Implementation Note**: After this phase and all automated verification passes, pause for manual
confirmation before Phase 2.

---

## Phase 2: Loop-recovery transitions backend

### Overview

Add the `Unclaim` and `Send back` transitions and switch field-editing to admin-anytime, with new
affordance flags — all on `TaskEndpoints.cs`, reusing the Phase 1 comment store for the send-back reason.

### Changes Required:

#### 1. New lifecycle event types

**File**: `src/Homdutio.Data/Entities/TaskEventType.cs`

**Intent**: Record unclaim and send-back in the append-only audit log (NFR-3).

**Contract**: Append `Unclaimed`, `SentBack` to the enum. No migration (string-converted column).
Update the doc-comment to reflect that these now exist.

#### 2. Unclaim endpoint

**File**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs`

**Intent**: Return an in-progress task to To do, unassigned — usable by the claimer or any admin.

**Contract**: `POST /api/tasks/{id:guid}/unclaim` — household-scoped (foreign/missing → 404); not
*In progress* → 409; caller neither the claimer nor an admin → 403. On success: `Status` → `ToDo`,
null `ClaimedById` + `ClaimedAtUtc`, `SortOrder` = `NextSortOrderAsync(ToDo)`, append an `Unclaimed`
`TaskEvent` (actor = caller) in the same `SaveChanges`; return the updated `TaskResponse`.

#### 3. Send-back endpoint

**File**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs` (+ a `SendBackRequest` record)

**Intent**: Admin returns a Done task to In progress with a required reason, preserving the claimer;
the reason enters the comment thread.

**Contract**: `POST /api/tasks/{id:guid}/sendback` — body `{ comment }`; household-scoped (404);
caller not admin → 403; not *Done* → 409; blank or >280 comment → 400. On success, in one
`SaveChanges`: `Status` → `InProgress`, clear `DoneAtUtc`, **keep** `ClaimedById`, `SortOrder` =
`NextSortOrderAsync(InProgress)`, append a `SentBack` `TaskEvent` **and** a `TaskComment` with
`Kind = SendBack` (author = caller); return the updated `TaskResponse`.

#### 4. Admin-anytime editing

**File**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs`

**Intent**: Editing task fields becomes admin-only and is no longer restricted to To-do; members
can no longer edit fields (they comment instead).

**Contract**: In `PUT /api/tasks/{id}` replace the `Status != ToDo → 409` guard with
`caller.Role != Admin → 403`; admins may edit in any column (still 404 on foreign id, 400 on blank
title). `DELETE` is unchanged (To-do-only, any member).

#### 5. Affordance flags

**File**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs`

**Intent**: Tell the SPA which recovery/edit actions the caller may take.

**Contract**: `TaskResponse`/`ToResponse` gain `CanUnclaim` (= `Status == InProgress &&
(ClaimedById == caller || caller is Admin)`) and `CanSendBack` (= `caller is Admin && Status ==
Done`). Redefine `CanEdit` to `caller.Role == Admin` (any status). `CanDelete` unchanged.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- New + updated xUnit tests pass: `dotnet test` — covering:
  - unclaim by claimer and by a (non-claimer) admin both succeed → task back in To do, unassigned,
    `Unclaimed` event appended;
  - unclaim by a non-claimer non-admin member → 403; unclaim of a non-in-progress task → 409;
  - send-back by admin → task In progress, claimer preserved, `DoneAtUtc` cleared, `SentBack` event
    + `SendBack` comment appended; send-back by non-admin → 403; non-Done → 409; blank/oversized
    comment → 400;
  - admin edits a claimed/Done task → 200 (replacing `Editing_a_claimed_task_returns_409`); non-admin
    edit → 403;
  - affordance flags: `CanUnclaim`/`CanSendBack`/`CanEdit` reported correctly per role+status;
  - foreign-household id → 404 and unauthenticated → 401 on both new routes.

#### Manual Verification:

- Audit trail after a full claim → done → send-back → done → confirm cycle reads coherently in the DB
  (`TaskEvent`s in order; one `SendBack` comment).
- No regression in the existing S-03/S-04 lifecycle behavior.

**Implementation Note**: Pause for manual confirmation after automated verification passes.

---

## Phase 3: Frontend — loop-recovery actions

### Overview

Wire the two reserved card menu slots to live transitions: Unclaim (direct) and Send back (a small
comment dialog), with board orchestration and polling-pause discipline.

### Changes Required:

#### 1. Service methods + DTO fields

**File**: `web/src/app/board/task.service.ts`

**Intent**: Call the new routes and carry the new flags.

**Contract**: Add `unclaim(id)` → `POST /unclaim` + refetch; `sendBack(id, comment)` →
`POST /sendback` + refetch. Extend the `Task` interface with `canUnclaim`, `canSendBack`,
`commentCount`.

#### 2. Send-back dialog component

**File**: `web/src/app/board/send-back/send-back.component.ts` (+ html/scss, new)

**Intent**: Collect the required reason before sending back, matching the existing CDK-dialog pattern.

**Contract**: A CDK `Dialog` component (sibling of `delete-confirm`) over a reactive control
`comment` (required, maxlength 280) with Cancel / **Send back**; closes returning the trimmed
comment (or undefined on cancel). The board performs the actual `sendBack` call.

#### 3. Card wiring

**File**: `web/src/app/board/task-card/task-card.component.ts` + `.html`

**Intent**: Replace the two disabled placeholder buttons with live menu items gated by the flags.

**Contract**: Add `@Output() unclaim` / `sendBack`. Render the **Unclaim** item only when
`task.canUnclaim`, **Send back** only when `task.canSendBack`; both close the menu and emit. Remove
the `disabled` / "Coming soon (S-05)" placeholders.

#### 4. Column + board orchestration

**File**: `web/src/app/board/task-column/task-column.component.ts`, `web/src/app/board/board.component.ts` (+ board html)

**Intent**: Re-emit the new card events and own the service calls.

**Contract**: `TaskColumnComponent` re-emits `unclaim` / `sendBack`. `BoardComponent.unclaim(task)`
calls `tasks.unclaim` via the existing `run()` (self-heals on 403/409). `BoardComponent.sendBack(task)`
pauses polling, opens `SendBackComponent`, and on a returned comment calls `tasks.sendBack`, resuming
polling on close — mirroring `requestDelete`.

### Success Criteria:

#### Automated Verification:

- SPA builds: `ng build` (Release path exercised by the .NET build).
- Lint passes: the repo's configured lint script.
- vitest specs pass: card shows Unclaim/Send-back only when the flags are set and emits on click;
  the send-back dialog blocks an empty comment and returns a trimmed one; the board calls the service
  + pauses/resumes polling around the dialog.

#### Manual Verification:

- Claimer unclaims their in-progress task; it returns to To do instantly.
- An admin unclaims another member's stuck task.
- An admin sends back a Done task after typing a reason; it lands in In progress with the claimer.
- The board self-heals if the server rejects a stale affordance (race with another member).

**Implementation Note**: Pause for manual confirmation after automated verification passes.

---

## Phase 4: Frontend — comments thread, count badge, admin edit

### Overview

Surface the comments feature: a 💬 count badge on the card and a lazy-loaded author/timestamp thread
plus an add-comment input in the detail dialog, with fields editable only for admins.

### Changes Required:

#### 1. Comment service + model

**File**: `web/src/app/board/task.service.ts`

**Intent**: Load and post comments for the dialog.

**Contract**: A `Comment` interface (`id, body, kind, authorName, createdAtUtc`); `getComments(id)`
→ `GET /api/tasks/{id}/comments`; `addComment(id, body)` → `POST …/comments`. (Posting need not
refetch the whole board — the dialog re-lists comments and the badge updates on the next poll/refetch.)

#### 2. Count badge on the card

**File**: `web/src/app/board/task-card/task-card.component.html` + `.scss`

**Intent**: A Jira-style 💬 indicator with the comment count in the card's bottom-left.

**Contract**: When `task.commentCount > 0`, render a small comment-count element bottom-left; purely
presentational, no new output (opening the thread is the existing title→`openDetail`). Must respect
the ≤400px layout (NFR-2).

#### 3. Comments thread + add-comment in the dialog

**File**: `web/src/app/board/task-detail/task-detail.component.ts` + `.html` + `.scss`

**Intent**: Show the immutable thread (author + when, send-back reasons distinguished) and let any
member post; keep fields editable for admins, read-only for members — driven by `canEdit`.

**Contract**: On open, call `getComments(task.id)` into a signal (lazy-load; handle the empty state).
Render each comment with author name, `date`-piped timestamp, body, and a label/style when
`kind === 'SendBack'`. Add a reactive `newComment` control (required, maxlength 280) + **Post**
button calling `addComment` then re-listing; an in-flight/error state mirrors the existing `pending`
/`errors` handling. Field editing already keys off `canEdit` (now admin) — no logic change there, but
confirm members see the read-only render with the thread + input still available.

### Success Criteria:

#### Automated Verification:

- SPA builds: `ng build`.
- Lint passes.
- vitest specs pass: the card badge renders only when `commentCount > 0`; the dialog lists comments
  with author/time and flags send-back kind; posting an empty comment is blocked; a valid post calls
  the service and re-lists; an admin sees editable fields while a member sees read-only fields + the
  comment input.

#### Manual Verification:

- The 💬 count on a card matches the number of comments in its dialog.
- A member (non-admin) can post a comment but cannot edit fields; an admin can do both.
- The send-back reason from Phase 3 appears in the thread, attributed and timestamped.
- Thread + input are usable at ≤400px without horizontal scroll (NFR-2).

**Implementation Note**: Pause for manual confirmation after automated verification passes.

---

## Phase 5: Charter reconciliation (docs)

### Overview

Record the two boundary overrides and the slice's completion in the foundation docs so the artifacts
stay truthful — the comments feature contradicted a PRD Non-Goal and the edit change diverged from FR-011.

### Changes Required:

#### 1. PRD Non-Goal + FR-011 note

**File**: `context/foundation/prd.md`

**Intent**: Stop the docs from asserting behavior the product no longer has.

**Contract**: Amend the *"No comments, multimedia, or chat on tasks"* Non-Goal to record that v1 now
ships flat, immutable, text-only task comments (decided 2026-06-11, S-05), with reply/media/chat
still out. Annotate FR-011 that field editing is admin-only/any-column as of S-05 (members comment
instead of editing).

#### 2. Roadmap status

**File**: `context/foundation/roadmap.md`

**Intent**: Reflect delivery + scope growth.

**Contract**: Set S-05 status → `done` in the At-a-glance table, Slices section, and Backlog Handoff;
add a one-line note that the slice grew to include free-form comments + admin-edit (Non-Goal override).

#### 3. Contract-surfaces registry

**File**: `docs/reference/contract-surfaces.md` (create if absent)

**Intent**: Register the load-bearing names this slice introduces.

**Contract**: List the new routes (`/api/tasks/{id}/unclaim`, `/sendback`, `/comments`), the
`TaskComment` entity + `TaskCommentKind`, the new `TaskEventType` members, and the new affordance
flags (`canUnclaim`, `canSendBack`, `commentCount`, redefined `canEdit`). If the file doesn't exist,
seed it with just these entries (do not block on back-filling prior slices).

### Success Criteria:

#### Automated Verification:

- The three docs exist and contain the new entries (grep for `unclaim`, `sendback`, `TaskComment`,
  `S-05` status `done`).

#### Manual Verification:

- A reader of the PRD/roadmap is no longer misled by the retired Non-Goal / FR-011 wording.

**Implementation Note**: Final phase — no code; commit alongside or after Phase 4.

---

## Testing Strategy

### Unit / component (vitest):

- Card: conditional render + emit of Unclaim/Send-back; 💬 badge visibility by `commentCount`.
- Send-back dialog: required-comment validation, trimmed return, cancel path.
- Detail dialog: comment list render (author/time/kind), add-comment validation + post, admin vs
  member field editability.
- Board: orchestration calls + polling pause/resume around the send-back dialog; self-heal on 403/409.

### Integration (xUnit, `TaskEndpointsTests` style):

- All Phase 1 comment endpoint cases + Phase 2 transition guards/affordances enumerated in those
  phases' Automated Verification. Reuse `SeedMemberAsync` for the non-admin and admin-vs-claimer paths.

### Manual:

1. Two-member household: claimer unclaims; admin unclaims a stuck task; admin sends back with a reason.
2. Member posts a comment; admin edits a claimed task's fields; member cannot.
3. Verify the badge count, thread attribution, and ≤400px layout.

## Performance Considerations

Comment volume per task is tiny for a single-household chore tracker. The board GET carries only a
grouped `commentCount` (no bodies); full threads load lazily per task on dialog open. The `TaskId`
index backs both the count and the thread query. No new hot path.

## Migration Notes

One additive migration (`AddTaskComments`) — a new table only, backward-compatible (consistent with
the F-01 "migrations stay backward-compatible" rule). New `TaskEventType` values need no migration
(string-converted column). No data backfill.

## References

- North-star lifecycle this extends: `src/Homdutio.Api/Tasks/TaskEndpoints.cs`
- Event log + foreshadowing: `src/Homdutio.Data/Entities/TaskEvent.cs`, `TaskEventType.cs:6`
- Reserved UI slots: `web/src/app/board/task-card/task-card.component.html:42`
- Dialog pattern to mirror: `web/src/app/board/delete-confirm/delete-confirm.component.ts`
- Test template: `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs`
- Roadmap S-05 / FR-022 / FR-023; PRD Non-Goal "No comments/chat on tasks"; FR-011

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Comments backend foundation

#### Automated

- [x] 1.1 Solution builds (`dotnet build`) — a018354
- [x] 1.2 Migration applies cleanly on a fresh DB (`dotnet test` fixture migrate) — a018354
- [x] 1.3 Comment endpoint xUnit tests pass (post 201 + persist; list ordered + author names; blank/oversized → 400; foreign id → 404; 401; commentCount correct) — a018354

#### Manual

- [x] 1.4 A posted comment survives an app restart (durable row) — a018354
- [x] 1.5 `GET /api/tasks` payload carries only the count, no bodies — a018354

### Phase 2: Loop-recovery transitions backend

#### Automated

- [x] 2.1 Solution builds (`dotnet build`) — ab19d26
- [x] 2.2 Unclaim tests pass (claimer + admin succeed, `Unclaimed` appended, unassigned; non-claimer non-admin → 403; non-in-progress → 409) — ab19d26
- [x] 2.3 Send-back tests pass (admin → In progress, claimer kept, `DoneAtUtc` cleared, `SentBack` event + `SendBack` comment; non-admin → 403; non-Done → 409; blank/oversized → 400) — ab19d26
- [x] 2.4 Admin-edit tests pass (admin edits claimed/Done → 200; non-admin edit → 403) — ab19d26
- [x] 2.5 Affordance-flag + foreign-id-404 + 401 tests pass for both new routes — ab19d26

#### Manual

- [x] 2.6 Audit trail of a claim→done→send-back→done→confirm cycle reads coherently — ab19d26
- [x] 2.7 No regression in existing S-03/S-04 lifecycle — ab19d26

### Phase 3: Frontend — loop-recovery actions

#### Automated

- [x] 3.1 SPA builds (`ng build`) — d682f31
- [x] 3.2 Lint passes — d682f31
- [x] 3.3 vitest specs pass (card conditional render/emit; send-back dialog validation/trim; board call + polling pause/resume) — d682f31

#### Manual

- [x] 3.4 Claimer unclaims own task; admin unclaims a stuck task (instant return to To do) — d682f31
- [x] 3.5 Admin sends back a Done task with a reason → In progress, claimer preserved — d682f31
- [x] 3.6 Board self-heals on a stale-affordance 403/409 — d682f31

### Phase 4: Frontend — comments thread, count badge, admin edit

#### Automated

- [x] 4.1 SPA builds (`ng build`)
- [x] 4.2 Lint passes
- [x] 4.3 vitest specs pass (badge by commentCount; thread render w/ author/time + send-back kind; empty-comment blocked; valid post re-lists; admin editable vs member read-only)

#### Manual

- [x] 4.4 Card 💬 count matches the dialog's comment count
- [x] 4.5 Member can comment but not edit fields; admin can do both
- [x] 4.6 Send-back reason appears in the thread, attributed + timestamped
- [x] 4.7 Thread + input usable at ≤400px (NFR-2)

### Phase 5: Charter reconciliation (docs)

#### Automated

- [ ] 5.1 Docs updated (grep finds `unclaim`/`sendback`/`TaskComment` in contract-surfaces; S-05 `done` in roadmap; PRD Non-Goal + FR-011 amended)

#### Manual

- [ ] 5.2 PRD/roadmap no longer misstate the comments Non-Goal or FR-011 edit model
