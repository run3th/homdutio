<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Household and Board (S-02)

- **Plan**: context/changes/household-and-board/plan.md
- **Mode**: Deep
- **Date**: 2026-06-01
- **Verdict**: REVISE (all findings fixed in-plan)
- **Findings**: 0 critical, 1 warning, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING |
| Plan Completeness | WARNING |

## Grounding

7/7 paths ✓ · `sub` claim confirmed = `user.Id` (JwtTokenService.cs:27), so the server-derived
`sub → HouseholdMember.UserId` scoping chain holds ✓ · brief↔plan consistent ✓ · Progress↔Phase
consistent (1.1–1.7, 2.x, 3.x) ✓ · no `docs/reference/contract-surfaces.md` (check skipped).

## Findings

### F1 — Login redirect change breaks an existing spec not in the plan

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 2 §4 + Phase 2 Automated criteria
- **Detail**: Phase 2 §4 retargets `login.component.ts:61` from `navigate(['/home'])` to `/board`, but `login.component.spec.ts:35,43` asserts `['/home']` and is named "navigates to /home on a successful login". Phase 2 criterion 2.3 ("existing vitest specs pass") would fail until that spec is updated, yet no change item listed it.
- **Fix**: Added `login.component.spec.ts` to Phase 2 §4 — update the assertion and test name to `/board`.
- **Decision**: FIXED

### F2 — `npm run lint` referenced but no lint script exists

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 2 (2.2) + Phase 3 (3.2) Automated criteria
- **Detail**: `web/package.json` scripts are ng/start/build/watch/test only — no `lint` script. The "(if configured)" hedge still risks `/10x-implement` running it verbatim and erroring.
- **Fix**: Removed the lint criteria from both phases and the Progress section; renumbered 2.x and 3.x.
- **Decision**: FIXED

### F3 — `_loaded` flag must reset on logout, not just `_household`

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 2 §1 (HouseholdService.clearOnLogout)
- **Detail**: If `clearOnLogout()` resets `_household` but not `_loaded`, a second user on the same page load is treated as "loaded, no household" and mis-routed to `/create-household` (the guard never refetches).
- **Fix**: §1 contract now states `clearOnLogout()` MUST reset both `_household` and `_loaded`, with the failure mode spelled out.
- **Decision**: FIXED

### F4 — GET /me empty result left as "404 (or 204)"

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 §4, §6 test (f), Critical Implementation Details, Phase 2 §1
- **Detail**: The no-household response was specified as "404 (or 204)" in several places, leaving the status undecided for the implementer and the integration test.
- **Fix**: Settled on `204 No Content` consistently across the endpoint contract, the §6 test (f), the guard's Critical Implementation Details note, and `loadMine`'s mapping.
- **Decision**: FIXED
