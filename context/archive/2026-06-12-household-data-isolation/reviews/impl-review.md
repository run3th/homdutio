<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Household Data Isolation (S-07)

- **Plan**: context/changes/household-data-isolation/plan.md
- **Scope**: All 3 phases
- **Date**: 2026-06-12
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warning, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | WARNING |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Diff scope exactly matches the plan: 5 source/test files + the 3 change-folder docs, no unplanned
source changes. The Phase 2 refactor is pure mechanical substitution (`Resolve*`/`LoadScopedTask` →
`HouseholdScope.*` + dead-code deletion), zero branch logic touched. Build clean (0 warnings / 0
errors), 102 API + 1 data tests green, convention-guard teeth demonstrated (dummy route → fail → reverted).

## Findings

### F1 — Convention guard is bounded to two hardcoded route prefixes

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architecture
- **Location**: tests/Homdutio.Api.Tests/RouteIsolationCoverageTests.cs (DiscoverDomainRoutes)
- **Detail**: The guard originally only scanned routes under `/api/tasks` or `/api/households`. A future
  household-scoped endpoint registered under a NEW prefix would be silently skipped — the exact regression
  class the guard exists to catch. Correct given only two domain prefixes exist today; a future-proofing
  blind spot in the backstop.
- **Fix A ⭐ Recommended**: Invert the filter — scan all `/api/*` routes minus an exempt-PREFIX allowlist
  (`/api/auth`).
  - Strength: A household-scoped route under any new prefix surfaces as uncategorized and fails the build,
    closing the blind spot. Low effort.
  - Tradeoff: Must maintain a non-domain exempt-prefix list; a genuinely new non-household /api area needs
    one line added.
  - Confidence: MED — straightforward; auth route inventory not exhaustively re-verified before the change.
  - Blind spot: Auth route inventory.
- **Fix B**: Leave as-is; revisit when a third domain prefix is introduced.
  - Strength: Zero change; matches the plan's literal scope.
  - Tradeoff: Backstop silently misses a new-prefix endpoint until someone widens it.
  - Confidence: HIGH — no code change.
  - Blind spot: Relies on future authors recalling the limitation.
- **Decision**: FIXED via Fix A — `DiscoverDomainRoutes` now scans all `/api/*` minus
  `ExemptPrefixes = { "/api/auth" }`; both convention tests + full suite green (102 API + 1 data).

### F2 — Helper placed at project root, not the plan's suggested Households/ folder

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: src/Homdutio.Api/HouseholdScope.cs
- **Detail**: Plan named `src/Homdutio.Api/Households/HouseholdScope.cs` "or a shared location both endpoint
  namespaces can reference". Placed at project root (namespace `Homdutio.Api`) so both Tasks and Households
  sub-namespaces resolve it via parent-namespace lookup with no `using`. Within the plan's explicit allowance.
- **Fix**: Optionally relocate to `Households/`. Not needed.
- **Decision**: ACCEPTED as-is — plan-compliant; root namespace is the cleaner cross-cutting placement.

### F3 — Test helpers duplicated across a 5th test file

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: tests/Homdutio.Api.Tests/HouseholdIsolationTests.cs (helpers block)
- **Detail**: `RegisterAndLoginAsync` / `Authed` / `CreateHouseholdAsync` / `SeedMemberAsync` / `NewEmail`
  copied again, matching the existing no-base-class convention the plan explicitly followed. With 5 files now
  repeating them, a shared test fixture base would DRY it up — a cross-cutting test refactor outside S-07.
- **Fix**: Optionally extract a shared test fixture base (separate cleanup).
- **Decision**: ACCEPTED as-is — consistent with current convention; future cleanup candidate.
