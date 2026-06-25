<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Password Reset Implementation Plan

- **Plan**: context/changes/password-reset/plan.md
- **Scope**: Phase 2 of 3 (Backend Reset Endpoints)
- **Date**: 2026-06-25
- **Verdict**: NEEDS ATTENTION → all findings triaged (4 fixed, 1 accepted)
- **Findings**: 0 critical, 1 warning, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Plan-drift agent: all four planned changes MATCH; every Critical Implementation Detail satisfied; no scope creep. Success criteria (build + 110 tests) green.

## Findings

### F1 — Email-send catch too narrow → 500 leaks account existence

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Homdutio.Api/Email/AcsEmailSender.cs:30-38
- **Detail**: SendPasswordResetAsync caught only RequestFailedException. A credential failure (DefaultAzureCredential) or transport fault would escape and 500 — but only for a known email (unknown emails never reach the send), an enumeration signal the always-200 design removes.
- **Fix**: Broadened the catch to `catch (Exception ex)` (log + return false) so every send failure preserves the generic 200.
- **Decision**: FIXED

### F2 — RevokeAllForUserAsync: load-then-loop race + materialization

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff
- **Dimension**: Safety & Quality (Reliability)
- **Location**: src/Homdutio.Api/Auth/RefreshTokenService.cs:127-145
- **Detail**: Loaded all live rows then stamped them in a loop with no DbUpdateConcurrencyException guard; a refresh racing the reset between ToListAsync and SaveChangesAsync could throw on RowVersion → 500 after the password already changed. Echoes the lessons.md atomic check-and-mutate rule.
- **Fix A (chosen)**: Replaced with set-based `ExecuteUpdateAsync` (SetProperty RevokedAtUtc, filtered UserId + RevokedAtUtc==null) — one atomic UPDATE, removes the race window and the materialization.
- **Fix B**: Wrap SaveChanges in catch(DbUpdateConcurrencyException) as benign.
- **Decision**: FIXED (Fix A)

### F3 — Token/policy discrimination via literal "InvalidToken"

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision
- **Dimension**: Safety & Quality
- **Location**: src/Homdutio.Api/Auth/AuthEndpoints.cs:166
- **Detail**: Distinguishing bad-token (generic) from weak-password (surfaced) relies on `e.Code == "InvalidToken"`. Correct today; fallback is enumeration-safe.
- **Fix**: Added a comment documenting the dependency on Identity's stable error-code contract and the safe fallback direction.
- **Decision**: FIXED

### F4 — Stale "ConnectionString" reference in AcsEmailSender doc

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision
- **Dimension**: Pattern Consistency
- **Location**: src/Homdutio.Api/Email/AcsEmailSender.cs:11-13
- **Detail**: Class XML doc said "Registered only when ConnectionString is configured"; selection is now by Endpoint (managed-identity switch).
- **Fix**: Updated the doc comment to reference `Endpoint` (auth by managed identity).
- **Decision**: FIXED

### F5 — Committed ACS Endpoint vs "no-op in local dev"

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — informational
- **Dimension**: Safety & Quality
- **Location**: src/Homdutio.Api/appsettings.json:17
- **Detail**: Agent flagged the committed non-empty Endpoint as routing local `dotnet run` to the live sender. Already mitigated: appsettings.Development.json blanks AcsEmail:Endpoint to "", so a default `dotnet run` (Development) selects NoOpEmailSender. The agent didn't read Development.json.
- **Decision**: ACCEPTED (no action — already handled)
