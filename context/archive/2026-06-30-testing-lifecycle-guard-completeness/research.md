---
date: 2026-06-30T22:21:46+0200
researcher: Rafal Michalak
git_commit: ce66f7fcc415f42351fa2754507ba97a1b1116e3
branch: main
repository: run3th/homdutio
topic: "Lifecycle guard completeness — the task transition matrix, who may move a task between states, and where the guard + self-attested flag are decided (Test-plan Phase 2 / Risk #2)"
tags: [research, codebase, task-lifecycle, authorization, transition-matrix, self-attested, test-plan-phase-2, risk-2]
status: complete
last_updated: 2026-06-30
last_updated_by: Rafal Michalak
---

# Research: Lifecycle guard completeness (Test-plan Phase 2 / Risk #2)

**Date**: 2026-06-30T22:21:46+0200
**Researcher**: Rafal Michalak
**Git Commit**: ce66f7f
**Branch**: main
**Repository**: run3th/homdutio

## Research Question

Ground the QA plan for **Risk #2**: a non-admin confirms, an admin confirms own
work without `self-attested`, a non-claimer marks done, or a double-claim
succeeds — any wrong-actor / wrong-state transition that corrupts the honest
record. Specifically (per test-plan §2 Risk Response): **the full transition
matrix** (who may move a task from which state to which) and **where the guard +
the `self-attested` flag are decided**. Anti-pattern to avoid: testing only
allowed transitions, or lifting the expected value from the guard code (the
oracle problem).

## Summary

The entire task-mutating surface is **two files**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs`
(all explicit lifecycle endpoints) and `src/Homdutio.Api/Households/HouseholdEndpoints.cs`
(one *implicit* `InProgress → ToDo` transition via the member-removal sweep). The
authorization boundary helper is `src/Homdutio.Api/HouseholdScope.cs`.

The state universe is small: `HouseholdTaskStatus { ToDo, InProgress, Done }`
(`src/Homdutio.Data/Entities/HouseholdTaskStatus.cs:12-17`) and
`HouseholdRole { Admin, Member }` (`src/Homdutio.Data/Entities/HouseholdRole.cs:9-13`).
**There is no `Closed` status** — closure is `ClosedAtUtc != null`, set at confirm,
with status staying `Done`.

Three findings dominate the test design:

1. **Guard ordering is NOT uniform**, and it determines the status code a test
   must assert. Two endpoints check **state first** (409 wins), two check **role
   first** (403 wins). A non-admin hitting a wrong-state confirm gets **403**, not
   409. A non-claimer hitting a wrong-state done gets **409**, not 403. The test
   matrix must respect which guard fires first per endpoint (see §"Guard ordering").

2. **`self-attested` is computed server-side at confirm** as
   `task.ClaimedById == caller.UserId` (`TaskEndpoints.cs:228`), written to both the
   projection and the `Confirmed` `TaskEvent`. It is **allowed, never blocked** — an
   admin may confirm their own claimed+done work; the flag merely records it
   (PRD FR-016: v1 records, does not cap). So "prove `self-attested` is correct"
   means: `true` iff confirmer == claimer, `false` when an admin confirms another
   member's work, and never client-settable.

3. **Three real seams the happy-path coverage misses**, suitable for this phase
   (see §"Gaps & risk seams"): the **`Done`/`Confirm` foreign-household 404** is
   untested for those two verbs; the **implicit unclaim** on member removal bypasses
   `/unclaim` and its guards; and **double-claim is guarded only by an in-handler
   status read** with no DB concurrency token (the *logical* 409 is testable here;
   the *true race* belongs to Phase 3 / Risk #3 — flag the seam, don't claim it).

## Detailed Findings

### The transition matrix (ground truth from code)

| From-state | Action | HTTP route | Required actor/role | Resulting state | Rejection shape |
|---|---|---|---|---|---|
| (none) | Create | `POST /api/tasks/` | any member | `ToDo`, unassigned | 404 no membership; 400 blank title / bad tags |
| `ToDo` | Claim | `POST /api/tasks/{id}/claim` | any member | `InProgress`, claimer set | **409** if not `ToDo` (incl. double-claim); 404 foreign/missing |
| `InProgress` | Mark done | `POST /api/tasks/{id}/done` | **claimer only** | `Done`, `DoneAtUtc` set | **409** if not `InProgress` *(checked first)*; **403** if not claimer |
| `Done` | Confirm | `POST /api/tasks/{id}/confirm` | **admin only** | `Done` + `ClosedAtUtc` set (+ `SelfAttested` if admin==claimer) | **403** if not admin *(checked first)*; **409** if not `Done` |
| `InProgress` | Unclaim | `POST /api/tasks/{id}/unclaim` | **claimer OR any admin** | `ToDo`, claim cleared | **409** if not `InProgress` *(checked first)*; **403** if neither claimer nor admin |
| `Done` | Send back | `POST /api/tasks/{id}/sendback` | **admin only** | `InProgress`, **claimer retained** | **403** if not admin *(checked first)*; **409** if not `Done`; **400** if reason empty/>280 |
| any | Edit | `PUT /api/tasks/{id}` | **admin only** | unchanged status | **403** if not admin; 400 blank title/bad tags |
| `ToDo` | Delete | `DELETE /api/tasks/{id}` | **any member (NOT role-gated)** | row deleted | **409** if not `ToDo` |
| `InProgress` | (member removed) | side-effect of `DELETE` member | removing admin | `ToDo`, claim cleared | — (no direct endpoint; see seam below) |

There is **no `reject` and no explicit `close` endpoint** — "reject" is
`sendback` (`Done → InProgress`), and "close" happens inside `confirm`. The only
ways out of `Done` are confirm (closes) or sendback (→ `InProgress`). No
transition lets an admin directly close a `ToDo`/`InProgress` task, and there is
no `Done → ToDo` shortcut.

### Guard ordering (the oracle that drives which status to assert)

The uniform prefix on every endpoint is: **(a) resolve caller → 404 if no
membership, (b) load household-scoped task → 404 if foreign/missing**, then the
per-endpoint state/role guards. Within those per-endpoint guards the order
differs and is load-bearing for the test matrix:

- **State-first (409 beats 403)**: `done` (`TaskEndpoints.cs:178` state, then `:183` actor), `unclaim` (`:260` state, then `:266` actor).
- **Role-first (403 beats 409)**: `confirm` (`TaskEndpoints.cs:217` role, then `:222` state), `sendback` (`:302` role, then `:307` state).

Concrete consequence to encode in tests:
- A **non-admin** confirming a task that is **not yet Done** → **403** (role guard fires first at `:217`).
- A **non-claimer** marking done a task that is **not InProgress** → **409** (state guard fires first at `:178`).

### Status / error shapes (assert these exactly)

- **No membership / no `sub` claim** → `404` (`HouseholdScope.cs:30-37` returns null).
- **Foreign or missing task id** → `404`, no existence leak (`HouseholdScope.cs:41-42` `LoadScopedTaskAsync` filters on `t.Id == id && t.HouseholdId == householdId`).
- **Wrong current state** → `409 Conflict` with `{ message }` body (`Results.Conflict(new { message = ... })`).
- **Wrong actor/role** → `403 Forbidden` via `Results.Forbid()` (empty body).
- **Validation** → `400 ValidationProblem` (RFC7807): create title/tags, sendback reason 1–280, comment body 1–280, reorder unknown status.
- **Unauthenticated** → `401` (group-level `.RequireAuthorization()` at `TaskEndpoints.cs:23`, before any handler).

### The `self-attested` decision (one place)

Set inside `confirm` only (`TaskEndpoints.cs:228, 231, 234`):

```
228:  var selfAttested = task.ClaimedById == caller.UserId;
229:  task.ConfirmedById = caller.UserId;
231:  task.SelfAttested = selfAttested;
234:  confirmed.SelfAttested = selfAttested;   // mirrored onto the Confirmed TaskEvent
```

- Condition: the confirming admin is the same user who claimed the task.
- Written to both `HouseholdTask.SelfAttested` (`src/Homdutio.Data/Entities/HouseholdTask.cs:55`) and the `Confirmed` `TaskEvent.SelfAttested` (`src/Homdutio.Data/Entities/TaskEvent.cs:26`).
- The SPA preview flag `willSelfAttest = canConfirm && task.ClaimedById == caller.UserId` (`TaskEndpoints.cs:684`) is **preview only**, never the source of truth.
- **Self-attestation is permitted, not blocked** (PRD FR-016: v1 records the flag, no cap; never surfaced in UI). A test that expects self-confirm to be *rejected* would be asserting a rule that does not exist — assert the **flag value**, not a rejection.

### Authorization layering (three separate layers, scope always first)

1. **Authentication** — `app.UseAuthentication()` + group `.RequireAuthorization()` (`TaskEndpoints.cs:23`; pipeline `Program.cs:173-174`). Unauthenticated → 401.
2. **Household-scope / membership** — derived **server-side from the JWT `sub` claim**, never client-supplied: `HouseholdScope.ResolveCallerAsync` (`HouseholdScope.cs:25-38`) + `LoadScopedTaskAsync` (`:41-42`). A handler-invoked helper (not middleware), called as the first two steps of every endpoint. Failure → 404. This is the single canonical cross-household boundary (Risk #1's surface; Phase 1 already swept it).
3. **Role / actor eligibility** — inline per handler against `caller.Role` and `task.ClaimedById` (the `:183/:217/:266/:302/:362` guards). Failure → 403.

Because scope (404) always runs before role (403), a non-member or foreign-task
caller can never reach the role check, so can never distinguish "exists but
forbidden" from "doesn't exist."

### Atomicity (relevant to the audit-record assertion, shared with Phase 3 / Risk #5)

Each transition mutates the `HouseholdTask` projection **and** appends exactly one
`TaskEvent` in a **single `SaveChanges`** (e.g. confirm `TaskEndpoints.cs:228-236`;
sendback also writes a `SendBack`-kind `TaskComment` in the same save,
`:328-337`). The append-only event types are
`Created, Claimed, MarkedDone, Confirmed, Unclaimed, SentBack`
(`src/Homdutio.Data/Entities/TaskEventType.cs:10-18`). No update/delete path
exists. (Deep audit-durability assertions are Phase 3 / Risk #5 — here we need
only that the right event is appended with the right `SelfAttested`.)

## Existing test coverage (so the plan targets gaps, not duplicates)

All in `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs`. Fixture:
`IClassFixture<AuthApiFactory>` (`AuthApiFactory.cs:18-86`) — real API over a
throwaway LocalDB, deterministic JWT config, capturing email sender.

**Already covered — illegal/rejected paths:**

| Transition | Scenario | Test (file:line) |
|---|---|---|
| Claim | already-claimed → 409 | `Claiming_an_already_claimed_task_returns_409` (`:170`) |
| Claim | foreign task → 404 | `Foreign_household_task_id_returns_404` (`:243`) |
| Done | non-claimer → 403 | `Marking_done_as_non_claimer_returns_403` (`:183`) |
| Done | wrong state (ToDo) → 409 | `Marking_done_on_a_non_in_progress_task_returns_409` (`:200`) |
| Confirm | non-admin → 403 | `Confirming_as_a_non_admin_member_returns_403` (`:212`) |
| Confirm | wrong state (InProgress) → 409 | `Confirming_a_non_done_task_returns_409` (`:230`) |
| Unclaim | non-claimer non-admin → 403 | `Unclaim_by_a_non_claimer_non_admin_returns_403` (`:615`) |
| Unclaim | wrong state → 409 | `Unclaim_of_a_non_in_progress_task_returns_409` (`:636`) |
| Unclaim/Sendback | foreign task → 404 | `Unclaim_and_send_back_on_a_foreign_household_task_return_404` (`:794`) |
| Send back | non-admin → 403 | `Send_back_by_a_non_admin_returns_403` (`:679`) |
| Send back | wrong state → 409 | `Send_back_of_a_non_done_task_returns_409` (`:697`) |
| Send back | bad reason → 400 | `Send_back_with_a_blank_or_oversized_reason_returns_400` (`:711`) |
| Edit | non-admin → 403 | `:725` |
| Delete | claimed task → 409 | `:481` |

**Already covered — `self-attested` and allowed paths:**
- `Self_attested_confirm_records_flag_on_event_and_projection` (`:150`) — admin==claimer → `SelfAttested=true` on task + event.
- `Done_task_reports_can_confirm_only_for_admin_with_self_attest` (`:296`) — affordance flag `WillSelfAttest=true` preview.
- `Full_loop_closes_task_and_get_omits_it` (`:106`), `Closed_task_row_and_events_persist` (`:125`), unclaim/sendback happy paths (`:570, :594, :647`).

**Fixture helpers available** (reuse, don't reinvent): `RegisterAndLoginAsync` (`:34`),
`CreateHouseholdAsync` (creator auto-becomes Admin), `SeedMemberAsync(email, householdId, role)`
(`:85-99`, seeds a member row directly — no invite flow needed), `ActionAsync(token,id,action)`
(`:74`), `SendBackAsync` (`:559`), `LoadTaskRowAsync(id)` (`:562`, reads the persisted row),
`GetBoardAsync` (`:77`). Promote to admin via `SetRoleAsync` (`HouseholdMemberAdminTests.cs:97`).

## Gaps & risk seams (the plan's target set)

Ordered by Risk #2 relevance. Each is a *failure scenario*, not a test name.

1. **`self-attested = false` on cross-member confirm is NOT explicitly asserted.**
   Only the `true` (self-attested) and the role-rejection paths exist. The
   positive cross-actor case — *admin confirms a task another member claimed+done
   → confirm succeeds AND `SelfAttested == false` on both projection and event* —
   has no dedicated test (`:296` only checks the preview flag). This is the heart
   of "set iff admin confirms own work."

2. **Foreign-household 404 untested for `done` and `confirm`.** Parity is proven
   for claim/unclaim/sendback (`:243, :794`) but **not** for `done` or `confirm`.
   A foreign-household `Done` task confirmed by an outsider must be **404** (scope
   first), not 403. (Overlaps Phase 1 / Risk #1 surface, but these two verbs are a
   genuine hole in the lifecycle sweep.)

3. **Guard-ordering corner cases are unasserted.** Specifically:
   *non-admin + not-yet-Done confirm → 403* (role beats state) and
   *non-claimer + not-InProgress done → 409* (state beats actor). The existing
   tests vary one axis at a time; the crossed case that proves the *ordering* is
   missing and is exactly where an "obvious" test would assert the wrong code.

4. **Logical double-claim is the only race testable at this layer.** The second
   claim on an `InProgress` task returns 409 via the in-handler status read
   (`TaskEndpoints.cs:143`) — testable now. But there is **no optimistic
   concurrency token / rowversion** on `HouseholdTask` (contrast the deliberate
   `UPDLOCK, HOLDLOCK` last-admin guard at `HouseholdEndpoints.cs:432-436`), so a
   *true simultaneous* double-claim is not provably rejected at the DB level.
   **Flag this seam; do not claim it here** — the concurrent-race assertion is
   Phase 3 / Risk #3 (see `lessons.md` "Guard min-count invariants with an atomic
   check-and-mutate").

5. **Implicit `InProgress → ToDo` via member removal bypasses `/unclaim`.**
   `HouseholdEndpoints.cs:444-475` (`SweepAndRemoveAsync`) sweeps a removed
   member's in-progress tasks back to `ToDo` and appends `Unclaimed` events, gated
   only by the member-removal endpoint's own admin check — not the `/unclaim`
   guards. A transition matrix built only from `/api/tasks` routes misses it.
   Partially exercised by `HouseholdMemberAdminTests.cs:344`; worth confirming the
   audit event + claim-clear from the lifecycle-integrity angle.

6. **`Delete` is not role-gated** (`TaskEndpoints.cs:418-420` has no `caller.Role`
   check) — any member can delete a `ToDo` task, unlike admin-only edit. Confirm
   this is intended before asserting it; if intended, a test should pin the
   behavior so a future "lock delete to admins" change is a conscious break.

7. **Lower-value combinatorial gaps** (include only if cheap): admin unclaims own
   task; member edit attempt on claimed/Done/closed task; bystander (neither
   claimer nor admin) affordance flags all-false; re-claim after sendback (stays
   `InProgress`, so re-claim → 409).

## Code References

- `src/Homdutio.Api/Tasks/TaskEndpoints.cs:23` — group `.RequireAuthorization()`
- `src/Homdutio.Api/Tasks/TaskEndpoints.cs:143-151` — claim guard (`ToDo`→`InProgress`; 409 on double-claim)
- `src/Homdutio.Api/Tasks/TaskEndpoints.cs:178-190` — done guard (state @178, actor @183)
- `src/Homdutio.Api/Tasks/TaskEndpoints.cs:217-234` — confirm guard (role @217, state @222) + `self-attested` (@228/231/234)
- `src/Homdutio.Api/Tasks/TaskEndpoints.cs:260-273` — unclaim guard (state @260, actor @266)
- `src/Homdutio.Api/Tasks/TaskEndpoints.cs:302-323` — sendback guard (role @302, state @307, reason @313)
- `src/Homdutio.Api/Tasks/TaskEndpoints.cs:362-365` — edit guard (admin-only)
- `src/Homdutio.Api/Tasks/TaskEndpoints.cs:418-420` — delete guard (state-only, NOT role-gated)
- `src/Homdutio.Api/Tasks/TaskEndpoints.cs:681-691` — affordance/preview flags (not authz source)
- `src/Homdutio.Api/HouseholdScope.cs:25-38, 41-42` — caller resolution + scoped-task load (404 source)
- `src/Homdutio.Api/Households/HouseholdEndpoints.cs:444-475` — implicit unclaim sweep on member removal
- `src/Homdutio.Api/Households/HouseholdEndpoints.cs:432-436` — last-admin pessimistic lock (contrast: claim has none)
- `src/Homdutio.Data/Entities/HouseholdTask.cs:37-55` — actor fields (`CreatedById`, `ClaimedById`, `ConfirmedById`, `ClosedAtUtc`, `SelfAttested`)
- `src/Homdutio.Data/Entities/HouseholdTaskStatus.cs:12-17` — `{ ToDo, InProgress, Done }`
- `src/Homdutio.Data/Entities/HouseholdRole.cs:9-13` — `{ Admin, Member }`
- `src/Homdutio.Data/Entities/TaskEvent.cs:10-27` — append-only audit row + `SelfAttested`
- `src/Homdutio.Data/Entities/TaskEventType.cs:10-18` — `{ Created, Claimed, MarkedDone, Confirmed, Unclaimed, SentBack }`
- `tests/Homdutio.Api.Tests/AuthApiFactory.cs:18-86` — integration fixture
- `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs:34-99` — register/login/create-household/seed-member helpers
- `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs:106-300, 570-794` — existing lifecycle coverage

## Architecture Insights

- **Two-field closure model**: status enum (`ToDo/InProgress/Done`) + separate
  `ClosedAtUtc`. The board filters on `ClosedAtUtc == null`; status stays `Done`
  after confirm. Tests asserting "closed" must check the timestamp, not a status.
- **Event-sourced audit + projection commit atomically** in one `SaveChanges` —
  the projection can never diverge from the log. No manual transaction (the
  DbContext's `EnableRetryOnFailure` forbids user transactions spanning the retry
  boundary).
- **The SPA is authorization-dumb**: server-computed affordance flags
  (`canClaim/canMarkDone/canConfirm/canUnclaim/canSendBack/canEdit/canDelete`,
  `willSelfAttest`) tell the UI what to render; the guards are the real
  enforcement. Tests must hit the endpoints, not trust the flags.
- **Guard-order is per-endpoint, not global** — the single most error-prone fact
  for this phase; an oracle taken from "what feels right" will assert the wrong
  status on the crossed role×state cases.

## Historical Context (from prior changes)

- `context/archive/2026-06-01-accountability-loop/plan.md` — S-03, the north-star
  slice. Establishes the guarded state machine, closure-as-transition
  (`ClosedAtUtc`, lines 117-121), the 409-for-state / 403-for-role / 404-for-foreign
  convention (Success Criteria, lines 274-278), and that **`self-attested` is
  computed server-side at confirm as `confirmerId == ClaimedById`, never trusted
  from the client** (lines 127-128).
- `context/archive/2026-06-01-accountability-loop/plan-brief.md` — Decision Table
  (line 33): status enum + `ClosedAtUtc`; (line 36) "Invalid/stale transitions →
  409 (403 for role)."
- `context/archive/2026-06-11-loop-recovery/plan.md` — S-05, adds unclaim +
  sendback. **Unclaim clears the claimer; sendback keeps it** (lines 202-223).
  Also: **admin-anytime editing replaced S-04's To-do-only edit** (lines 258-259),
  and non-admins can never edit — so any pre-S-05 test asserting
  `Editing_a_claimed_task_returns_409` is obsolete by design.
- `context/foundation/prd.md` FR-016 — self-confirm is **allowed**; v1 records the
  flag, does not cap it; "social pressure of visibility is the only deterrent";
  never surfaced in UI. Confirms the rule to assert is the *flag value*, not a
  rejection.
- `context/foundation/lessons.md` — "Guard min-count invariants with an atomic
  check-and-mutate" (the last-admin TOCTOU). Relevant as the *contrast* that frames
  the double-claim seam: the codebase locks where it cares about races, and
  deliberately does not on claim — true-race coverage is Phase 3, not here.
- `context/foundation/roadmap.md` — S-03 (done 2026-06-01), S-04 (2026-06-02),
  S-05 (2026-06-11) sequencing; S-05 expanded scope overrode two settled PRD
  boundaries (comments + admin-anytime edit).

## Related Research

- `context/foundation/test-plan.md` §2 (Risk #2 row + Risk Response Guidance, line 80),
  §3 Phase 2, §6.1 (backend integration convention) — the strategy this phase executes.
- `context/archive/testing-cross-household-isolation/` (Phase 1) — the foreign-household
  404 sweep this phase extends to `done`/`confirm`.

## Open Questions

1. **Is `Delete` being member-open (not admin-gated) intended?** (`TaskEndpoints.cs:418-420`).
   The plan should pin current behavior either way; if a future change locks it to
   admins, the test makes that a conscious break.
2. **Scope of double-claim for this phase.** Confirm the plan asserts only the
   *logical* 409 (in-handler status read) and explicitly defers the *true
   concurrent* double-claim to Phase 3 / Risk #3 — otherwise the test gives a false
   sense of race safety (`HouseholdTask` has no rowversion).
3. **Do we assert the implicit member-removal unclaim** (`HouseholdEndpoints.cs:444-475`)
   here, or leave it to a household-membership phase? It is a real lifecycle
   transition but lives outside `/api/tasks`.
