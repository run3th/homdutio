<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Join-Household Flow + User Settings

- **Plan**: context/changes/join-flow-and-user-settings/plan.md
- **Scope**: All 3 phases (complete)
- **Date**: 2026-06-30
- **Verdict**: REJECTED at review time → **APPROVED after triage** (F1 + F2 fixed; F3 + F4 accepted as intentional/at-scale).
- **Findings**: 1 critical, 1 warning, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | FAIL |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Success criteria evidence: `dotnet build` clean; `npm run lint` clean; 36 backend endpoint tests pass (Profile/Invite/Auth filters); 25 Angular specs pass (join, user-avatar, settings-dialog, avatar-menu). Plan adherence: all 12 planned items MATCH, all 7 "not doing" constraints honored, no scope creep.

## Findings

### F1 — Public avatar endpoint serves unvalidated bytes without nosniff

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Homdutio.Api/Users/UserAvatarEndpoints.cs:29-50; src/Homdutio.Api/Profile/ProfileEndpoints.cs:72-79
- **Detail**: GET /api/users/{id}/avatar is AllowAnonymous, serves arbitrary user-uploaded bytes from the app origin, and echoes the *declared* content-type. Upload validation checks only the Content-Type header — it never inspects the actual bytes. A user can upload an HTML/JS payload labeled image/png; stored verbatim and served back. No X-Content-Type-Options: nosniff and no global security-headers middleware, so a browser can MIME-sniff and execute it → stored XSS on the origin via an anonymous URL.
- **Fix A ⭐ Recommended**: nosniff header on the serve response + magic-byte signature validation on upload (PNG \x89PNG, JPEG \xFF\xD8).
  - Strength: Closes both halves — header stops sniffing, byte validation stops mislabeled bytes ever being stored. Defense-in-depth; SVG already excluded by allowlist.
  - Tradeoff: ~10 lines across two files.
  - Confidence: HIGH — standard hardening for user-content serving.
  - Blind spot: None significant.
- **Fix B**: nosniff header only on the serve response.
  - Strength: One-line mitigation of the execution vector.
  - Tradeoff: Mislabeled bytes still stored; relies solely on the header.
  - Confidence: MED — blocks the common path but not the root cause.
  - Blind spot: Other consumers of the stored content-type.
- **Decision**: FIXED via Fix A — nosniff header added to the serve response (UserAvatarEndpoints.cs); magic-byte signature validation (HasMatchingImageSignature) added on upload (ProfileEndpoints.cs).

### F2 — Avatar version bump is a non-atomic read-modify-write

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (Reliability)
- **Location**: src/Homdutio.Api/Profile/ProfileEndpoints.cs:110-113, 129-132
- **Detail**: `user.AvatarVersion++` is computed in app memory from a same-request read, then written. Two concurrent uploads/deletes for the same user can both read version N and write N+1 — counter advances once instead of twice, one payload silently wins, served bytes and cache-busting URL drift apart. Self-only/cosmetic impact (stale cached image). Matches the project lesson "Guard … with an atomic check-and-mutate". Sibling entities (HouseholdInvite, RefreshToken) use IsRowVersion(); ApplicationUser does not.
- **Fix**: Bump atomically with ExecuteUpdateAsync (SET AvatarVersion = AvatarVersion + 1) in the same statement that writes the bytes.
- **Decision**: FIXED — both upload and delete now use ExecuteUpdateAsync with SetProperty(AvatarVersion, u => u.AvatarVersion + 1); upload re-reads the resulting version to build the URL. 10 ProfileEndpointsTests pass.

### F3 — Avatar serve buffers full byte[]; anonymous route unthrottled

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Performance)
- **Location**: src/Homdutio.Api/Users/UserAvatarEndpoints.cs:31-49
- **Detail**: Serving path materializes the entire varbinary(max) into memory per request, on an anonymous unthrottled route hit by the 4s poll. immutable cache header mitigates browser side; server still reads bytes on every cache-miss. DTO projections correctly select only HasAvatar + version (no byte bloat, no N+1). Acceptable at current scale given the 1 MiB cap.
- **Fix**: Acceptable as-is. If load grows, stream the column and/or add a light rate-limit to the anonymous route.
- **Decision**: ACCEPTED — acceptable at current scale; revisit only if load grows.

### F4 — Anonymous GUID fetch discloses whether a user has a photo

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Security)
- **Location**: src/Homdutio.Api/Users/UserAvatarEndpoints.cs:29
- **Detail**: Anyone with a user GUID can fetch the photo (404 vs 200 reveals "has photo"). IDs are non-enumerable GUIDs and already leak via DTOs (InviterId, member rows); the design doc-comment calls this deliberately public (mirrors invite preview). Low-sensitivity.
- **Fix**: No action — documented, intentional design.
- **Decision**: ACCEPTED — documented, deliberate public-by-GUID design.
