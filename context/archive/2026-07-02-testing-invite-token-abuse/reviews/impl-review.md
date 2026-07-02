<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Invite/token abuse — integration abuse tests (Phase 4)

- **Plan**: context/changes/testing-invite-token-abuse/plan.md
- **Scope**: Phase 1 of 1 (full plan)
- **Date**: 2026-07-02
- **Verdict**: APPROVED
- **Findings**: 0 critical, 0 warnings, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Third helper (`UserIdByEmailAsync`) added beyond the plan's "two private helpers"

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: tests/Homdutio.Api.Tests/HouseholdInviteEndpointsTests.cs:139
- **Detail**: The plan (§Changes Required #1) named exactly two new private helpers — `SendConcurrentlyAsync` and `LoadInviteRowAsync`. The implementation added a third, `UserIdByEmailAsync`, to resolve a caller's `Id` from their email so the double-consume test can assert `row.ConsumedById` is one of the two racers. This is a benign, necessary supporting helper (the oracle needs the user id, which the test only knows by email); it introduces no production surface and follows the same fresh-scope pattern as the two planned helpers. Called out only for completeness — not a defect.
- **Fix**: None needed. Optionally note the extra helper in the plan's Changes Required as an addendum for traceability.
- **Decision**: PENDING

## Evidence

- **Git scope**: `8f1eb1b` (test) + `52c4e1a` (epilogue). Diff = +120 lines to `HouseholdInviteEndpointsTests.cs` plus the change folder. No production code touched — matches the plan's "tests-only" guardrail exactly.
- **Plan adherence**: all three tests implemented per contract —
  - `Concurrent_double_consume_of_one_token_creates_exactly_one_membership` — primary oracle is fresh-scope member count (== 2, admin + one racer) + `ConsumedById ∈ {first, second}` + `ConsumedAtUtc != null`; secondary is the unordered status set {200, 410}. Does not assert which caller won. ✓
  - `Same_user_concurrently_accepting_two_tokens_lands_in_exactly_one_household` — primary oracle is fresh-scope membership count (== 1); secondary {200, 409}. ✓
  - `Consumed_token_returns_410_on_anonymous_preview` — anonymous GET → 410 Gone, assertion not softened; leak documented as escalate-not-soften. ✓
- **Success criteria (re-run live 2026-07-02)**: `dotnet build` → 0 warnings, 0 errors. `dotnet test --filter HouseholdInviteEndpointsTests` → 18 passed, 0 failed. Full-suite green was recorded at commit time.
- **Pattern consistency**: `LoadInviteRowAsync` mirrors `LoadTaskRowAsync` (fresh scope, `AsNoTracking`); `SendConcurrentlyAsync` copied per-file from `HouseholdMemberAdminTests` per the suite's no-shared-base convention — both consistent with the codebase.
- **Lessons prior**: consistent with `lessons.md` "atomic check-and-mutate for min-count invariants" — these tests verify the rowversion single-use consume, the sibling of that rule.
