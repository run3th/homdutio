# Concurrency & Audit Durability — Plan Brief

> Full plan: `context/changes/testing-concurrency-audit-durability/plan.md`
> Research: `context/changes/testing-concurrency-audit-durability/research.md`

## What & Why

Phase 3 of the test-plan rollout. Prove — via new integration tests — that two already-shipped defenses actually hold: the last-admin guard cannot be raced to zero admins (Risk #3), and task recovery transitions never drop prior audit history (Risk #5). Research confirmed both are defended in current code, so this is a **proving/verification** phase, not a fix.

## Starting Point

The last-admin guard is already atomic (`IsLastAdminLockedAsync` reads the admin count under SQL `UPDLOCK, HOLDLOCK` inside the demote/remove transaction), and all recovery transitions already append `TaskEvent` rows in the same `SaveChanges`. But every admin test is serial (no `Task.WhenAll` anywhere in the suite), and the unclaim/send-back tests assert only that the *new* event exists — never that the *prior* history survived. So the defenses are unproven against the exact regressions the risks describe.

## Desired End State

`dotnet test tests/Homdutio.Api.Tests` includes a concurrent demote × remove test that fails if the last-admin lock is ever weakened, and drop-detection tests that fail if any recovery transition drops or overwrites a prior audit event. Cookbook §6.2/§6.3 are filled, §3 Phase 3 is marked `complete`, and the concurrent double-claim seam is re-parked with a durable in-code pointer.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Double-claim scope | Test-only; re-park it | Observing/defending it needs a `HouseholdTask` rowversion — a production change beyond a test phase. | Research + Plan |
| Admin-race breadth | Cross-endpoint (demote × remove) | Exercises both guarded paths that share the lock — fullest proof for barely more code. | Plan |
| Audit drop-detection depth | Full-history + compound sequence | Only asserting prior events survive (and accumulate across repeated transitions) detects the literal "drops/overwrites" risk. | Plan |
| Race assertion oracle | Post-state invariant + response codes | The risk map names "assert the guard message" as the anti-pattern; the invariant (count == 1) is the real proof. | Research + Plan |
| Test placement | Extend existing files | Per-file, no-shared-base-class convention: admin race → `HouseholdMemberAdminTests.cs`, audit → `TaskEndpointsTests.cs`. | Research |

## Scope

**In scope:** concurrent last-admin race test; unclaim/send-back/compound audit drop-detection tests; the suite's first parallel-request helper; cookbook §6.2/§6.3; §3 row + §6.6 closeout; double-claim re-park pointer.

**Out of scope:** any production (`src/`) change; a `HouseholdTask` rowversion + concurrent double-claim test; new test infra; e2e / CI gate wiring (§3 Phase 5).

## Architecture / Approach

Two phases on the existing xUnit + `WebApplicationFactory` + LocalDB layer. Each concurrent HTTP request gets its own `Scoped` DbContext/connection, so the race is genuinely observable; because the guard serializes via a *held* lock, the proving test is deterministic (one 200 / one conflict) — no iteration or barrier. Durable-record assertions read from a fresh DI scope, never the request's cached context.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Last-admin race | Concurrent demote × remove test + parallel helper + §6.2 + double-claim re-park | Test must observe the race, not false-pass — mitigated by asserting the post-state count invariant |
| 2. Audit durability | Prior-history-survives + compound-sequence tests + §6.3 + rollout closeout | Assertions must bite on a real drop — verified by a local overwrite experiment |

**Prerequisites:** LocalDB (`(localdb)\MSSQLLocalDB`) available; existing suite green.
**Estimated effort:** ~1 session across 2 phases.

## Open Risks & Assumptions

- Assumes the last-admin rejection status returned by the handlers is the oracle for the secondary assertion — derive it from handler behavior, not intuition (Phase 2 guard-ordering discipline).
- Assumes LocalDB read-committed isolation lets two scoped connections observe the race; if a future host serializes connections, the test would need review.

## Success Criteria (Summary)

- A concurrent admin-mutation test proves the household never reaches zero admins.
- Recovery transitions provably preserve full, ordered audit history across single and compound sequences.
- Cookbook §6.2/§6.3 filled; §3 Phase 3 `complete`; double-claim re-parked.
