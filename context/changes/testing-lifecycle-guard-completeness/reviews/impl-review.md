<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Lifecycle Guard Completeness

- **Plan**: context/changes/testing-lifecycle-guard-completeness/plan.md
- **Scope**: Phases 1–3 of 3 (full plan)
- **Date**: 2026-06-30
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

### F1 — Test comments hard-code source line numbers that will drift

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: tests/Homdutio.Api.Tests/TaskEndpointsTests.cs:212, 231, 281, 308 (and cross-test refs :150, :246, :296, :319, :870)
- **Detail**: The new tests carry oracle-traceability comments citing exact guard line numbers (e.g. "TaskEndpoints.cs:217 role before :222 state", ":143", ":418-420") and sibling-test positions. These are deliberate and valuable — they let a future reader trace each asserted status back to the guard-ordering fact rather than re-derive it (the oracle discipline the plan demanded). The trade is that bare line numbers go stale when TaskEndpoints.cs shifts, and the in-file refs may already be approximate after the +160 lines this change inserted. Assertions remain correct regardless; only the navigational hints decay.
- **Fix**: Leave as-is. Accepted cost of oracle traceability in this plan (research itself uses file:line anchors); §6.1 now documents the guard-ordering rule durably by name. If a future TaskEndpoints.cs refactor invalidates them, prefer symbol/guard names over line numbers at that point.
- **Decision**: ACCEPTED — leave as-is (accepted cost of oracle traceability)

## Notes

A textbook execution. All six research gaps closed exactly as contracted; foreign-404 test exceeds the contract with an unknown-id parity anchor; gap 6 (member-removal) correctly NOT duplicated — verified existing `HouseholdMemberAdminTests.cs:332` already asserts the reverted row + `Unclaimed` event. "What We're NOT Doing" boundaries respected (concurrent race deferred + flagged in-code, Delete pinned not fixed, no production code). 143/143 tests green.
