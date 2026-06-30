# Lifecycle Guard Completeness Implementation Plan

## Overview

Test-plan rollout **Phase 2** (`context/foundation/test-plan.md` §3), covering
**Risk #2**: a wrong-actor / wrong-state transition corrupts the honest record
the product rides on. This plan adds **backend integration tests** (classic
layer, the cheapest real signal — no e2e promotion) that prove every illegal
transition × role × state is rejected with the **correct status code**, and that
`self-attested` is set **if and only if** an admin confirms their own claimed
work. It closes the genuine gaps research identified against the *existing*
substantial coverage — it does not re-test already-covered paths — and ends by
filling in cookbook §6.1.

## Current State Analysis

The entire task-mutating surface is two files: `src/Homdutio.Api/Tasks/TaskEndpoints.cs`
(all explicit lifecycle endpoints) and `src/Homdutio.Api/Households/HouseholdEndpoints.cs`
(one *implicit* `InProgress → ToDo` transition via the member-removal sweep). The
authorization boundary helper is `src/Homdutio.Api/HouseholdScope.cs`.

State universe is small: `HouseholdTaskStatus { ToDo, InProgress, Done }` and
`HouseholdRole { Admin, Member }`. There is **no `Closed` status** — closure is
`ClosedAtUtc != null`, set at confirm, status staying `Done`.

Existing coverage (`tests/Homdutio.Api.Tests/TaskEndpointsTests.cs`, fixture
`IClassFixture<AuthApiFactory>`) is substantial — ~14 illegal-path tests plus
`self-attested == true` and the allowed happy paths are already asserted
(research §"Existing test coverage"). The gaps below are what remains.

### Key Discoveries:

- **Guard ordering is NOT uniform** and decides which status a test must assert
  (`TaskEndpoints.cs`):
  - State-first (409 beats 403): `done` (state `:178`, actor `:183`), `unclaim` (state `:260`, actor `:266`).
  - Role-first (403 beats 409): `confirm` (role `:217`, state `:222`), `sendback` (role `:302`, state `:307`).
- **`self-attested` is computed server-side at confirm** as
  `task.ClaimedById == caller.UserId` (`TaskEndpoints.cs:228`), mirrored onto both
  the projection (`HouseholdTask.SelfAttested`) and the `Confirmed` `TaskEvent.SelfAttested`
  (`:231`, `:234`). It is **allowed, never blocked** (PRD FR-016) — the rule to
  prove is the *flag value*, not a rejection.
- **Uniform prefix on every endpoint**: resolve caller → 404 if no membership;
  load household-scoped task → 404 if foreign/missing (`HouseholdScope.cs:25-38, 41-42`),
  *then* the per-endpoint guards. Scope (404) always runs before role (403).
- **Foreign-household 404 is untested for `done` and `confirm`** specifically —
  parity is proven for claim/unclaim/sendback (`:243`, `:794`) but not these two verbs.
- **Double-claim** is guarded only by an in-handler status read (`TaskEndpoints.cs:143`);
  `HouseholdTask` has **no rowversion / optimistic-concurrency token** (contrast the
  deliberate `UPDLOCK, HOLDLOCK` last-admin guard at `HouseholdEndpoints.cs:432-436`).
  The *logical* 409 is testable here; the *true* concurrent race is Phase 3 / Risk #3.
- **Delete is NOT role-gated** (`TaskEndpoints.cs:418-420` — no `caller.Role` check):
  any member may delete a `ToDo` task, unlike admin-only edit.
- **Implicit `InProgress → ToDo` on member removal**: `SweepAndRemoveAsync`
  (`HouseholdEndpoints.cs:444-475`) reverts a removed member's in-progress tasks
  to `ToDo` and appends `Unclaimed` events, gated only by the removal endpoint's
  admin check — bypassing the `/unclaim` guards. Partially exercised by
  `HouseholdMemberAdminTests.cs:344`.
- **Reusable fixture helpers** (do not reinvent): `RegisterAndLoginAsync`,
  `CreateHouseholdAsync` (creator → Admin), `SeedMemberAsync(email, householdId, role)`,
  `ActionAsync(token, id, action)`, `SendBackAsync`, `LoadTaskRowAsync(id)`,
  `GetBoardAsync`; promote via `SetRoleAsync` (`HouseholdMemberAdminTests.cs:97`).

## Desired End State

After this plan, the test suite proves, for the task lifecycle:

1. **`self-attested` is correct in both directions** — `true` when the confirming
   admin is the claimer (already covered) *and* `false` when an admin confirms
   another member's claimed+done work, on **both** the projection and the
   `Confirmed` event.
2. **Guard-ordering crossed cases assert the code that actually fires** — non-admin
   confirming a not-yet-Done task → **403**; non-claimer marking done a
   not-InProgress task → **409**.
3. **Foreign-household isolation parity extends to `done` and `confirm`** — an
   outsider acting on a foreign-household task gets **404** (scope first), body
   byte-identical to an unknown-id 404, no existence leak.
4. **Logical double-claim is rejected** with 409 — with an explicit in-code note
   deferring the *concurrent* race to Phase 3 / Risk #3.
5. **Delete's member-open behavior is pinned** — a non-admin member can delete a
   `ToDo` task, asserted with a comment flagging it as deliberately pinned so a
   future "lock delete to admins" change is a conscious break.
6. **The implicit member-removal unclaim is asserted from the lifecycle angle** —
   the sweep clears the claim and appends an `Unclaimed` audit event.
7. **Cookbook §6.1** documents the lifecycle transition-matrix pattern and the
   guard-ordering gotcha; §6.6 carries a Phase 2 note.

Verify: `dotnet test tests/Homdutio.Api.Tests` is green, including the new tests;
`grep`-able new test names exist for each scenario above.

## What We're NOT Doing

- **Not testing the *true concurrent* double-claim race** — `HouseholdTask` has no
  rowversion; a serial test cannot observe the race. That assertion belongs to
  **Phase 3 / Risk #3** (concurrency & audit durability). We assert only the
  *logical* 409 from the in-handler status read, and flag the seam in-code.
- **Not re-testing already-covered illegal paths** (research §"Existing test
  coverage" — the ~14-row table). New tests target gaps only.
- **Not the §7 lower-value combinatorial cases** (admin unclaims own task; member
  edit on claimed/Done task; bystander all-false affordance flags; re-claim after
  sendback) — deferred; the core gaps 1–6 close every genuine Risk #2 hole.
- **Not fixing Delete's gating** — we pin current behavior; changing it is a
  product decision outside a test phase.
- **Not deep audit-durability assertions** (event log survives across many
  recovery transitions) — that is Phase 3 / Risk #5. Here we assert only that the
  *right single event* is appended with the right `SelfAttested`.
- **Not writing any production code** — this phase adds tests and a cookbook entry only.

## Implementation Approach

Three phases, all backend integration tests over the existing `AuthApiFactory`
fixture against throwaway LocalDB, reusing the established register → login →
create-household → seed-member → bearer helpers. Test placement follows each
file's domain: **lifecycle assertions extend `TaskEndpointsTests.cs`** (where all
existing lifecycle coverage lives); the **member-removal unclaim assertion extends
`HouseholdMemberAdminTests.cs`** (where the removal path is already exercised at
`:344`).

Phase 1 takes the two **oracle-critical** clusters first — the cases where a test
written from "what feels right" asserts the wrong status or misses the flag.
Phase 2 is the broader completeness sweep. Phase 3 fills the cookbook so the
pattern is reusable.

The **oracle discipline** throughout: the expected status/flag is derived from the
transition matrix and guard-ordering facts in research (which read the code once,
adversarially), **never** lifted from a fresh read of the guard at authoring time —
that would re-introduce the oracle problem the risk warns against.

## Critical Implementation Details

- **Guard ordering is the single most error-prone fact.** When a test crosses two
  axes (wrong role *and* wrong state), the asserted status must match which guard
  fires first for that specific endpoint: `confirm`/`sendback` are role-first
  (403), `done`/`unclaim` are state-first (409). Assert from the matrix, not intuition.
- **`self-attested == false` must be read from the persisted row and the event**,
  not from an affordance/preview flag. `willSelfAttest` (`TaskEndpoints.cs:684`) is
  preview-only and already covered by `:296`; it is not the source of truth. Use
  `LoadTaskRowAsync` for the projection and query the `Confirmed` `TaskEvent` for
  the mirror.
- **Foreign-404 parity means byte-identical bodies** — a foreign-id 404 must match
  an unknown-id 404 (empty body, the existence-oracle seal), consistent with the
  Phase 1 / §6.1 parity convention. Assert the body shape, not just the status.
- **The fixture is shared per test class** (`IClassFixture`). New tests must build
  their own isolated state (unique emails / households) so they don't collide with
  existing tests in the same file.

## Phase 1: Oracle-Critical Correctness

### Overview

The two clusters where the obvious test asserts the wrong thing: the
cross-member `self-attested == false` case (gap 1, the heart of "set iff admin
confirms own work"), and the guard-ordering crossed cases (gap 3).

### Changes Required:

#### 1. Cross-member confirm sets `self-attested = false`

**File**: `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs`

**Intent**: Prove the positive cross-actor path — an admin confirms a task a
*different* member claimed and marked done — succeeds **and** records
`SelfAttested == false` on both the projection and the `Confirmed` event. This is
the missing half of the "iff" rule (`true` is already covered at `:150`).

**Contract**: New test, e.g. `Cross_member_confirm_records_self_attested_false`.
Seed an admin + a member; member claims + marks done; admin confirms → 200/expected
success. Assert via `LoadTaskRowAsync(id)` that `SelfAttested == false` and
`ConfirmedById == adminId`, and that the `Confirmed` `TaskEvent.SelfAttested == false`.
Reuse `SeedMemberAsync` and `ActionAsync`; do not trust affordance flags.

#### 2. Guard-ordering: non-admin + not-yet-Done confirm → 403

**File**: `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs`

**Intent**: Pin that the **role** guard fires before the **state** guard on
`confirm` — a non-admin confirming an `InProgress` (not-Done) task gets **403**,
not 409. The crossed case the existing single-axis tests miss.

**Contract**: New test, e.g.
`Confirming_a_non_done_task_as_a_non_admin_returns_403`. State the task at
`InProgress`, call confirm as a non-admin member, assert **403**. (Existing tests
cover non-admin+Done → 403 at `:212` and admin+InProgress → 409 at `:230`; this
crosses both.)

#### 3. Guard-ordering: non-claimer + not-InProgress done → 409

**File**: `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs`

**Intent**: Pin that the **state** guard fires before the **actor** guard on
`done` — a non-claimer marking done a `ToDo` (not-InProgress) task gets **409**,
not 403.

**Contract**: New test, e.g.
`Marking_done_a_to_do_task_as_a_non_claimer_returns_409`. Leave task at `ToDo`
(unclaimed), call done as a member who is not the claimer, assert **409**.
(Existing tests cover non-claimer+InProgress → 403 at `:183` and claimer+ToDo → 409
at `:200`; this crosses both.)

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build`
- New Phase 1 tests pass: `dotnet test tests/Homdutio.Api.Tests --filter "FullyQualifiedName~TaskEndpointsTests"`
- Full API test suite still green: `dotnet test tests/Homdutio.Api.Tests`

#### Manual Verification:

- Each new test asserts the status/flag derived from the research matrix, not from a fresh read of the guard code (oracle discipline held).
- The cross-member test reads `SelfAttested` from the persisted row and the `Confirmed` event, not from an affordance flag.

**Implementation Note**: After Phase 1 automated verification passes, pause for
human confirmation before Phase 2.

---

## Phase 2: Lifecycle Completeness Sweep

### Overview

The broader gap set: foreign-household 404 parity for `done`/`confirm` (gap 2),
logical double-claim 409 (gap 4), Delete member-open pin (gap 6), and the implicit
member-removal unclaim from the lifecycle angle (gap 5).

### Changes Required:

#### 1. Foreign-household 404 parity for `done` and `confirm`

**File**: `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs`

**Intent**: Extend the foreign-household isolation sweep to the two verbs research
flagged as untested — a caller from household A acting on a household-B task via
`done` and `confirm` must get **404** (scope-first), body byte-identical to an
unknown-id 404, no existence leak.

**Contract**: New test(s), e.g. `Done_and_confirm_on_a_foreign_household_task_return_404`
(mirroring the existing `:794` unclaim/sendback parity test). Assert status **404**
and **empty body** (parity with unknown-id 404 per the §6.1 existence-oracle
convention). Reuse the two-household setup pattern from the existing foreign-task tests.

#### 2. Logical double-claim returns 409

**File**: `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs`

**Intent**: Prove a second claim on an already-`InProgress` task is rejected with
409 via the in-handler status read. Note in-code that this is the *logical* case
only; the *concurrent* race is deferred to Phase 3 / Risk #3 (no rowversion on
`HouseholdTask`).

**Contract**: New test, e.g.
`Claiming_an_in_progress_task_as_a_second_member_returns_409`. First member claims
(→ InProgress); a second member claims → **409**. Add a short comment referencing
`lessons.md` "Guard min-count invariants…" and Phase 3 for the true race.
(Distinct from the existing same-claimer re-claim at `:170`: this is a *different*
actor double-claiming.)

#### 3. Pin Delete as member-open (not role-gated)

**File**: `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs`

**Intent**: Pin current behavior — a non-admin member **can** delete a `ToDo` task
— so a future "lock delete to admins" change becomes a conscious break rather than
a silent regression.

**Contract**: New test, e.g. `Deleting_a_to_do_task_as_a_non_admin_member_succeeds`.
Seed a non-admin member, create a `ToDo` task, delete as the member → expected
success; confirm the row is gone. Add a comment flagging this as a **deliberately
pinned** assumption (Delete is intentionally not role-gated today,
`TaskEndpoints.cs:418-420`); if a future change gates it, this test should be
updated as a conscious decision.

#### 4. Member-removal sweep reverts claim and appends `Unclaimed`

**File**: `tests/Homdutio.Api.Tests/HouseholdMemberAdminTests.cs`

**Intent**: Assert the implicit `InProgress → ToDo` lifecycle transition from the
lifecycle-integrity angle — when an admin removes a member, that member's
in-progress tasks revert to `ToDo`, the claim is cleared, and an `Unclaimed` audit
event is appended (the path bypasses `/unclaim`'s guards).

**Contract**: New test extending the removal coverage near `:344`, e.g.
`Removing_a_member_reverts_their_in_progress_task_and_appends_unclaimed_event`.
Seed a member, have them claim a task (→ InProgress), admin removes the member;
assert via `LoadTaskRowAsync` that status is `ToDo` and `ClaimedById == null`, and
that an `Unclaimed`-kind `TaskEvent` exists for that task. Reference
`HouseholdEndpoints.cs:444-475` (`SweepAndRemoveAsync`).

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build`
- New Phase 2 tests pass: `dotnet test tests/Homdutio.Api.Tests`
- Foreign-404 parity tests assert empty body (not just status)

#### Manual Verification:

- The double-claim test carries the in-code note deferring the concurrent race to Phase 3.
- The Delete test carries the comment flagging the pinned-behavior assumption.
- The member-removal test asserts the persisted row + the appended event, not the board view.

**Implementation Note**: After Phase 2 automated verification passes, pause for
human confirmation before Phase 3.

---

## Phase 3: Cookbook §6.1 Update

### Overview

Fill in the lifecycle transition-matrix pattern in `test-plan.md` §6.1 (currently
"For the lifecycle transition-matrix pattern (Risk #2), see §3 rollout Phase 2 —
not yet shipped") and add a §6.6 per-phase note.

### Changes Required:

#### 1. Add the lifecycle transition-matrix cookbook entry

**File**: `context/foundation/test-plan.md`

**Intent**: Make §6.1 the canonical answer to "how do I add a lifecycle-guard test
in this project?" — capturing location, the guard-ordering gotcha (the one fact
that makes the "obvious" test wrong), reference tests, and the run command.

**Contract**: Replace the §6.1 trailing "not yet shipped" line with a short
subsection covering: location (`TaskEndpointsTests.cs`, `IClassFixture<AuthApiFactory>`,
the seed-member/action helpers); the **guard-ordering rule** (confirm/sendback
role-first → 403; done/unclaim state-first → 409 — assert from the matrix, never
from a fresh guard read); the **`self-attested` rule** (assert the flag value on
projection + event, not a rejection, not the preview flag); reference tests (the
new cross-member confirm + guard-ordering tests); run command
(`dotnet test tests/Homdutio.Api.Tests`).

#### 2. Add §6.6 Phase 2 per-phase note

**File**: `context/foundation/test-plan.md`

**Intent**: Record anything Phase 2 taught (per the §6.6 convention), e.g. the
non-uniform guard ordering and the no-rowversion double-claim seam boundary.

**Contract**: Append a `**Phase 2 — Lifecycle guard completeness (<date>).**`
bullet under §6.6.

### Success Criteria:

#### Automated Verification:

- §6.1 no longer contains the "not yet shipped" placeholder for Risk #2: `grep -n "not yet shipped" context/foundation/test-plan.md` returns nothing for the lifecycle line
- Full suite remains green: `dotnet test tests/Homdutio.Api.Tests`

#### Manual Verification:

- §6.1 reads as a usable how-to: a contributor could add a new lifecycle-guard test from it alone, and would not fall into the guard-ordering oracle trap.
- §6.6 Phase 2 note is present and accurate.

**Implementation Note**: This phase has no production-code impact; after the
cookbook reads cleanly and the suite is green, the plan is complete. Mark §3
Phase 2 row `complete` and re-invoke `/10x-test-plan` to advance the rollout.

---

## Testing Strategy

This plan *is* a testing change — the "tests" below are the suite itself.

### Integration Tests (the deliverables):

- Cross-member confirm → `SelfAttested == false` on projection + event.
- Guard-ordering crossed cases: non-admin+not-Done confirm → 403; non-claimer+not-InProgress done → 409.
- Foreign-household `done`/`confirm` → 404 with empty-body parity.
- Logical double-claim → 409 (concurrent race deferred to Phase 3).
- Delete a `ToDo` task as non-admin member → success (pinned).
- Member removal → in-progress task reverts to `ToDo`, claim cleared, `Unclaimed` event appended.

### Manual Testing Steps:

1. Run `dotnet test tests/Homdutio.Api.Tests` — confirm all new tests pass and no existing test regressed.
2. Spot-check that each crossed-axis test asserts the status the research matrix predicts (not the intuitive one).
3. Read §6.1 after Phase 3 and confirm it would steer a newcomer away from the guard-ordering oracle trap.

## Performance Considerations

None — integration tests over throwaway LocalDB, consistent with the existing
~10-file suite. New tests reuse the shared `AuthApiFactory` fixture, adding no new
host startup cost beyond the per-test state each builds.

## Migration Notes

None — no schema or data changes.

## References

- Research: `context/changes/testing-lifecycle-guard-completeness/research.md`
- Change identity: `context/changes/testing-lifecycle-guard-completeness/change.md`
- Strategy: `context/foundation/test-plan.md` §2 (Risk #2 + Risk Response), §3 Phase 2, §6.1
- Reference fixture/helpers: `tests/Homdutio.Api.Tests/AuthApiFactory.cs:18-86`, `TaskEndpointsTests.cs:34-99`
- Guard ordering source: `src/Homdutio.Api/Tasks/TaskEndpoints.cs:178, 183, 217, 222, 228-234, 260, 266, 302, 307, 418-420`
- Member-removal sweep: `src/Homdutio.Api/Households/HouseholdEndpoints.cs:444-475`
- Prior art: `context/archive/2026-06-01-accountability-loop/plan.md` (S-03 guarded state machine); `context/archive/2026-06-11-loop-recovery/plan.md` (S-05 unclaim/sendback)
- Lesson (contrast for double-claim seam): `context/foundation/lessons.md` "Guard min-count invariants with an atomic check-and-mutate"

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Oracle-Critical Correctness

#### Automated

- [x] 1.1 Build passes (`dotnet build`) — fbc2067
- [x] 1.2 New Phase 1 tests pass (`dotnet test tests/Homdutio.Api.Tests --filter "FullyQualifiedName~TaskEndpointsTests"`) — fbc2067
- [x] 1.3 Full API test suite still green (`dotnet test tests/Homdutio.Api.Tests`) — fbc2067

#### Manual

- [x] 1.4 Each new test asserts status/flag from the research matrix, not a fresh guard read (oracle discipline) — fbc2067
- [x] 1.5 Cross-member test reads `SelfAttested` from persisted row + `Confirmed` event, not an affordance flag — fbc2067

### Phase 2: Lifecycle Completeness Sweep

#### Automated

- [x] 2.1 Build passes (`dotnet build`)
- [x] 2.2 New Phase 2 tests pass (`dotnet test tests/Homdutio.Api.Tests`)
- [x] 2.3 Foreign-404 parity tests assert empty body (not just status)

#### Manual

- [x] 2.4 Double-claim test carries the in-code note deferring the concurrent race to Phase 3
- [x] 2.5 Delete test carries the comment flagging the pinned-behavior assumption
- [x] 2.6 Member-removal test asserts persisted row + appended event, not the board view (existing `HouseholdMemberAdminTests.cs:332`; no duplicate written)

### Phase 3: Cookbook §6.1 Update

#### Automated

- [ ] 3.1 §6.1 lifecycle "not yet shipped" placeholder removed (`grep -n "not yet shipped"` clean for the lifecycle line)
- [ ] 3.2 Full suite remains green (`dotnet test tests/Homdutio.Api.Tests`)

#### Manual

- [ ] 3.3 §6.1 reads as a usable how-to that steers a newcomer away from the guard-ordering oracle trap
- [ ] 3.4 §6.6 Phase 2 note present and accurate
