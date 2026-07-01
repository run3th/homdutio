# Concurrency & Audit Durability — Test Phase Implementation Plan

## Overview

Phase 3 of the test-plan rollout (`context/foundation/test-plan.md` §3) is a **proving/verification** phase, not a fix phase. Research (`research.md`) established that both target risks are already defended in current code. This plan adds integration tests on the existing xUnit + `WebApplicationFactory` + LocalDB layer that empirically prove the defenses hold:

- **Risk #3** — a concurrent cross-endpoint (demote × remove) test proving the last-admin lock cannot drive the household to zero admins.
- **Risk #5** — drop-detection tests proving recovery transitions (unclaim / send-back / confirm) always *append* audit history and never drop the prior events.

It also fills cookbook §6.2 and §6.3, and re-parks the concurrent double-claim seam (out of scope here) with a durable in-code pointer.

**No production code changes.** Test project only.

## Current State Analysis

From `research.md` (grounded against commit `1e6723a`):

- The last-admin guard is **atomic**: `IsLastAdminLockedAsync` (`src/Homdutio.Api/Households/HouseholdEndpoints.cs:432-436`) reads the admin count under SQL Server `UPDLOCK, HOLDLOCK` inside the same transaction as the mutating write (demote txn `:329-341`, remove txn `:396-407`). Locks held to commit → a concurrent second mutation blocks, re-reads the post-mutation count, and is rejected. `HouseholdMember` carries **no rowversion** — the defense is pessimistic locking.
- All three recovery transitions **append** a `TaskEvent` in the same `SaveChanges` as the projection mutation: confirm `TaskEndpoints.cs:233-236`, unclaim `:277-278`, send-back `:328-329`. Closure = `ClosedAtUtc` set non-null (`HouseholdTask.cs:51-52`), a state transition — the only hard delete is a still-`ToDo` task (`:404-429`).
- **Test coverage gaps**: (a) every admin test is serial — no `Task.WhenAll` anywhere in the suite — so the lock is unproven under concurrency; (b) the unclaim/send-back tests assert only that the *new* event exists (`AnyAsync`), not that the *prior* history survived — they cannot detect a drop/overwrite regression.
- **Harness**: `AuthApiFactory` (`tests/Homdutio.Api.Tests/AuthApiFactory.cs:18-86`) gives each test class a fresh GUID-named LocalDB; DbContext is `Scoped` (`Program.cs:33-37`) so each concurrent HTTP request gets its own scope/connection — the race is genuinely observable. Helpers are **per-file** (no shared base class).

### Key Discoveries

- The proving test is **deterministic, not flaky**: because the guard serializes via a *held* lock (not retry-on-conflict), two concurrent requests resolve to exactly one 200 and one last-admin conflict — no iteration loop or barrier needed. (`research.md` → Architecture Insights.)
- Helpers to reuse live in `HouseholdMemberAdminTests.cs:38-117` (`RegisterAndLoginAsync`, `CreateHouseholdAsync`, `SeedMemberAsync`, `UserIdByEmailAsync`, `SetRoleAsync`, `RemoveAsync`, `Authed`) and `TaskEndpointsTests.cs` (`ClaimAsync`/`MarkDoneAsync`/`ConfirmAsync`, `SendBackAsync`, `LoadTaskRowAsync`).
- The "second admin" pattern: register → `SeedMemberAsync(email, householdId, Member)` → `SetRoleAsync(adminToken, userId, "Admin")` (`HouseholdMemberAdminTests.cs:364-385`).
- The durable-record query convention is a **fresh** DI scope (`_factory.Services.CreateScope()` → `ApplicationDbContext`), never the request's cached context (`HouseholdMemberAdminTests.cs:349-361`).
- The existing serial double-claim test in `TaskEndpointsTests.cs` already carries an in-code note that the *concurrent* double-claim race is deferred (Phase 2 §6.6). This plan updates that pointer to name the re-park explicitly.

## Desired End State

`dotnet test tests/Homdutio.Api.Tests` passes with:

- A concurrent demote × remove test that fails if the last-admin lock is ever removed or weakened (asserts admin count == 1 as the primary oracle).
- Unclaim, send-back, and a compound recovery sequence tests that fail if any prior `TaskEvent` is dropped or overwritten.
- Cookbook §6.2 (concurrency/invariant pattern) and §6.3 (audit-durability pattern) filled in `test-plan.md`, replacing the `TBD — see §3 Phase 3` placeholders.
- Test-plan §3 Phase 3 row marked `complete`; a §6.6 per-phase note appended.
- The concurrent double-claim seam re-parked with a durable in-code pointer (no production change).

## What We're NOT Doing

- **Not** adding a rowversion / concurrency token to `HouseholdTask`, and **not** testing the concurrent double-claim race. Observing/defending it requires a production entity change + migration; it is re-parked as its own future hardening slice (Risk #3 as written in the risk map is strictly the zero-admin race). Recorded via an in-code pointer in Phase 1.
- **Not** modifying any production code (`src/`). This is a proving phase.
- **Not** adding new test infrastructure (no shared base class, no MSW, no new fixture) — helpers stay per-file per the established convention.
- **Not** exhaustively parametrizing every concurrent admin-pair (remove×remove, demote×demote); all funnel through the same lock, so the cross-endpoint pair is the representative proof.
- **Not** touching the e2e layer or CI gate wiring (that is §3 Phase 5).

## Implementation Approach

Two phases, each independently verifiable by `dotnet test tests/Homdutio.Api.Tests`. Phase 1 extends `HouseholdMemberAdminTests.cs` (admin/household helpers already there) and introduces the suite's first parallel-request helper local to that file. Phase 2 extends `TaskEndpointsTests.cs` (task-action helpers already there). Each phase ends by filling its cookbook section; Phase 2 also performs rollout closeout.

## Critical Implementation Details

**Concurrency oracle (Risk #3).** Assert the **post-state invariant** — after both requests settle, the persisted admin count for the household is exactly 1 — as the primary check, queried from a fresh DI scope. Response codes (exactly one 200, one last-admin conflict) are a secondary assertion. Do **not** assert on the guard's message text; the risk map explicitly names that as the anti-pattern (a passing status can coexist with a corrupted post-state).

**Guard-status source of truth.** The last-admin rejection status is whatever the demote/remove handlers currently return on the last-admin branch — derive the expected code from the handler behavior at `HouseholdEndpoints.cs:327-346` / `:394-412`, not from intuition (same oracle discipline as Phase 2's guard-ordering note in §6.1).

**Determinism.** No retry loop or synchronization barrier is needed — the held `UPDLOCK/HOLDLOCK` serializes the two requests, so the outcome is deterministic. If a future author observes flakiness, that is itself a signal the lock regressed.

**Fresh-scope query.** All durable-record assertions (both phases) must read from `_factory.Services.CreateScope()` → `ApplicationDbContext`, ordering events by `OccurredAtUtc`, never the in-request context.

---

## Phase 1: Last-admin race concurrency test (Risk #3)

### Overview

Add the suite's first concurrent-request test: two admins in one household, a simultaneous demote of one and remove of the other, proving the lock prevents a zero-admin outcome. Fill cookbook §6.2 and re-park double-claim.

### Changes Required:

#### 1. Parallel-request helper

**File**: `tests/Homdutio.Api.Tests/HouseholdMemberAdminTests.cs`

**Intent**: Introduce a small local helper to fire two authed requests concurrently and await both, since no such pattern exists in the suite. Keep it per-file (no shared base class), consistent with convention.

**Contract**: A helper that builds two `HttpRequestMessage`s via the existing `Authed(...)` pattern and awaits them with `Task.WhenAll`, returning both `HttpResponseMessage`s for assertion. No change to existing helpers.

#### 2. Concurrent demote × remove test

**File**: `tests/Homdutio.Api.Tests/HouseholdMemberAdminTests.cs`

**Intent**: Prove that a concurrent demote-of-admin-A and remove-of-admin-B in a two-admin household cannot both succeed — the household must retain at least one admin. This is the test the `lessons.md` TOCTOU implies and that no serial test can observe.

**Contract**: Arrange two admins (register both, seed the second as `Member`, promote to `Admin`) in one household, with a distinct actor token issuing each mutation. Fire `POST /api/households/members/{userIdA}/role` (demote to Member) and `DELETE /api/households/members/{userIdB}` concurrently. Assert (primary) the persisted `Admin` count for the household == 1 via a fresh scope; assert (secondary) exactly one response is success and one is the last-admin rejection. Build isolated state (unique emails/household) since the fixture is shared per class.

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build`
- The new concurrency test passes: `dotnet test tests/Homdutio.Api.Tests --filter FullyQualifiedName~HouseholdMemberAdminTests`
- Full backend suite passes: `dotnet test tests/Homdutio.Api.Tests`

#### Manual Verification:

- Temporarily weakening the guard (e.g. removing the `UPDLOCK/HOLDLOCK` hint locally) makes the new test fail on the admin-count invariant — confirming it actually observes the race — then revert.

#### 3. Cookbook §6.2 + double-claim re-park

**File**: `context/foundation/test-plan.md`

**Intent**: Replace the §6.2 `TBD` placeholder with the concrete parallel-request pattern (location, helper, reference test, run command, and the post-state-invariant oracle rule). Confirm the durable re-park pointer for double-claim.

**Contract**: §6.2 sub-section rewritten to name `HouseholdMemberAdminTests.cs` as home, the `Task.WhenAll` helper, the reference test name, `dotnet test tests/Homdutio.Api.Tests`, and the "assert post-state invariant, not guard message" rule.

**File**: `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs`

**Intent**: Update the existing in-code note at the serial double-claim test to state explicitly that the *concurrent* double-claim race is re-parked to a dedicated future hardening slice (requires a `HouseholdTask` rowversion), not covered by this phase.

**Contract**: Comment-only edit at the double-claim test; no test logic change.

### Success Criteria:

#### Automated Verification:

- Suite still passes after the comment edit: `dotnet test tests/Homdutio.Api.Tests`

#### Manual Verification:

- §6.2 reads as an actionable recipe (a new reader could add a concurrency test from it) and the double-claim re-park pointer is unambiguous.

---

## Phase 2: Audit-durability drop-detection tests (Risk #5)

### Overview

Add tests proving recovery transitions preserve the full prior audit history (not just append the new event), including a compound sequence, then fill cookbook §6.3 and perform rollout closeout.

### Changes Required:

#### 1. Prior-history-survives tests for unclaim and send-back

**File**: `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs`

**Intent**: Strengthen the recovery-transition coverage from "the new event exists" to "the complete prior event set survived and the new event was appended" — the assertion that actually detects a drop/overwrite regression.

**Contract**: For unclaim (after claim) and send-back (after claim→done), query the full `TaskEvents` set for the task from a fresh scope, ordered by `OccurredAtUtc`, and assert the prior events (`Created`, `Claimed`, and `MarkedDone` for send-back) are all still present *plus* the new (`Unclaimed` / `SentBack`) event — no prior event dropped, none overwritten. Build isolated state (unique emails/household).

#### 2. Compound recovery-sequence test

**File**: `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs`

**Intent**: Prove events accumulate append-only across repeated recovery transitions — the strongest drop/overwrite guard.

**Contract**: Drive a sequence such as claim → done → send-back → done → confirm, then assert the full ordered `TaskEvent` list contains every expected event in order (`Created, Claimed, MarkedDone, SentBack, MarkedDone, Confirmed`), the row persists with `ClosedAtUtc != null`, and `self-attested` is correct on the final `Confirmed` event. Query from a fresh scope.

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build`
- The new audit tests pass: `dotnet test tests/Homdutio.Api.Tests --filter FullyQualifiedName~TaskEndpointsTests`
- Full backend suite passes: `dotnet test tests/Homdutio.Api.Tests`

#### Manual Verification:

- Temporarily changing a recovery handler to overwrite/skip an event locally makes a drop-detection test fail — confirming the assertion bites — then revert.

#### 3. Cookbook §6.3 + rollout closeout

**File**: `context/foundation/test-plan.md`

**Intent**: Replace the §6.3 `TBD` placeholder with the audit-durability pattern (query the durable record from a fresh scope, assert full history preserved — not the board view), mark §3 Phase 3 `complete`, and append the §6.6 per-phase note.

**Contract**: §6.3 sub-section rewritten (location `TaskEndpointsTests.cs`, fresh-scope query pattern, reference test names, `dotnet test tests/Homdutio.Api.Tests`, "assert full prior history, never `AnyAsync`-only" rule); §3 Phase 3 Status cell → `complete`; a §6.6 bullet appended summarizing the phase's key lesson (both risks were already defended — the value was the proving tests; the concurrency test is deterministic because the guard serializes via a held lock).

### Success Criteria:

#### Automated Verification:

- Suite still passes after doc/comment edits: `dotnet test tests/Homdutio.Api.Tests`

#### Manual Verification:

- §6.3 reads as an actionable recipe; §3 Phase 3 shows `complete`; §6.6 note is accurate.

---

## Testing Strategy

### Integration Tests:

- **Risk #3**: concurrent demote × remove on two distinct admins; assert admin count == 1 (primary) + one success/one rejection (secondary).
- **Risk #5**: full prior-history-survives after unclaim; after send-back; and across a compound claim→done→send-back→done→confirm sequence (ordered event list + closure flag + self-attested).

### Manual Testing Steps:

1. Run `dotnet test tests/Homdutio.Api.Tests` — all green.
2. Locally weaken the last-admin lock; confirm the Phase 1 test fails on the count invariant; revert.
3. Locally make a recovery handler overwrite an event; confirm a Phase 2 drop-detection test fails; revert.

## Performance Considerations

Negligible — two additional integration test classes' worth of cases against per-class LocalDB. The concurrency test issues two parallel requests and resolves deterministically (no polling/iteration).

## Migration Notes

None — no schema or data changes.

## References

- Research: `context/changes/testing-concurrency-audit-durability/research.md`
- Risk map + rollout: `context/foundation/test-plan.md` §2, §3, §6.2, §6.3, §6.6
- Lesson: `context/foundation/lessons.md` — "Guard min-count invariants with an atomic check-and-mutate" (actioned; this phase proves it)
- Guard: `src/Homdutio.Api/Households/HouseholdEndpoints.cs:432-436` (+ `:329-341`, `:396-407`)
- Recovery appends: `src/Homdutio.Api/Tasks/TaskEndpoints.cs:233-236`, `:277-278`, `:328-329`
- Harness + helpers: `tests/Homdutio.Api.Tests/AuthApiFactory.cs:18-86`, `HouseholdMemberAdminTests.cs:38-117`, `TaskEndpointsTests.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Last-admin race concurrency test (Risk #3)

#### Automated

- [x] 1.1 Build passes: `dotnet build`
- [x] 1.2 New concurrency test passes (`--filter FullyQualifiedName~HouseholdMemberAdminTests`)
- [x] 1.3 Full backend suite passes: `dotnet test tests/Homdutio.Api.Tests`
- [x] 1.4 Suite still passes after §6.2 + double-claim re-park comment edit

#### Manual

- [x] 1.5 Weakening the guard locally makes the test fail on the admin-count invariant, then revert
- [x] 1.6 §6.2 reads as an actionable recipe and the double-claim re-park pointer is unambiguous

### Phase 2: Audit-durability drop-detection tests (Risk #5)

#### Automated

- [ ] 2.1 Build passes: `dotnet build`
- [ ] 2.2 New audit tests pass (`--filter FullyQualifiedName~TaskEndpointsTests`)
- [ ] 2.3 Full backend suite passes: `dotnet test tests/Homdutio.Api.Tests`
- [ ] 2.4 Suite still passes after §6.3 + closeout doc edits

#### Manual

- [ ] 2.5 Making a recovery handler overwrite/skip an event locally makes a drop-detection test fail, then revert
- [ ] 2.6 §6.3 reads as an actionable recipe; §3 Phase 3 shows `complete`; §6.6 note is accurate
