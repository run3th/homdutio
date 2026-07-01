---
date: 2026-07-01T20:52:25+0200
researcher: Rafal Michalak
git_commit: 1e6723aed33ce3a2d5da80232c3c7c1f81904544
branch: main
repository: Homdutio
topic: "Phase 3 — Concurrency & audit durability (Risks #3, #5): ground the last-admin race guard and the audit-append model against current code"
tags: [research, codebase, concurrency, toctou, audit-trail, task-lifecycle, households, test-plan-phase-3]
status: complete
last_updated: 2026-07-01
last_updated_by: Rafal Michalak
---

# Research: Phase 3 — Concurrency & audit durability (Risks #3, #5)

**Date**: 2026-07-01T20:52:25+0200
**Researcher**: Rafal Michalak
**Git Commit**: 1e6723aed33ce3a2d5da80232c3c7c1f81904544
**Branch**: main
**Repository**: Homdutio

## Research Question

Phase 3 of the test-plan rollout (`context/foundation/test-plan.md` §3) covers two risks and must be grounded against **current code** before a QA plan is written:

- **Risk #3 (concurrency):** Two concurrent demote/remove requests on *different* admins both pass the last-admin guard → the household lands at zero admins (the TOCTOU race recorded in `context/foundation/lessons.md`). Is the guard still check-then-write, or atomic? Does the entity carry a rowversion?
- **Risk #5 (audit durability):** Closing / unclaiming / sending-back a task overwrites or **drops** audit history instead of appending → the durable record is silently lost. Does each recovery transition append, and is closure a state transition vs a delete?

Plus the harness question: what does a test author need to actually *observe* the race and query the durable record, given the classic integration layer (xUnit + `WebApplicationFactory` + LocalDB)?

## Summary

**Both risks are already defended in current code.** This phase is **verification-and-hardening**, exactly as §2 of the test plan predicted for Risks #5–#6 (and, it turns out, for #3 too — the lesson has already been actioned). The genuine gaps are *proving* tests, not fixes:

1. **Risk #3 — guard is atomic, not racy.** The last-admin guard was rewritten from the racy check-then-write described in `lessons.md` into `IsLastAdminLockedAsync`, which reads the admin count under SQL Server `UPDLOCK, HOLDLOCK` **inside the same transaction as the demote/remove write** (`HouseholdEndpoints.cs:432-436`, transactions at `:329-341` and `:396-407`). The lock is held to commit, so a concurrent second mutation blocks, re-reads the post-mutation count, and is rejected. `HouseholdMember` carries **no rowversion** — the defense is pessimistic locking, not optimistic concurrency. **Gap: every existing admin test is serial; none drives two parallel requests to prove the lock holds.**

2. **Risk #5 — audit history always appends; closure never deletes.** All three recovery transitions call `db.TaskEvents.Add(...)` for a new event (`TaskEndpoints.cs` confirm `:233-236`, unclaim `:277-278`, send-back `:328-329`), in the same `SaveChanges` as the projection mutation. Closure = `ClosedAtUtc` set non-null (a state transition), **not** a row delete; the only hard delete is a still-unclaimed *To-Do* task (`:404-429`). Existing tests cover the happy append cases — but the recovery-transition tests assert only that the **new** event exists (`AnyAsync`), not that **prior events survived undropped**, which is the precise wording of the risk ("overwrites or drops"). **Gap: a drop-detection assertion (full history preserved + one appended) across unclaim / send-back, and append-not-overwrite under repeated recovery transitions.**

3. **Harness — the parallel-request pattern is new; the race is deterministically observable.** `AuthApiFactory` gives each test class a fresh GUID-named LocalDB; DbContext is `Scoped`, so each concurrent HTTP request gets its own scope/connection — the race is real and observable under LocalDB's read-committed default. No test in the suite uses `Task.WhenAll`/`Parallel` today. Because the guard serializes via the lock, the proving test is **deterministic** (exactly one 200, one conflict; post-state count == 1), not a flaky probabilistic race needing many iterations.

**One open scope decision for the plan:** the Phase 2 note deferred the *concurrent double-claim* race to "Phase 3 (Risk #3)", but `HouseholdTask` has **no rowversion**, so that race cannot be observed (or defended) without adding a concurrency token — a **production change**, not a test. Risk #3 as written in the risk map is strictly the zero-admin race. Whether double-claim is in this phase's scope is a plan-level call (see Open Questions).

## Detailed Findings

### Risk #3 — Last-admin guard (concurrency)

**The guard is atomic via pessimistic locking.** `src/Homdutio.Api/Households/HouseholdEndpoints.cs:432-436`:

```csharp
private static async Task<bool> IsLastAdminLockedAsync(ApplicationDbContext db, Guid householdId) =>
    await db.HouseholdMembers
        .FromSqlInterpolated(
            $"SELECT * FROM [HouseholdMembers] WITH (UPDLOCK, HOLDLOCK) WHERE [HouseholdId] = {householdId} AND [Role] = {nameof(HouseholdRole.Admin)}")
        .CountAsync() <= 1;
```

- **Demote** (`POST /api/households/members/{userId}/role`) — handler `:285-359`; guard fires only on Admin→Member (`:327`); transaction `:329-341`:
  ```csharp
  var blocked = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
  {
      await using var tx = await db.Database.BeginTransactionAsync();
      if (await IsLastAdminLockedAsync(db, caller.HouseholdId)) return true;  // locked read
      target.Role = newRole;                 // write
      await db.SaveChangesAsync();
      await tx.CommitAsync();                 // locks released here
      return false;
  });
  ```
- **Remove** (`DELETE /api/households/members/{userId}`) — handler `:365-420`; guard fires only when target is Admin (`:394`); transaction `:396-407` calls `SweepAndRemoveAsync(...)` inside the lock.
- **Why the race is closed:** `UPDLOCK` takes write-intent locks on the matching admin rows and `HOLDLOCK` (serializable range lock) holds them to commit. A concurrent second demote/remove on a *different* admin blocks on those locks until the first commits, then reads the **post-mutation** count (now 1) and returns `true` → its mutation is skipped and it responds with the last-admin conflict. Both cannot pass.
- **`HouseholdMember` entity** (`src/Homdutio.Data/Entities/HouseholdMember.cs:8-23`): `Id`, `HouseholdId`, `Household?`, `UserId`, `User?`, `Role`, `JoinedAtUtc`. **No `[Timestamp]` / `RowVersion` / concurrency token.** DbContext config `ApplicationDbContext.cs:57-75` adds a unique index on `UserId` but no concurrency token.
- **Role model** (`src/Homdutio.Data/Entities/HouseholdRole.cs:9-13`): `enum HouseholdRole { Admin, Member }`, persisted as **string** (`.HasConversion<string>()`, `ApplicationDbContext.cs:64`). "Is admin" = `member.Role == HouseholdRole.Admin`.

**Existing coverage is serial.** `tests/Homdutio.Api.Tests/HouseholdMemberAdminTests.cs`:
- `Admin_can_remove_another_admin_while_one_remains` (`:364-385`) — two admins, **one** removal, single request, checks post-state. The closest test to the race, but cannot observe it.
- `Promote_then_demote_round_trips_the_role` (`:187-204`), `Self_role_change_returns_409` (`:257`), `Self_remove_returns_409` (`:304`), foreign-user 404s (`:269`, `:316`), `Remove_member_sweeps_in_progress_task_to_todo_and_drops_membership` (`:332-361`) — all serial.
- **No test issues concurrent requests anywhere in the suite** (searched `Task.WhenAll`, `Parallel.`).

### Risk #5 — Audit / event durability

**The audit model is append-only.** `src/Homdutio.Data/Entities/TaskEvent.cs:10-27`:
- Fields: `Id` (Guid PK), `TaskId` (FK → `HouseholdTask`, cascade delete, `ApplicationDbContext.cs:102-105`), `Type` (`TaskEventType`, stored as string ≤20 chars, `:100`), `ActorId` (FK → AspNetUsers), `OccurredAtUtc`, `SelfAttested` (meaningful only on `Confirmed`).
- Enum `src/Homdutio.Data/Entities/TaskEventType.cs`: `Created, Claimed, MarkedDone, Confirmed, Unclaimed, SentBack`.
- Entity docstring marks it "append-only audit log (NFR-3)"; no update/delete path, no soft-delete flags.

**Every recovery transition appends** (`src/Homdutio.Api/Tasks/TaskEndpoints.cs`):
- **Confirm** `:202-242` → sets `ConfirmedById`, `ClosedAtUtc = now`, `SelfAttested`; `db.TaskEvents.Add(NewEvent(..., Confirmed, ...))` `:233-236`. Prior events untouched.
- **Unclaim** `:244-284` → `Status = ToDo`, clears claimer; `db.TaskEvents.Add(NewEvent(..., Unclaimed, ...))` `:277`.
- **Send-back** `:286-344` → `Status = InProgress`, `DoneAtUtc = null`, **keeps** `ClaimedById`; `db.TaskEvents.Add(NewEvent(..., SentBack, ...))` `:328` **and** a `TaskComment{ Kind = SendBack }` `:329-337`, both in one `SaveChanges` `:338`.

**Closure is a state transition, not a delete.** `HouseholdTask.cs:51-52` — "closure is [`ClosedAtUtc`] being non-null, not a status value." The board query filters `ClosedAtUtc == null` (`TaskEndpoints.cs:34-36`) but the row persists. The **only** hard delete is a still-`ToDo` task (`.Remove(task)`, `:404-429`) — a task that has only ever had a `Created` event; once claimed/completed it can never be deleted, only closed.

**`self-attested` is written to two places in one `SaveChanges`** (`TaskEndpoints.cs:228,231,234`): `selfAttested = task.ClaimedById == caller.UserId`, stored on `task.SelfAttested` and on `confirmed.SelfAttested`. They cannot diverge.

**`HouseholdTask` entity** (`src/Homdutio.Data/Entities/HouseholdTask.cs:11-61`): `Id, HouseholdId, Title, Description, Category, Status (ToDo|InProgress|Done), SortOrder, CreatedById, CreatedAtUtc, ClaimedById?, ClaimedAtUtc?, DoneAtUtc?, ConfirmedById?, ClosedAtUtc?, SelfAttested, Events`. Durable-record fields: `CreatedById/At`, `ConfirmedById`, `ClosedAtUtc`, `SelfAttested`, plus the `Events` log. **No rowversion.** (Contrast `HouseholdInvite.RowVersion.IsRowVersion()` `ApplicationDbContext.cs:156`, `RefreshToken.RowVersion` `:180`.)

**Existing coverage — appends proven, drops not.** `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs`:
- `Closed_task_row_and_events_persist` (`:125-147`) — after claim→done→confirm, queries a fresh DbContext and asserts the row survives with `ClosedAtUtc != null` **and all four events present** (`Created, Claimed, MarkedDone, Confirmed`). This is the one test that already checks the *full set*.
- `Claimer_unclaims_..._back_to_to_do_unassigned` (`:730-751`) — asserts `AnyAsync(... Unclaimed)` only.
- `Admin_sends_back_...recording_the_reason` (`:807-836`) — asserts `AnyAsync(... SentBack)` + the comment only.
- `Self_attested_confirm_records_flag_on_event_and_projection` (`:150-167`) and `Cross_member_confirm_records_self_attested_false` (`:174-206`) — assert the flag on **both** projection and event.
- `HouseholdMemberAdminTests.Remove_member_leaves_a_closed_tasks_audit_attribution_intact` (`:388`) — audit attribution survives member removal.

The gap is precise: the unclaim/send-back tests confirm the **new** event lands but not that the **pre-existing** history (`Created`, `Claimed`, `MarkedDone`) is still there afterward — i.e. they don't detect a *drop/overwrite* regression, which is the literal risk.

### Harness — driving parallel requests & querying durable state

- **`AuthApiFactory`** (`tests/Homdutio.Api.Tests/AuthApiFactory.cs:18-86`): `WebApplicationFactory<Program>`; ctor generates a unique DB name `Homdutio_ApiTest_{GUID}` (`:22-27`); connection `Server=(localdb)\MSSQLLocalDB;...;MultipleActiveResultSets=true`; migrations applied once (`:65`), DB dropped on disposal (`:82`). **Per-test-class fresh DB**, no factory-level seeding.
- **Helpers** (per-file, no shared base class — live in `HouseholdMemberAdminTests.cs` / `TaskEndpointsTests.cs`, must be copied or re-declared for a new test file): `RegisterAndLoginAsync(email)` (`:38-45`), `CreateHouseholdAsync(token, name)` → household GUID, caller becomes sole admin (`:59-64`), `SeedMemberAsync(email, householdId, role)` inserts a `HouseholdMember` directly via a scope (`:67-81`), `UserIdByEmailAsync` (`:83-88`), `SetRoleAsync(token, userId, role)` = `POST .../role` (`:97-98`), `RemoveAsync(token, userId)` = `DELETE .../{userId}` (`:100-101`), task actions `ClaimAsync/MarkDoneAsync/ConfirmAsync` (`:110-117`), `Authed(...)` request builder (`:47-57`). "Second admin" pattern: register → `SeedMemberAsync(..., Member)` → `SetRoleAsync(..., Admin)`.
- **DbContext lifetime**: `AddDbContext<ApplicationDbContext>` (factory delegate, `Program.cs:33-37`) → **Scoped** by default. Each in-process HTTP request through `CreateClient()` opens its own DI scope → own DbContext + connection. Two `Task.WhenAll`-driven requests genuinely run in separate scopes; LocalDB honors read-committed isolation → the race is observable if the guard regresses.
- **Durable-record query pattern** (used e.g. `HouseholdMemberAdminTests.cs:349-361`):
  ```csharp
  using var scope = _factory.Services.CreateScope();
  var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
  var events = await db.TaskEvents.Where(e => e.TaskId == taskId)
                                  .OrderBy(e => e.OccurredAtUtc).ToListAsync();
  ```
  Always query a **fresh scope**, not the request's cached context — this is the established "durable record, not board view" convention.

## Code References

- `src/Homdutio.Api/Households/HouseholdEndpoints.cs:432-436` — `IsLastAdminLockedAsync` (UPDLOCK/HOLDLOCK count read)
- `src/Homdutio.Api/Households/HouseholdEndpoints.cs:285-359` — demote endpoint + transaction (`:329-341`)
- `src/Homdutio.Api/Households/HouseholdEndpoints.cs:365-420` — remove endpoint + transaction (`:396-407`), `SweepAndRemoveAsync`
- `src/Homdutio.Data/Entities/HouseholdMember.cs:8-23` — member entity (no rowversion)
- `src/Homdutio.Data/Entities/HouseholdRole.cs:9-13` — `{ Admin, Member }`, string-persisted
- `src/Homdutio.Api/Tasks/TaskEndpoints.cs:202-242` — confirm (append `Confirmed`, set `ClosedAtUtc`)
- `src/Homdutio.Api/Tasks/TaskEndpoints.cs:244-284` — unclaim (append `Unclaimed`)
- `src/Homdutio.Api/Tasks/TaskEndpoints.cs:286-344` — send-back (append `SentBack` + comment)
- `src/Homdutio.Api/Tasks/TaskEndpoints.cs:404-429` — hard delete guarded to `ToDo` only
- `src/Homdutio.Api/Tasks/TaskEndpoints.cs:34-36` — board query filters `ClosedAtUtc == null`
- `src/Homdutio.Data/Entities/TaskEvent.cs:10-27` + `TaskEventType.cs` — append-only audit model
- `src/Homdutio.Data/Entities/HouseholdTask.cs:11-61` — task entity (no rowversion; `ClosedAtUtc` = closure flag)
- `src/Homdutio.Api/Program.cs:33-37` — `AddDbContext` (Scoped)
- `tests/Homdutio.Api.Tests/AuthApiFactory.cs:18-86` — per-class fresh LocalDB, migrations, disposal
- `tests/Homdutio.Api.Tests/HouseholdMemberAdminTests.cs:38-117` — register/household/seed/role/remove helpers; `:364-385` closest (serial) admin-removal test
- `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs:125-147` — full-history persistence after confirm; `:730-751` / `:807-836` — recovery appends asserted via `AnyAsync` only

## Architecture Insights

- **Two concurrency strategies coexist by design.** Household membership uses **pessimistic** locking (`UPDLOCK/HOLDLOCK` in a transaction) for the last-admin invariant; invites and refresh tokens use **optimistic** concurrency (`RowVersion.IsRowVersion()`). Tasks use **neither** — task-lifecycle guards rely on read-committed reads of the status field, which is why a concurrent double-claim is currently undefendable.
- **Closure-as-transition is a deliberate audit pattern (NFR-3 / S-05).** "Closed" is `ClosedAtUtc != null`, not a status; the row and its full event chain persist forever and are filtered off the board rather than deleted. Send-back deliberately preserves the original claimer.
- **Dual-write of the durable fact.** `self-attested` (and each transition) is written to both the projection row and the append-only event in a single `SaveChanges`, keeping the fast-read projection and the audit log consistent by construction.
- **Deterministic race proof.** Because the guard serializes via a held lock rather than retry-on-conflict, a two-request `Task.WhenAll` test yields a deterministic (one success / one conflict) outcome — the test asserts the **post-state invariant** (admin count ≥ 1), matching the §2 anti-pattern warning against asserting the guard message.

## Historical Context (from prior changes)

- `context/foundation/lessons.md` — "Guard min-count invariants with an atomic check-and-mutate" records the original TOCTOU on `IsLastAdminAsync`. Current code has **actioned** this lesson (renamed to `IsLastAdminLockedAsync`, wrapped in a locked transaction). Phase 3's job is to add the proving test the lesson implies.
- `context/foundation/test-plan.md` §2 — Risk #3 (impact High / likelihood Medium) and Risk #5 (High / Low-Medium), with the "Must challenge" / "Anti-pattern to avoid" columns that shape the assertions above.
- `test-plan.md` §6.6 Phase 2 note (`:199`) — explicitly deferred the concurrent double-claim race to "§3 Phase 3 (Risk #3)" because `HouseholdTask` has no rowversion. This research confirms the entity still has none, surfacing the scope question below.
- `test-plan.md` §6.1 — established conventions this phase inherits: `IClassFixture<AuthApiFactory>`, per-file helpers, LocalDB, build own isolated state (unique emails/households) because the fixture is shared per class.

## Related Research

- `context/archive/testing-cross-household-isolation/` (Phase 1) and `context/changes/testing-lifecycle-guard-completeness/` (Phase 2) — sibling rollout phases on the same integration layer/fixture; their reference tests (`HouseholdIsolationTests.cs`, `TaskEndpointsTests.cs`) are the pattern source for Phase 3.

## Open Questions

1. **Is concurrent double-claim in Phase 3 scope?** Risk #3 as written is strictly the zero-admin race (already guarded — needs only a proving test). The Phase 2 note parked double-claim here, but observing/defending it requires **adding a rowversion to `HouseholdTask`** — a production change beyond a test phase. The plan must decide: (a) scope Phase 3 to proving the two already-defended invariants (admin race + audit drops), and re-park double-claim as its own hardening slice; or (b) expand to add the concurrency token + its proving test. Recommendation leans (a) — keep the test phase test-only, consistent with the lesson boundaries.
2. **Demote×remove cross-endpoint race, or remove×remove?** Both endpoints share `IsLastAdminLockedAsync`. The fullest proof drives one demote + one remove on two different admins concurrently; a remove×remove pair is the simpler representative. Plan to pick coverage vs cost.
3. **Cookbook §6.2 / §6.3 are still `TBD — see §3 Phase 3`.** This phase's plan must end with the sub-phase that fills them in (the parallel-request pattern for §6.2; the "query the durable record, assert full history preserved" pattern for §6.3), per the rollout's cookbook-growth rule.
