# Session Persistence (Refresh-Token Flow) Implementation Plan

## Overview

A logged-in user stays authenticated across a full page reload, a reopened tab, and a browser
restart — instead of being bounced to `/login`. The access token stays in an in-memory signal
(unchanged from today). On top of that we add a **refresh token**, persisted in `localStorage`,
that the SPA uses to silently re-mint a short-lived access token on startup and on access-token
expiry. The refresh token rotates on every use with replay detection, is stored server-side as a
hash so logout becomes real revocation, and the access-token lifetime shrinks from 120 min to
~15 min now that a transparent refresh path exists.

This un-defers the refresh/revocation work F-02 explicitly postponed and delivers roadmap slice
**S-10** (Access Control).

## Current State Analysis

- **Backend is stateless JWT, no refresh.** `JwtTokenService.CreateAccessToken`
  (`src/Homdutio.Api/Auth/JwtTokenService.cs:21`) mints an HS256 access token with
  `AccessTokenMinutes = 120` (`JwtOptions.cs:18`). Endpoints are `register` / `login` / `me` in
  `src/Homdutio.Api/Auth/AuthEndpoints.cs`. There is **no** refresh endpoint, no revocation, and no
  server-side token store. `Program.cs:53-71` validates bearer tokens with `ValidateLifetime = true`.
- **No refresh-token data model.** `ApplicationDbContext`
  (`src/Homdutio.Data/ApplicationDbContext.cs:13`) is an `IdentityDbContext<ApplicationUser>` plus the
  household/task/invite domain. Migrations are **additive** — the established pattern (e.g.
  `AddHouseholdInvites`). A new table is a clean additive migration.
- **SPA keeps the token in memory only.** `AuthService` (`web/src/app/auth/auth.service.ts:34`)
  holds the access token in a signal; its own doc comment states "A full page reload therefore logs
  the user out, which is accepted for v1." `authGuard` (`auth.guard.ts:11`) reads that signal
  **synchronously** and redirects to `/login` when empty.
- **No startup auth hook.** `app.config.ts:9` provides only the router and the two HTTP interceptors —
  there is no `APP_INITIALIZER` or bootstrap-time restore.
- **Interceptors today.** `bearerInterceptor` (`bearer.interceptor.ts:10`) attaches the access token;
  `unauthorizedInterceptor` (`unauthorized.interceptor.ts:19`) discards state and redirects to
  `/login` on any protected 401, excluding `/api/auth/login` + `/api/auth/register`.
- **Deployment is same-origin.** App Service serves the SPA (`wwwroot`) and the API from one host;
  the dev proxy maps `/api` → `localhost:5252`. (Relevant context only — we are **not** using a
  cookie, so origin/SameSite concerns do not arise.)
- **Test stack.** xUnit integration tests drive the real middleware via `WebApplicationFactory`
  (`tests/Homdutio.Api.Tests/AuthEndpointsTests.cs`); the SPA uses vitest.

## Desired End State

After this plan:

1. A user logs in, closes the tab, reopens the app — and lands on `/board` already authenticated,
   no password re-entry. The same holds for a hard reload and a browser restart.
2. The access token is short-lived (~15 min) and is re-minted transparently; the user never sees a
   mid-session bounce to `/login` while their refresh token is valid.
3. Each refresh returns a **new** refresh token and invalidates the old one. Replaying a consumed
   refresh token revokes the entire token family (all sessions descended from that login).
4. Logout revokes the server-side refresh token (and its family) so a copied token cannot be reused.
5. A user can be signed in on multiple devices independently (logging out one does not log out
   the others).
6. When the refresh token is expired/revoked/absent, the app lands cleanly on `/login` with **no**
   redirect loop.

**Verification:** the automated suites in each phase pass; manual reload/reopen/restart and
multi-device checks in the Testing Strategy confirm the user-visible outcome.

### Key Discoveries:

- The SPA guard is **synchronous** (`auth.guard.ts:14`), so the startup restore must complete
  *before* routing runs — a blocking `APP_INITIALIZER` is the fit, and it keeps the guard untouched.
- `unauthorizedInterceptor` already exempts the auth endpoints (`unauthorized.interceptor.ts:12`);
  the refresh endpoint must be added to that exemption so a failed refresh doesn't recurse.
- Login response is `LoginResponse(AccessToken, ExpiresAtUtc)` (`AuthEndpoints.cs:82`); it must grow
  a refresh-token field, and the SPA `LoginResponse` interface (`auth.service.ts:15`) mirrors it.
- `JwtTokenService` is a singleton (`Program.cs:74`); the new `RefreshTokenService` needs DB access,
  so it must be **scoped** (it depends on `ApplicationDbContext`).
- Migrations run migrate-first in CI before code swap (F-04) — an additive table is safe.

## What We're NOT Doing

- **No httpOnly cookie and no CSRF machinery.** Decision on record (contradicts the roadmap's cookie
  lean): the refresh token lives in `localStorage` and travels in the request body. CSRF does not
  apply to a body-transported token. The XSS tradeoff is accepted and mitigated by short lifetime +
  rotation/replay, not eliminated.
- **No `sessionStorage`** — `localStorage` is required to survive tab close / restart (the S-10
  outcome).
- **No e2e/browser harness** — the project has none; reload behavior is verified manually.
- **No password-reset, email, or account-confirmation work** — that is S-08.
- **No change to the `me` endpoint, household, or task flows.**
- **No admin "revoke all sessions" UI** — server-side family revocation exists, but no management
  surface ships here (could be a later S-09 adjunct).

## Implementation Approach

Build the server side first (data model → service → endpoints), then wire the SPA. The refresh
token is a high-entropy random string; the server stores only its SHA-256 hash plus rotation
lineage. On refresh, the server validates the presented token's hash, checks it is not consumed/
revoked/expired, marks it consumed, and issues a new (access, refresh) pair linked to the same
**family** id. If a token that is already consumed is presented again, that is a replay — the server
revokes the whole family and rejects. Logout marks the presented token's family revoked. The SPA
persists the current refresh token in `localStorage`, attempts a silent refresh at startup (blocking
the router via `APP_INITIALIZER`) and on any protected 401 (retry-once), and clears storage + calls
the server on logout.

## Critical Implementation Details

- **Service lifetime.** `RefreshTokenService` depends on `ApplicationDbContext`, so register it
  **scoped** — unlike the singleton `JwtTokenService`. Mixing a singleton that captures a scoped
  context is the classic DI footgun; keep token *minting* (stateless, singleton) separate from
  refresh *persistence* (scoped).
- **Rotation race.** Two near-simultaneous refreshes with the same token (e.g. a double-fired
  startup) can both pass the "not consumed" check. Treat the row's consumed-flag transition as the
  atomic point: use a conditional update (consume only `WHERE Consumed = 0`) and treat "0 rows
  affected" as already-consumed → reject without revoking the family on the very first rotation, OR
  accept the documented simplification that a genuine replay revokes the family. Plan: use the
  `RowVersion`/conditional-update pattern already proven on `HouseholdInvite`
  (`ApplicationDbContext.cs:108`) to make consume single-winner.
- **Startup timeout.** The blocking `APP_INITIALIZER` must not hang bootstrap if the API is slow/down
  — wrap the silent refresh in a bounded timeout that falls through to logged-out (render `/login`),
  never an infinite spinner.
- **401 loop guard.** The silent-refresh-and-retry in `unauthorizedInterceptor` must refresh **at
  most once** per original request and must exempt `/api/auth/refresh` itself, or a failing refresh
  recurses. Mark the retried request (e.g. a context token or a one-shot flag) so a second 401 on the
  replay falls straight through to logout+redirect.

## Phase 1: Backend — Refresh-Token Store & Issuance

### Overview

Introduce the persisted, hashed refresh-token model and make `login` issue a refresh token alongside
the access token. Shorten the access-token lifetime. No refresh/logout endpoints yet — this phase
establishes the store and issuance so Phase 2 can consume it.

### Changes Required:

#### 1. RefreshToken entity

**File**: `src/Homdutio.Data/Entities/RefreshToken.cs` (new)

**Intent**: Persist a refresh token server-side as a hash with the lineage needed for rotation,
replay detection, multi-session support, and revocation.

**Contract**: New entity with: `Id` (Guid/PK), `UserId` (FK to `AspNetUsers`, raw string column with
no navigation — matching the `HouseholdTask` convention at `ApplicationDbContext.cs:83`), `TokenHash`
(SHA-256 hex/base64 of the raw token), `FamilyId` (Guid — shared across a rotation chain from one
login), `ExpiresAtUtc`, `CreatedAtUtc`, `ConsumedAtUtc` (nullable — set when rotated), `RevokedAtUtc`
(nullable — set on logout/replay), and `RowVersion` (concurrency token for single-winner consume).

#### 2. DbContext mapping + migration

**File**: `src/Homdutio.Data/ApplicationDbContext.cs`, plus a new `Migrations/` entry

**Intent**: Register the `DbSet` and configure the table, then generate an additive migration.

**Contract**: Add `DbSet<RefreshToken> RefreshTokens`; configure in `OnModelCreating` — unique index
on `TokenHash` (lookup path), index on `FamilyId` (family revocation), index on `UserId` (cleanup /
per-user queries), `RowVersion` as `IsRowVersion()`. Generate `AddRefreshTokens` migration
(`dotnet ef migrations add AddRefreshTokens`). Additive only — no changes to existing tables.

#### 3. RefreshTokenService

**File**: `src/Homdutio.Api/Auth/RefreshTokenService.cs` (new), registered **scoped** in `Program.cs`

**Intent**: Own the lifecycle of refresh tokens: issue a new token (new family or continuing a
family), hash + persist, validate a presented token, rotate (consume + issue successor in same
family), detect replay, and revoke a family.

**Contract**: Methods roughly — `IssueAsync(userId, familyId?)` → returns the **raw** token string +
expiry and persists its hash; `ValidateAndRotateAsync(rawToken)` → returns a new (access-eligible
userId, raw refresh token) on success, signals `Expired` / `Revoked` / `Replay` / `NotFound`
distinctly; `RevokeFamilyAsync(rawToken)` for logout. Raw token = cryptographically random
(`RandomNumberGenerator`, ≥256-bit), returned to the caller once and never stored. Hash with SHA-256.
Lifetime from a new `JwtOptions.RefreshTokenDays` setting.

#### 4. JwtOptions + access-token lifetime

**File**: `src/Homdutio.Api/Auth/JwtOptions.cs`, `src/Homdutio.Api/appsettings.json`

**Intent**: Shorten the access token and add the refresh-token lifetime knob.

**Contract**: Change `AccessTokenMinutes` default 120 → 15; add `RefreshTokenDays` (e.g. 30). Mirror
the non-secret values in `appsettings.json` under the `Jwt` section.

#### 5. Login issues a refresh token

**File**: `src/Homdutio.Api/Auth/AuthEndpoints.cs`

**Intent**: On successful login, also mint and return a refresh token (new family).

**Contract**: Inject `RefreshTokenService` into the `login` handler; after `CreateAccessToken`, call
`IssueAsync(user.Id)`. Extend `LoginResponse` (`AuthEndpoints.cs:82`) to
`LoginResponse(string AccessToken, DateTime ExpiresAtUtc, string RefreshToken)`.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- Migration is additive and applies cleanly: `dotnet ef database update` against LocalDB
- New + existing API tests pass: `dotnet test`
- A login integration test asserts the response now includes a non-empty `refreshToken` and a row
  exists in `RefreshTokens` with a hash (not the raw token)

#### Manual Verification:

- Inspect the `RefreshTokens` table after a login: `TokenHash` is a hash, `FamilyId` set,
  `ExpiresAtUtc` ~30 days out, `ConsumedAtUtc`/`RevokedAtUtc` null
- Access token `exp` is now ~15 min out (decode the JWT)

**Implementation Note**: After completing this phase and all automated verification passes, pause for
manual confirmation before proceeding to Phase 2.

---

## Phase 2: Backend — Refresh, Logout & Replay Handling

### Overview

Add the `refresh` and `logout` endpoints on top of Phase 1's store, implementing rotation-on-use,
replay detection (family revocation), and clean expiry. This is the security-critical phase.

### Changes Required:

#### 1. Refresh endpoint

**File**: `src/Homdutio.Api/Auth/AuthEndpoints.cs`

**Intent**: Exchange a valid refresh token for a fresh (access, refresh) pair, rotating the refresh
token.

**Contract**: `POST /api/auth/refresh` with body `{ refreshToken }` (anonymous — no bearer required).
Calls `RefreshTokenService.ValidateAndRotateAsync`; on success returns the same shape as login
(`{ accessToken, expiresAtUtc, refreshToken }`) with the new access token minted by
`JwtTokenService` and the rotated refresh token. On `Expired`/`Revoked`/`NotFound` → `401`. On
`Replay` → revoke the family, then `401`. New request/response records as needed.

#### 2. Logout endpoint

**File**: `src/Homdutio.Api/Auth/AuthEndpoints.cs`

**Intent**: Real server-side revocation on logout.

**Contract**: `POST /api/auth/logout` with body `{ refreshToken }`. Calls `RevokeFamilyAsync` and
returns `200` regardless of whether the token was already gone (idempotent — never leak existence).
Does not require a valid access token (a user with an expired access token must still be able to log
out).

#### 3. Replay + expiry semantics in the service

**File**: `src/Homdutio.Api/Auth/RefreshTokenService.cs`

**Intent**: Finalize the rotation/replay/expiry logic the endpoints depend on.

**Contract**: Presenting a token whose row has `ConsumedAtUtc != null` (already rotated) = replay →
set `RevokedAtUtc` on **every** row sharing the `FamilyId`. Presenting an expired or revoked token =
reject without family revocation. Consume must be single-winner via the `RowVersion` conditional
update (see Critical Implementation Details).

### Success Criteria:

#### Automated Verification:

- `dotnet test` passes, including new integration tests covering:
  - refresh with a valid token → 200, returns a **different** refresh token, old token now rejected
  - refresh with an expired token → 401
  - **replay**: refresh once, then refresh again with the *original* token → 401 **and** the rotated
    (second) token is now also revoked (family killed)
  - logout → subsequent refresh with that family's token → 401
  - refresh with a garbage/unknown token → 401
  - logout with an unknown token → 200 (idempotent)

#### Manual Verification:

- Walk the rotation in a REST client: login → refresh → refresh, confirming each refresh token works
  exactly once
- Confirm a replayed token kills the chain (DB shows `RevokedAtUtc` set across the family)

**Implementation Note**: Pause for manual confirmation before proceeding to Phase 3.

---

## Phase 3: SPA — Persist, Restore & Interceptor Wiring

### Overview

Wire the Angular SPA to persist the refresh token, restore the session on startup before routing,
silently refresh on access-token expiry, and revoke on logout.

### Changes Required:

#### 1. AuthService: persist + refresh

**File**: `web/src/app/auth/auth.service.ts`

**Intent**: Store the refresh token in `localStorage`, expose a `refresh()` that re-mints the access
token, and clear storage on logout. Access token stays in the in-memory signal.

**Contract**: On `login` success, write `response.refreshToken` to `localStorage` (single key, e.g.
`homdutio.refreshToken`) and set the in-memory access token as today. Add
`refresh(): Observable<boolean>` (or Promise) that POSTs the stored refresh token to
`/api/auth/refresh`, on success updates the in-memory access token **and** the stored refresh token
(rotation), on failure clears both and resolves false. Update the `LoginResponse` interface
(`auth.service.ts:15`) to include `refreshToken`. `logout()` becomes async: POST
`/api/auth/logout` with the stored token (fire-and-forget with a fallback), then clear in-memory +
`localStorage` + household/task state (existing `clearOnLogout()` calls preserved).

#### 2. Startup restore (blocking APP_INITIALIZER)

**File**: `web/src/app/app.config.ts` (+ a small initializer factory, new file e.g.
`web/src/app/auth/session-restore.initializer.ts`)

**Intent**: Before the router/guards run, attempt one silent refresh if a refresh token is present,
so the synchronous guard sees an authenticated user.

**Contract**: Provide an `APP_INITIALIZER` (or `provideAppInitializer`) that, when a refresh token
exists in `localStorage`, awaits `AuthService.refresh()` bounded by a timeout; resolves regardless of
outcome (success → access token in memory; failure/timeout → logged-out, storage cleared). No refresh
token present → resolve immediately. Guard (`auth.guard.ts`) is **unchanged**.

#### 3. unauthorizedInterceptor: silent refresh + retry once

**File**: `web/src/app/auth/unauthorized.interceptor.ts`

**Intent**: On a protected 401, attempt one silent refresh and replay the original request before
giving up.

**Contract**: Add `/api/auth/refresh` (and keep `/api/auth/logout`) to the exempt list so their 401s
don't recurse. On a non-exempt 401: call `AuthService.refresh()`; on success, retry the original
request **once** (mark it so a second 401 falls through); on failure, the existing
logout + redirect-to-`/login` path runs. Guard against concurrent refreshes (single in-flight refresh
shared across queued 401s).

### Success Criteria:

#### Automated Verification:

- Lint passes: `npm run lint` (in `web/`)
- Type check / build: the Release `BuildAngularSpa` target succeeds
- vitest passes including new specs: `npm test` (in `web/`) covering:
  - the startup initializer restores the access token when refresh succeeds and clears state when it
    fails/times out
  - the 401 interceptor refreshes once and retries the original request, and on a second 401 logs out
    + redirects (no loop)
  - `logout()` posts to `/api/auth/logout` and clears `localStorage`

#### Manual Verification:

- Log in, hard-reload the page → still on `/board`, no `/login` flash
- Close the tab, reopen the app URL → resumes authenticated
- Wait past the 15-min access-token expiry (or temporarily shorten it), perform a board action →
  succeeds silently (no bounce to `/login`)
- Log out → reload → lands on `/login` (storage cleared, refresh rejected), no redirect loop
- Log in on a second browser/device, log out the first → second stays authenticated
- Mobile ≤400px: the reload/restore path shows no layout regression (NFR-2)

**Implementation Note**: Final phase — after manual confirmation, the slice is complete.

---

## Testing Strategy

### Unit Tests (vitest, SPA):

- Startup initializer: restores on refresh success; clears + resolves on failure/timeout; no-op when
  no stored token
- `unauthorizedInterceptor`: refresh-once-and-retry; second-401 → logout+redirect; refresh endpoint
  exempt
- `AuthService`: login persists refresh token; `refresh()` rotates the stored token; `logout()` posts
  + clears

### Integration Tests (xUnit, `WebApplicationFactory`):

- Login issues a refresh token + persists a hashed row (Phase 1)
- Refresh rotates (old token rejected, new token works) (Phase 2)
- Replay → family revoked (Phase 2)
- Expired / revoked / unknown token → 401 (Phase 2)
- Logout revokes family + is idempotent (Phase 2)

### Manual Testing Steps:

1. Reload, reopen-tab, and browser-restart all resume the session
2. Mid-session access-token expiry refreshes silently
3. Logout → reload lands cleanly on `/login`, no loop
4. Multi-device independence
5. ≤400px no regression

## Performance Considerations

- One extra DB write per login (issue) and per refresh (rotate). Negligible at single-household
  scale; the `TokenHash` unique index keeps lookups O(log n).
- Expired/consumed/revoked rows accumulate. Out of scope to schedule a cleanup job here, but a periodic
  delete of rows past `ExpiresAtUtc` is the natural follow-up — note it for ops; the indexes on
  `UserId`/`ExpiresAtUtc` support it.
- Access-token refreshes increase from ~every 2h to ~every 15 min — still trivial traffic.

## Migration Notes

- `AddRefreshTokens` is **additive** (new table only); safe with F-04's migrate-first deploy.
- Shortening `AccessTokenMinutes` to 15 affects only newly issued tokens; tokens already in flight
  keep their original `exp`. No data migration.
- Existing users have no refresh token until their next login — they get one transparently on next
  sign-in. No backfill needed.

## References

- Roadmap slice: `context/foundation/roadmap.md` S-10 (lines 45, 246–256, 275)
- Change identity: `context/changes/session-persistence/change.md`
- Existing auth backend: `src/Homdutio.Api/Auth/AuthEndpoints.cs`,
  `src/Homdutio.Api/Auth/JwtTokenService.cs`, `src/Homdutio.Api/Program.cs:53`
- Single-winner concurrency precedent: `HouseholdInvite` RowVersion
  (`src/Homdutio.Data/ApplicationDbContext.cs:108`)
- SPA auth layer: `web/src/app/auth/auth.service.ts`, `auth.guard.ts`,
  `unauthorized.interceptor.ts`, `app.config.ts`
- Integration-test pattern: `tests/Homdutio.Api.Tests/AuthEndpointsTests.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend — Refresh-Token Store & Issuance

#### Automated

- [x] 1.1 Solution builds: `dotnet build` — 2089ae7
- [x] 1.2 `AddRefreshTokens` migration applies cleanly against LocalDB — 2089ae7
- [x] 1.3 New + existing API tests pass: `dotnet test` — 2089ae7
- [x] 1.4 Login integration test asserts non-empty `refreshToken` + a hashed `RefreshTokens` row — 2089ae7

#### Manual

- [x] 1.5 `RefreshTokens` row after login: hashed token, FamilyId set, ~30-day expiry, null consumed/revoked — 2089ae7
- [x] 1.6 Access token `exp` is ~15 min out — 2089ae7

### Phase 2: Backend — Refresh, Logout & Replay Handling

#### Automated

- [x] 2.1 `dotnet test` passes including refresh-rotates test (old token rejected, new works) — 621ce61
- [x] 2.2 Expired-token refresh → 401 test — 621ce61
- [x] 2.3 Replay test: re-presenting consumed token → 401 and family revoked — 621ce61
- [x] 2.4 Logout → subsequent refresh → 401 test — 621ce61
- [x] 2.5 Unknown-token refresh → 401; unknown-token logout → 200 (idempotent) — 621ce61

#### Manual

- [x] 2.6 REST-client rotation walk: each refresh token works exactly once — 621ce61
- [x] 2.7 Replayed token kills the chain (RevokedAtUtc across family) — 621ce61

### Phase 3: SPA — Persist, Restore & Interceptor Wiring

#### Automated

- [x] 3.1 Lint passes: `npm run lint` (adapted → `npx prettier --check`; no lint script in repo) — bed8c29
- [x] 3.2 Release `BuildAngularSpa` target succeeds — bed8c29
- [x] 3.3 vitest: startup initializer restores on success / clears on failure-timeout / no-op when no token — bed8c29
- [x] 3.4 vitest: 401 interceptor refreshes once + retries, second 401 → logout+redirect — bed8c29
- [x] 3.5 vitest: `logout()` posts to `/api/auth/logout` and clears `localStorage` — bed8c29

#### Manual

- [x] 3.6 Hard reload → still on `/board`, no `/login` flash — bed8c29
- [x] 3.7 Close + reopen tab → resumes authenticated — bed8c29
- [x] 3.8 Mid-session access-token expiry → board action succeeds silently — bed8c29
- [x] 3.9 Logout → reload → lands on `/login`, no redirect loop — bed8c29
- [x] 3.10 Multi-device: logout one, other stays authenticated — bed8c29
- [x] 3.11 ≤400px no layout regression on the restore path (NFR-2) — bed8c29
