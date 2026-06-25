<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Password Reset Implementation Plan

- **Plan**: context/changes/password-reset/plan.md
- **Scope**: Full plan (Phases 1–3)
- **Date**: 2026-06-25
- **Verdict**: APPROVED — both findings fixed
- **Findings**: 0 critical, 1 warning, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Drift agent: all 10 planned items MATCH; both Phase 2 review fixes intact; no SendGrid remnants; no scope creep. Safety agent: no client-side enumeration leaks, no SPA XSS, no committed secrets; navigation-state handoff and validator extraction correct. Success criteria: backend build + 110 tests, SPA build + lint + 152 vitest tests, all manual checks confirmed.

## Findings

### F1 — Reset link not HTML-encoded in the ACS email body

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Homdutio.Api/Email/AcsEmailSender.cs (BuildResetMessage HTML body)
- **Detail**: The reset link was interpolated raw into the href attribute and link text of the HTML body. Not reachable via the current caller (Base64Url token + Uri.EscapeDataString'd email), but BuildResetMessage is a public static reusable primitive whose safety rested on a caller-side invariant it didn't enforce.
- **Fix**: HTML-encode the link with `WebUtility.HtmlEncode` for both the href and the text (orthogonal to URL-encoding — defense in depth). Updated AcsEmailSenderTests to assert the encoded link in HTML and the raw link in plain text.
- **Decision**: FIXED

### F2 — AcsEmailOptions doc says Endpoint "not committed"; it is

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision
- **Dimension**: Pattern Consistency
- **Location**: src/Homdutio.Api/Email/AcsEmailOptions.cs (XML doc)
- **Detail**: The doc said Endpoint + SenderAddress are "set per environment rather than committed," but appsettings.json commits both (non-secret) and appsettings.Development.json blanks Endpoint to force the no-op sender locally. The arrangement is intentional; only the comment was stale.
- **Fix**: Updated the doc to state the non-secret values are committed in appsettings.json and blanked in appsettings.Development.json (with App Service override for other environments).
- **Decision**: FIXED
