<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Member Administration

- **Plan**: context/changes/member-administration/plan.md
- **Scope**: Phases 1–2 of 2 (full plan)
- **Date**: 2026-06-12
- **Verdict**: NEEDS ATTENTION (at review time) → all findings triaged & resolved
- **Findings**: 0 critical, 1 warning, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING (benign/approved — F3) |
| Safety & Quality | WARNING (F1 warning + F2 observation) |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS (91→93 BE tests, 143 FE tests, lint clean, no migration) |

## Findings

### F1 — Role endpoint accepts undefined enum values

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Homdutio.Api/Households/HouseholdEndpoints.cs (role endpoint, `Enum.TryParse`)
- **Detail**: `Enum.TryParse<HouseholdRole>("5")` returns true with value `(HouseholdRole)5` (verified), so an admin POSTing `{ "role": "5" }` would persist an out-of-range role stored as the string "5". Not privilege escalation, but a data-integrity hole.
- **Fix**: Added `&& Enum.IsDefined(newRole)` to the parse guard; invalid → existing ValidationProblem. Locked with a new test (`Role_change_with_an_undefined_role_value_returns_400`).
- **Decision**: FIXED

### F2 — Last-admin guard has a TOCTOU race under concurrent demote/remove

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Homdutio.Api/Households/HouseholdEndpoints.cs (`IsLastAdminAsync` + demote/remove)
- **Detail**: The admin-count read and the role/remove mutation weren't atomic. Two concurrent requests demoting/removing *different* admins in a 2-admin household both read count = 2, both pass the `<= 1` guard → zero admins. (The guard is otherwise unreachable in single-action flow; this race was the only path that exercised it.)
- **Fix**: Replaced `IsLastAdminAsync` with `IsLastAdminLockedAsync` — counts admins via a parameterized `SELECT ... WITH (UPDLOCK, HOLDLOCK)` inside a transaction that then performs the demote/remove, so a concurrent admin mutation blocks on the lock and re-reads the post-mutation count (deadlock-free). The transaction runs through `CreateExecutionStrategy()` (the provider has retry-on-failure enabled). Added `Admin_can_remove_another_admin_while_one_remains` to cover the transactional path.
- **Decision**: FIXED + ACCEPTED-AS-RULE — "Guard min-count invariants with an atomic check-and-mutate" appended to `context/foundation/lessons.md`.

### F3 — Toolchain + spec fix landed beyond the plan's Changes Required

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: web/eslint.config.js, web/angular.json, web/package*.json, web/src/app/board/task-detail/task-detail.component.spec.ts
- **Detail**: ESLint setup + the task-detail spec edit weren't in the plan's "Changes Required" but landed (package-lock grew ~1900 lines). Both surfaced and approved mid-run: ESLint fulfills criterion 2.3; the spec fix unblocked 2.2 (pre-existing breakage from 26a676c). Documented in the p2 commit.
- **Fix**: None — accept as approved-and-documented scope.
- **Decision**: ACCEPTED (no action)
