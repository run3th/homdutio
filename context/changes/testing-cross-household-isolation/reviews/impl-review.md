<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Cross-household isolation hardening

- **Plan**: context/changes/testing-cross-household-isolation/plan.md
- **Scope**: Full plan (Phases 1–3 of 3)
- **Date**: 2026-06-29
- **Verdict**: APPROVED
- **Findings**: 0 critical, 2 warnings, 1 observation (all triaged → FIXED)

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

Plan intent fully met and strengthened: inventory is exactly 14 routes (11 ParityNotFound / 2 OwnOnlyCollection / 1 MixedBatchRejected); the loop-count assertion locks Gap #2; parity extended from 7 → 11 routes (Gap #1); `/api/auth` blind spot documented at source + §6.1 (Gap #3). No scope creep, no production code touched. Success criteria reverified: `dotnet build` 0 warnings, 104 tests pass, §6.1 has no `TBD`, §3 Phase 1 = `complete`. Phase 2 tripwires were genuinely executed (red → revert → green), not rubber-stamped.

## Findings

### F1 — Admin-gated routes' 404 parity silently depends on House B being an admin

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: ScopedRouteInventory.cs (role + DELETE-member entries)
- **Detail**: For 9 of 11 ParityNotFound routes the scoped lookup runs first (404 regardless of role). For `POST /members/{userId}/role` and `DELETE /members/{userId}`, the handler's admin gate (403) precedes the scoped target lookup (404), so foreign-id parity holds only because House B's sweep caller is an admin of B (BuildHouseBAsync creates the household). Correct today and fails loud (403≠404) if changed — no false-green — but the precondition was undocumented.
- **Fix**: Added a NOTE comment on the two member-admin inventory entries documenting that their 404 parity relies on House B's caller being a household admin (admin gate precedes the scoped lookup).
- **Decision**: FIXED (Fix now)

### F2 — Inventory enum doc claims "fails to compile"; it's a runtime throw

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: ScopedRouteInventory.cs:23-25 (Behavior enum XML doc)
- **Detail**: The enum doc said a new Behavior value "fails to compile." It does not — the sweep uses a switch expression with a `_ => throw` discard arm, so a new value compiles and throws at runtime (caught by the sweep → red). The sweep's own inline comment described this accurately; the enum doc contradicted it. Both review agents flagged it independently.
- **Fix**: Corrected the enum XML doc to describe the runtime backstop (a new value falls through the discard arm and turns the sweep red; not a compile error, since the project does not treat warnings as errors).
- **Decision**: FIXED (Fix now)

### F3 — Sweep try/catch can't distinguish an isolation failure from an infra throw

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: HouseholdIsolationTests.cs:89-92
- **Detail**: The per-route catch(Exception) aggregates failures (intended, per plan). But it also catches an HttpRequestException from setup or a wiring throw and reports them indistinguishably from a real isolation-assertion failure. Diagnostic-only — everything still goes red; no correctness impact.
- **Fix**: Prefixed the aggregated failure message with `ex.GetType().Name` so triage can tell a XunitException (assertion failure) from an infrastructure/wiring throw.
- **Decision**: FIXED (Fix now)
