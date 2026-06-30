<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Emailed Invite Links + email templating

- **Plan**: context/changes/redesign-tags-and-email-invites/plan.md
- **Scope**: Phase 7 of 7
- **Date**: 2026-06-30
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 2 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Invite-email path has no rate limit (plan asked to keep it consistent)

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence / Safety & Quality
- **Location**: src/Homdutio.Api/Households/HouseholdEndpoints.cs:82
- **Detail**: Phase 7 step 4 contract says "Keep rate-limiting consistent with other email endpoints." forgot-password (AuthEndpoints.cs:132) is rate-limited; the invite endpoint sends to a caller-supplied RecipientEmail with no throttle — an authenticated member can email unlimited invites to arbitrary third-party addresses (outbound email-bombing / ACS quota burn).
- **Fix A ⭐ Recommended**: Add a rate-limit policy to the invite endpoint, partitioned by user id.
  - Strength: Closes the abuse vector; honors the plan contract; mirrors ForgotPassword policy shape.
  - Tradeoff: New policy + Program.cs wiring; threshold must not trip existing invite tests.
  - Confidence: MED — pattern exists, tuning needs care.
  - Blind spot: Unsure if RateLimitPolicies has a per-user partition helper.
- **Fix B**: Document the deviation as accepted risk in the plan.
  - Strength: Minimal change; authenticated-only bounds blast radius.
  - Tradeoff: Leaves the email-bombing vector open; contradicts plan intent.
  - Confidence: MED — depends on threat model.
  - Blind spot: No registration verification makes abuse cheap.
- **Decision**: FIXED via Fix A — added `RateLimitPolicies.Invite` + `InviteRateLimitOptions` (10/15min, partitioned by sub), `.RequireRateLimiting` on the invite endpoint, appsettings entry, and `InviteRateLimitTests`.

### F2 — Recipient email not validated or normalized server-side

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Homdutio.Api/Households/HouseholdEndpoints.cs:116
- **Detail**: Server only trims + non-blank-checks RecipientEmail; format validation is client-side only. A direct API caller can pass garbage, attempted as an ACS send (swallowed on failure). Wastes a send; trusts client for a server concern.
- **Fix**: Add a lightweight server-side format guard (MailAddress parse / `@` check) returning a 400 in the mapValidationProblem shape before minting.
- **Decision**: FIXED — `MailAddress.TryCreate` guard before minting, returns 400 ValidationProblem; covered by `Generate_with_malformed_recipient_email_returns_400_and_mints_nothing`.

### F3 — Emailing mints a separate token from the copy-link box

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architecture / Pattern Consistency
- **Location**: web/src/app/shell/topbar/invite-dialog.component.ts:60
- **Detail**: Dialog mints one token on open (copy box) and a second on Send (email). Both are valid single-use invites; a user who copies AND emails creates two invite rows. Direct consequence of the plan's deliberate "No new public endpoint" choice.
- **Fix**: None recommended — matches the plan's explicit design.
- **Decision**: SKIPPED (acknowledged — accepted as plan design).

### F4 — Plain-text email bodies are hand-authored, not templated

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/Homdutio.Api/Email/AcsEmailSender.cs:62
- **Detail**: HTML bodies render from embedded templates; plain-text fallbacks are inline C# strings. Acceptable (templates are HTML-only; preserves the reset-email plain-text test) but the two bodies for one message live in two places.
- **Fix**: None recommended — acceptable split.
- **Decision**: SKIPPED (acknowledged — accepted split).
