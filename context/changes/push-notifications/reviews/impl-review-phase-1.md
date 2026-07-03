<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Per-Device Push Notifications & Admin Task Assignment

- **Plan**: context/changes/push-notifications/plan.md
- **Scope**: Phase 1 of 4
- **Date**: 2026-07-03
- **Verdict**: APPROVED
- **Findings**: 0 critical, 0 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Findings

### F1 — Member picker is a native `<select>`, plan described "assign chips"

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: web/src/app/board/create-task/create-task.component.html, web/src/app/board/task-detail/task-detail.component.html
- **Detail**: Plan §Phase 1 #4 says "person selector … mirroring the reference's assign chips" and "one chip per member". I implemented a native `<select>` with `<option>` per member ("Anyone" as the empty option). Functionally identical (single-select, "Anyone" = unassigned, `isSelf` → " (you)") and arguably better for accessibility (`getByLabel`/`getByRole` friendly, keyboard-native), but it is a presentation deviation from the plan's chip language. The reference mockup is explicitly read-only, so faithful visual parity was never a hard requirement here.
- **Fix**: Keep the `<select>` (recommended) — it meets the functional intent and is more accessible; or, if visual parity with the reference matters, swap to a chip-group later. No code change needed unless chip styling is desired.
- **Decision**: PENDING

### F2 — On edit+assign, a failed assign leaves the title/tag edit persisted

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (reliability)
- **Location**: web/src/app/board/task-detail/task-detail.component.ts:save()
- **Detail**: `save()` chains `update$ → switchMap(assign$)`. The two are separate HTTP calls, not one transaction. If `update` succeeds but `assign` fails — the realistic case is a concurrent claim between the board load and the assign making the task non-`ToDo`, so `/assign` returns 409 — the title/description/tag edit has already persisted while the assignment did not, and the user sees only the generic "Something went wrong" error. The board self-heals on the next poll (status is server-authoritative) and the edit itself is harmless, so blast radius is small; but the error copy doesn't tell the admin the *assignment* specifically failed.
- **Fix**: Acceptable as-is for Phase 1 (rare race, self-healing, non-destructive). Optional hardening later: surface the server's 409 message instead of the generic fallback so the admin learns the task was already claimed. Not worth blocking on now.
- **Decision**: PENDING

### F3 — Backend test project existed; plan assumed none (adaptation)

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: tests/Homdutio.Api.Tests/ScopedRouteInventory.cs
- **Detail**: Plan §Testing Strategy said "No backend test project exists yet (AGENTS.md); if one is added at `src/Homdutio.Api.Tests/`, cover…". A project *does* exist at `tests/Homdutio.Api.Tests/`, and `RouteIsolationCoverageTests` fails the build if a new household-scoped `/api/*` route isn't registered in `ScopedRouteInventory`. I adapted by registering `POST /api/tasks/{id}/assign` (IdShape.TaskId, Behavior.ParityNotFound, body factory) and updating the route-count doc comment. The isolation sweep (`HouseholdIsolationTests`) now drives the route from a foreign household automatically and confirmed the 404 existence-oracle parity — bonus coverage the plan's "cover: assign rejects non-admins / non-members / non-ToDo" wishlist partially gets for free. `dotnet test` is green.
- **Fix**: None needed — adaptation is complete and the build is green. Recorded so the plan-vs-reality delta is explicit.
- **Decision**: PENDING

## Notes

- Plan Adherence: all five planned changes (assign endpoint + `Assigned` event, optional assignee on create, `TaskService.assign`/`canAssign`/`assigneeId`, admin-only picker, `FlashService`) implemented as described. Server-side admin enforcement (the plan's load-bearing "Critical Implementation Detail") is present and mirrors `confirm`'s guard ordering (scoped load → admin gate → status → member validation) so a foreign/unknown id is a uniform 404.
- Scope Discipline: no "What We're NOT Doing" boundary crossed — no real Web Push, no `AssignedToId` column (reused `ClaimedById`), `templates/Homdutio Pro.html` left untouched/unstaged. The only unplanned edits (global `.field select` styling, `canAssign` added to 5 sibling spec factories, the route-inventory registration) are all mechanically required by the planned changes, not scope creep.
- Lessons: the one recorded lesson (atomic check-and-mutate for min-count invariants) does not apply — assignment touches no minimum-count invariant.
