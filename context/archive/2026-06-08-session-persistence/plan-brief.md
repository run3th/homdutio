# Session Persistence (Refresh-Token Flow) — Plan Brief

> Full plan: `context/changes/session-persistence/plan.md`

## What & Why

Today a full page reload logs the user out — the access token lives only in an in-memory signal, so
a refresh, a reopened tab, or a browser restart bounces them to `/login`. This slice (roadmap S-10)
adds a persisted **refresh token** that silently re-mints a short-lived access token, so sessions
survive without re-entering the password — while keeping the access token in memory.

## Starting Point

Backend is stateless JWT bearer (`AuthEndpoints.cs`, `JwtTokenService.cs`) with a 120-min access
token and **no** refresh, revocation, or server-side token store. The SPA `AuthService` keeps the
token in memory only (`auth.service.ts:34`), the guard reads it synchronously (`auth.guard.ts:14`),
and there is no startup auth hook. Migrations are additive; tests are xUnit integration +
vitest.

## Desired End State

A user can reload, reopen a tab, or restart the browser and stay on `/board` authenticated. The
access token is ~15 min and re-minted transparently (no mid-session bounce). Refresh tokens rotate on
every use with replay detection, logout is real server-side revocation, multiple devices stay
independent, and an expired/absent token lands cleanly on `/login` with no redirect loop.

## Key Decisions Made

| Decision                  | Choice                                   | Why (1 sentence)                                                                 | Source |
| ------------------------- | ---------------------------------------- | -------------------------------------------------------------------------------- | ------ |
| Refresh-token transport   | **`localStorage`, body-transported**     | User chose simplicity over the roadmap's cookie lean; no cookie ⇒ no CSRF.        | Plan   |
| XSS mitigation            | Short lifetime + rotation/replay         | Makes web-storage exposure single-use and detectable rather than open-ended.     | Plan   |
| Storage location          | `localStorage` (not `sessionStorage`)    | Must survive tab close + restart to meet the S-10 outcome.                        | Plan   |
| Rotation                  | Rotate-on-use + replay → family revoke   | A reused (stolen) token is single-use and trips revocation of the whole chain.   | Plan   |
| Access-token lifetime     | 120 min → ~15 min                        | A leaked access token expires fast; the refresh flow hides the short TTL.         | Plan   |
| Server store              | Hashed token, new `RefreshToken` table   | Enables revocation + replay detection; a DB leak exposes no usable tokens.        | Plan   |
| Startup restore           | Blocking `APP_INITIALIZER`               | Runs before the synchronous guard, so no `/login` flash and the guard is untouched.| Plan  |
| Mid-session 401           | Silent refresh + retry once (loop-guarded)| Keeps the 15-min TTL invisible; one retry max prevents refresh loops.            | Plan   |
| Logout                    | Server revoke (family) + client clear    | A copied refresh token can't be reused after logout.                             | Plan   |
| Sessions                  | Multiple per user                        | Phone + desktop stay independent — natural for a shared-household tool.           | Plan   |
| Testing                   | Backend integration + key SPA units      | Covers security-critical server logic + trickiest client lifecycle; no e2e dep.   | Plan   |

## Scope

**In scope:** `RefreshToken` entity + additive migration; `RefreshTokenService` (issue/rotate/replay/
revoke); `refresh` + `logout` endpoints; shortened access TTL; login issues a refresh token; SPA
persistence, blocking startup restore, 401 refresh-and-retry, server-side logout; backend integration
+ SPA unit tests.

**Out of scope:** httpOnly cookie / CSRF machinery; `sessionStorage`; e2e/browser harness; password
reset/email (S-08); a "revoke all sessions" management UI; expired-row cleanup job (noted for ops).

## Architecture / Approach

Refresh token = high-entropy random string; server stores only its SHA-256 hash plus a `FamilyId`
(rotation lineage). Login issues access + refresh (new family). `refresh` validates the presented
token's hash, consumes it (single-winner via a `RowVersion` conditional update, mirroring
`HouseholdInvite`), and issues a new pair in the same family. Re-presenting a consumed token = replay
→ the whole family is revoked. SPA stores the current refresh token in `localStorage`, refreshes at
startup (blocking the router) and on protected 401s (retry-once), and revokes on logout.

## Phases at a Glance

| Phase                                   | What it delivers                                              | Key risk                                              |
| --------------------------------------- | ------------------------------------------------------------ | ----------------------------------------------------- |
| 1. Backend store & issuance             | `RefreshToken` table + service; login issues a refresh token; access TTL → 15 min | DI lifetime (scoped service vs singleton JWT)         |
| 2. Backend refresh/logout & replay      | `refresh` + `logout` endpoints; rotation, replay→family revoke, expiry | Rotation race / single-winner consume correctness     |
| 3. SPA persist, restore & interceptors  | `localStorage` persistence, blocking startup restore, 401 refresh-and-retry, server logout | Redirect loops; startup bootstrap hang on slow API    |

**Prerequisites:** S-01 (done) — builds on the existing SPA auth layer and F-02 token pipeline.
**Estimated effort:** ~3 sessions, one per phase.

## Open Risks & Assumptions

- **Accepted XSS tradeoff (on record):** `localStorage` reintroduces the exposure F-02/the roadmap
  designed around. Mitigated — not eliminated — by short lifetime + rotation/replay. This contradicts
  the roadmap's cookie lean and was an explicit user decision.
- Rotation race on rapid double-refresh must resolve to a single winner without falsely revoking a
  family on the first legitimate rotation.
- Blocking startup restore must time out cleanly so a slow/down API never hangs bootstrap.
- Expired refresh-token rows accumulate; a cleanup job is the natural follow-up (out of scope here).

## Success Criteria (Summary)

- Reload / reopened tab / browser restart resume the session — no re-login.
- A mid-session access-token expiry refreshes silently; no `/login` bounce while the refresh token is
  valid.
- Logout revokes server-side; a reload afterward lands cleanly on `/login` with no redirect loop.
