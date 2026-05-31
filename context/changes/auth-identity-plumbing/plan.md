# Auth + Identity Plumbing — ASP.NET Identity + JWT Bearer Implementation Plan

## Overview

Mount **ASP.NET Core Identity** (with a custom `ApplicationUser`) onto the existing
`ApplicationDbContext`, and stand up a **stateless JWT bearer** pipeline that issues and validates
signed tokens. The slice ships three API endpoints — `POST /api/auth/register`,
`POST /api/auth/login` (issues a JWT), and a protected `GET /api/auth/me` probe — plus an
integration-test project that proves the validation middleware actually gates access. This is the
bounded foundation enabler **F-02**: it satisfies the PRD's Access Control surface (email + password,
JWT bearer, one account per person) and unlocks **S-01** (the user-facing register/login/logout
flow) and the household-scoped authorization **S-07** later hardens — **without** any user-facing
pages, SPA wiring, or production deploy.

## Current State Analysis

The persistence baseline (F-01) is in place; auth is entirely absent:

- `src/Homdutio.Data/ApplicationDbContext.cs` extends plain `DbContext` and carries only the
  throwaway `DbSet<SchemaProbe>` (`ApplicationDbContext.cs:11-18`). No Identity tables.
- `src/Homdutio.Data/Homdutio.Data.csproj:10` references only `Microsoft.EntityFrameworkCore.SqlServer 9.0.9` — **no Identity EF stores package**.
- `src/Homdutio.Api/Program.cs` wires the DbContext + `/health` + the stock `/weatherforecast` demo and the SPA fallback, but has **no `UseAuthentication`/`UseAuthorization`, no authentication scheme, and no auth packages** (`Microsoft.AspNetCore.Authentication.JwtBearer` absent).
- One migration exists: `src/Homdutio.Data/Migrations/20260530163042_InitialCreate.cs` (creates `__EFMigrationsHistory` + the `SchemaProbes` table).
- The Angular app (`web/`) is a bare shell: `app.config.ts` has **no `provideHttpClient`**, `app.routes.ts` is empty, and there is no HTTP interceptor or token storage. SPA token handling is greenfield (and out of scope here).
- Tests today: `tests/Homdutio.Data.Tests/PersistenceSmokeTests.cs` exercises a `SchemaProbe` write/read round-trip against LocalDB. No web/integration test project exists.

### Key Discoveries:

- **JWT decision is locked upstream** — roadmap F-02 (2026-05-30) chose **stateless JWT bearer, not cookie sessions**, superseding `tech-stack.md`'s original cookie note. The signing key lives in App Service settings / Key Vault, never the repo (`infrastructure.md` risk register). Logout = client-side token discard + short token lifetime; server-side refresh/revocation store is **explicitly deferred unless required**.
- **`MapIdentityApi` is off-charter** — ASP.NET's turnkey identity endpoints issue *opaque* bearer tokens, not JWTs, so they contradict the locked JWT decision. The chosen path is `AddIdentityCore<ApplicationUser>` for the user store + password hashing and **hand-minted JWTs** validated by `AddJwtBearer` (decision: Auth surface).
- **Single DbContext, additive then destructive migration** — `ApplicationDbContext` becomes `IdentityDbContext<ApplicationUser>`; the same migration that adds the `AspNet*` tables also **drops `SchemaProbes`** (decision: remove SchemaProbe now).
- **Email confirmation is disabled in v1** — the only permitted transactional email is password reset (S-08, SendGrid); PRD non-goals cut every other email pipeline, so `RequireConfirmedAccount = false` (decision: Email confirm).
- **`EnableRetryOnFailure` is already on** (`Program.cs:16`) — the execution strategy disallows user-initiated transactions spanning the retry boundary. Identity's `UserManager` operations are single SaveChanges calls, so this is not a problem here, but it remains a note for downstream slices.
- **`BuildAngularSpa` Release target** (`Homdutio.Api.csproj:24-28`) must remain undisturbed; adding packages, endpoints, and a test project is additive to it.

## Desired End State

ASP.NET Core Identity is mounted on `ApplicationDbContext` via `ApplicationUser`; the `AddIdentity`
migration applies cleanly to LocalDB (adding the `AspNet*` tables and dropping `SchemaProbes`); the
API exposes `POST /api/auth/register`, `POST /api/auth/login` (returning a signed JWT), and a
JWT-protected `GET /api/auth/me`; `UseAuthentication`/`UseAuthorization` are wired so an unauthorized
request to `/me` returns 401 and a valid bearer token returns the caller's claims; the JWT signing
key is read from configuration (user-secrets locally) and never committed; and an
`Homdutio.Api.Tests` integration project proves register → login → authorized `/me` (200) and
unauthorized `/me` (401) against LocalDB.

**Verification:** `dotnet build` and `dotnet test` are green; `dotnet ef database update` applies the
`AddIdentity` migration to LocalDB; the running app issues a JWT on login and rejects an unauthenticated
`/me` with 401; no signing key or secret appears in any tracked file.

## What We're NOT Doing

- **No user-facing pages** — no Angular login/register UI, no token storage, no HTTP interceptor, no routes. All SPA work is **S-01** (decision: backend-only).
- **No refresh tokens / no server-side revocation store** — single moderate-lifetime access token; logout is client-side discard. Refresh/revocation is deferred per the roadmap (decision: token model).
- **No roles / household scoping** — `IdentityRole`, admin/adult-member roles, and household-membership claims belong to S-02; this slice only stands up the user store + token pipeline.
- **No email confirmation flow** — `RequireConfirmedAccount = false`; account-confirmation email is out of scope (the only v1 email is password reset, S-08).
- **No password reset** — that is S-08 (SendGrid).
- **No production deploy or prod migration apply** — applying `AddIdentity` to Azure SQL and setting the prod `Jwt__SigningKey` are deferred to the deploy that carries this code (F-04 CI or a manual redeploy), consistent with F-01's prod-verify deferral. See Migration Notes.
- **No external identity providers** (Google/Microsoft sign-in) — email + password only per PRD Access Control.

## Implementation Approach

Build inside-out and local-first, mirroring F-01's rhythm. Phase 1 changes the data layer and gets a
clean Identity schema onto LocalDB (no web concerns). Phase 2 wires the JWT issue/validate pipeline
and the three endpoints in the API and proves them manually. Phase 3 locks in an automated regression
net with a `WebApplicationFactory`-based integration test project. Every phase is fully verifiable on
the dev machine; nothing in this slice touches Azure or costs money.

## Critical Implementation Details

- **HMAC-SHA256 key length** — the symmetric signing key for `HS256` must be **at least 256 bits (32+ ASCII chars)**, or `AddJwtBearer`/token generation throws at runtime. The user-secrets value and the documented App Service setting must both satisfy this.
- **Middleware ordering** — `app.UseAuthentication()` must precede `app.UseAuthorization()`, and both must run before the endpoints and before `app.MapFallbackToFile("index.html")`. The `/api/auth/me` endpoint must carry `.RequireAuthorization()` so the SPA fallback never shadows it and unauthenticated calls get 401, not `index.html`.
- **`WebApplicationFactory<Program>` needs an accessible entry point** — the minimal-API `Program` is implicitly internal; add `public partial class Program;` at the end of `Program.cs` so the test project can reference it as the generic type argument.

## Phase 1: Identity Store + Schema Migration

### Overview

Add the Identity EF stores package to the data library, introduce `ApplicationUser`, switch
`ApplicationDbContext` to `IdentityDbContext<ApplicationUser>`, remove the throwaway `SchemaProbe`,
generate the `AddIdentity` migration, repoint the F-01 data-layer smoke test, and apply the migration
to LocalDB. End state: an Identity-backed schema on LocalDB with the placeholder table gone.

### Changes Required:

#### 1. Identity EF stores package

**File**: `src/Homdutio.Data/Homdutio.Data.csproj`

**Intent**: Give the data library the Identity DbContext base class and EF user/role stores.

**Contract**: Add `PackageReference Microsoft.AspNetCore.Identity.EntityFrameworkCore` (9.0.x, matching the pinned EF Core 9.0.9 line). Existing `Microsoft.EntityFrameworkCore.SqlServer` stays.

#### 2. ApplicationUser entity

**File**: `src/Homdutio.Data/Entities/ApplicationUser.cs` (new)

**Intent**: Define the project's user type now (empty subclass) so later slices add household FK / member metadata as additive migrations without swapping the generic user type (decision: custom `ApplicationUser`).

**Contract**: `public class ApplicationUser : IdentityUser` (string GUID PK inherited), empty body, in the `Homdutio.Data.Entities` namespace. A code comment notes future properties (household membership per FR-007) arrive with S-02.

#### 3. ApplicationDbContext → IdentityDbContext; remove SchemaProbe

**File**: `src/Homdutio.Data/ApplicationDbContext.cs`, `src/Homdutio.Data/Entities/SchemaProbe.cs` (delete)

**Intent**: Host the Identity tables on the single shared context and retire the placeholder now that real tables exist (decision: remove SchemaProbe).

**Contract**: Change base class to `IdentityDbContext<ApplicationUser>`; keep the `DbContextOptions<ApplicationDbContext>` constructor and call `base(options)`. Remove the `SchemaProbes` `DbSet` and delete `SchemaProbe.cs`. If `OnModelCreating` is overridden, call `base.OnModelCreating(builder)` first (Identity requires it). Update the class XML summary to describe the Identity store.

#### 4. AddIdentity migration

**File**: `src/Homdutio.Data/Migrations/*` (generated)

**Intent**: Produce the migration that creates the `AspNet*` Identity tables and drops the `SchemaProbes` table.

**Contract**: `dotnet ef migrations add AddIdentity --project src/Homdutio.Data --startup-project src/Homdutio.Api`, then `dotnet ef database update --project src/Homdutio.Data --startup-project src/Homdutio.Api`. Verify the generated `Up` both creates `AspNetUsers`/`AspNetRoles`/etc. and drops `SchemaProbes`. Migration files committed.

#### 5. Repoint the F-01 data-layer smoke test

**File**: `tests/Homdutio.Data.Tests/PersistenceSmokeTests.cs`

**Intent**: Keep the data-layer regression net alive after `SchemaProbe` is gone by proving the Identity-backed schema applies and persists a row.

**Contract**: Replace the `SchemaProbe` write/read round-trip with an `ApplicationUser` round-trip — apply migrations to a uniquely-named throwaway LocalDB database, insert an `ApplicationUser` (set `UserName`/`Email`), read it back on a fresh context, assert equality, and tear down (`EnsureDeleted`) in disposal. The test class/file may be renamed to reflect the Identity store. No reference to `SchemaProbe` remains.

### Success Criteria:

#### Automated Verification:

- Build clean: `dotnet build Homdutio.sln`
- Migration applies to LocalDB: `dotnet ef database update --project src/Homdutio.Data --startup-project src/Homdutio.Api`
- Data-layer tests pass: `dotnet test tests/Homdutio.Data.Tests/Homdutio.Data.Tests.csproj`
- No `SchemaProbe` reference remains in tracked files: `git grep -i "SchemaProbe"` returns nothing

#### Manual Verification:

- The `AddIdentity` migration's `Up` creates the `AspNet*` tables and drops `SchemaProbes`.
- LocalDB shows the `AspNetUsers` table after `database update`.

**Implementation Note**: After automated verification passes, pause for manual confirmation that the Identity schema applied as expected before proceeding.

---

## Phase 2: JWT Pipeline + Auth Endpoints

### Overview

Register Identity Core on `ApplicationDbContext`, configure JWT bearer issuance and validation from
configuration, add a token-minting service, expose register/login/`me` endpoints, wire the auth
middleware, store the local signing key in user-secrets, and remove the template `/weatherforecast`
demo. End state: a running, manually-verifiable JWT auth pipeline.

### Changes Required:

#### 1. JWT bearer package

**File**: `src/Homdutio.Api/Homdutio.Api.csproj`

**Intent**: Add the JWT bearer authentication handler.

**Contract**: Add `PackageReference Microsoft.AspNetCore.Authentication.JwtBearer` (9.0.x). `AddIdentityCore` and `AddSignInManager` are available from the shared framework (`Microsoft.NET.Sdk.Web`) once the Data project's Identity EF stores package is referenced.

#### 2. Identity Core + JWT authentication registration

**File**: `src/Homdutio.Api/Program.cs`

**Intent**: Mount the Identity user store on the existing context and register the JWT bearer scheme reading issuer/audience/key from configuration (decision: IdentityCore + custom JWT).

**Contract**:
- `builder.Services.AddIdentityCore<ApplicationUser>(options => { options.SignIn.RequireConfirmedAccount = false; })` + `.AddEntityFrameworkStores<ApplicationDbContext>()` + `.AddSignInManager()`. Keep Identity's default password policy.
- `builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)` with `TokenValidationParameters` validating issuer, audience, lifetime, and the symmetric signing key. Config keys: `Jwt:Issuer`, `Jwt:Audience`, `Jwt:SigningKey`, `Jwt:AccessTokenMinutes` (decision: single access token, moderate lifetime ~120–240 min).
- `builder.Services.AddAuthorization()`.
- Bind a small strongly-typed options object (e.g. `JwtOptions`) from the `Jwt` config section for reuse by the token service.

#### 3. JWT token-minting service

**File**: `src/Homdutio.Api/Auth/JwtTokenService.cs` (new)

**Intent**: Centralize signed-JWT creation so issuance and validation share one definition of issuer/audience/key/claims.

**Contract**: A DI-registered service exposing a method that takes an `ApplicationUser` and returns a signed JWT string. Claims: `sub` = user Id, `email`, `jti` (`Guid.NewGuid()`), and standard `exp`/`iat`. Signs with `HS256` over the configured key via `SymmetricSecurityKey` + `SigningCredentials`. Reads `JwtOptions`.

#### 4. Auth endpoints

**File**: `src/Homdutio.Api/Auth/AuthEndpoints.cs` (new) + registration in `Program.cs`

**Intent**: Expose the three plumbing endpoints proving end-to-end issue + validate (decision: register + login + protected `/me`).

**Contract**: A minimal-API endpoint group under `/api/auth`:
- `POST /api/auth/register` — body `{ email, password }`; creates the user via `UserManager.CreateAsync`; returns 200 on success, 400 with Identity error details on failure.
- `POST /api/auth/login` — body `{ email, password }`; verifies via `SignInManager.CheckPasswordSignInAsync` (lockout enabled); on success returns `{ accessToken, expiresAtUtc }` from `JwtTokenService`; on failure returns 401.
- `GET /api/auth/me` — `.RequireAuthorization()`; returns the authenticated user's `sub`/`email` claims (200) so the validation middleware is exercised.

Request/response DTOs live alongside the endpoints. No user-facing validation polish beyond Identity's own results (that is S-01).

#### 5. Auth middleware + remove template demo

**File**: `src/Homdutio.Api/Program.cs`

**Intent**: Activate the auth pipeline in the correct order and drop the stock template noise now that real `/api` endpoints exist.

**Contract**: Add `app.UseAuthentication();` then `app.UseAuthorization();` before endpoint mapping and before `app.MapFallbackToFile("index.html")`. Map the auth endpoint group. Remove the `/weatherforecast` endpoint and the `WeatherForecast` record. Append `public partial class Program;` to the end of the file for Phase 3's test host (see Critical Implementation Details).

#### 6. Local signing key via user-secrets

**File**: API project user-secrets store (not tracked) — `UserSecretsId` already present (`Homdutio.Api.csproj:8`)

**Intent**: Supply the dev signing key (and issuer/audience) without committing a secret (decision: key in user-secrets / App Service settings, never the repo).

**Contract**: `dotnet user-secrets set "Jwt:SigningKey" "<32+ char random string>"`, plus `Jwt:Issuer` and `Jwt:Audience` (these two may instead live in `appsettings.json` as non-secret values). Confirm no `Jwt:SigningKey` value appears in any committed `appsettings*.json`. Document the prod equivalent (`Jwt__SigningKey` App Service setting) for the deferred deploy.

### Success Criteria:

#### Automated Verification:

- Build clean: `dotnet build Homdutio.sln`
- No signing key in tracked files: `git grep -i "SigningKey" -- '*.json'` shows only non-secret placeholders (no real key value)
- App starts: `dotnet run --project src/Homdutio.Api` boots without DI/auth configuration errors

#### Manual Verification:

- `POST /api/auth/register` with a new email/password returns 200; a duplicate or weak password returns 400 with Identity errors.
- `POST /api/auth/login` with valid credentials returns a JWT; wrong credentials return 401.
- `GET /api/auth/me` with `Authorization: Bearer <token>` returns the user's claims (200); with no/invalid token returns 401 (not `index.html`).
- `/health` still returns `Healthy` and is not shadowed by auth or the SPA fallback.

**Implementation Note**: After automated verification passes, pause for manual confirmation that the register → login → `/me` round-trip and the 401 path behave as expected before proceeding.

---

## Phase 3: Integration Test Project

### Overview

Stand up an API integration-test project using `WebApplicationFactory<Program>` against LocalDB and
assert the full pipeline: register → login → authorized `/me` (200) and unauthorized `/me` (401) —
giving the auth foundation a real regression net and a host harness later slices reuse.

### Changes Required:

#### 1. API integration test project

**File**: `tests/Homdutio.Api.Tests/Homdutio.Api.Tests.csproj` (new)

**Intent**: Establish the web integration harness (decision: integration project via `WebApplicationFactory` + LocalDB for provider parity, matching F-01).

**Contract**: `net9.0` xUnit project referencing `Homdutio.Api` and `Microsoft.AspNetCore.Mvc.Testing` (9.0.x); added to `Homdutio.sln`.

#### 2. Auth pipeline integration tests

**File**: `tests/Homdutio.Api.Tests/AuthEndpointsTests.cs` (new)

**Intent**: Prove the JWT issue + validate pipeline end-to-end through the real ASP.NET middleware.

**Contract**: A `WebApplicationFactory<Program>`-derived fixture that overrides configuration to supply a test `Jwt:SigningKey`/issuer/audience and a uniquely-named throwaway LocalDB connection string, and applies migrations on startup (and `EnsureDeleted` on disposal). Tests:
- register a user → 200;
- login with those credentials → 200 with a non-empty `accessToken`;
- `GET /api/auth/me` with the bearer token → 200 and the expected `email`/`sub` claim;
- `GET /api/auth/me` with no token → 401;
- `GET /api/auth/me` with a malformed/expired token → 401.

### Success Criteria:

#### Automated Verification:

- Full suite passes: `dotnet test Homdutio.sln`
- Test run leaves no residual database (clean re-run verifies teardown)

#### Manual Verification:

- The test reads as a clear template for how S-01 (and later authorized slices) test against the real auth middleware.

**Implementation Note**: After `dotnet test` is green, pause for manual confirmation before closing the slice.

---

## Testing Strategy

### Unit Tests:

- (Covered by integration) JWT issuance produces a token whose claims validate under the same parameters — exercised through login → `/me` rather than in isolation.

### Integration Tests:

- register → 200 (user created in the Identity store).
- login → 200 with a signed JWT for valid credentials; 401 for invalid.
- `GET /api/auth/me` → 200 + claims with a valid bearer token; 401 with no/invalid/expired token.
- Migrations (`AddIdentity`) apply against a throwaway LocalDB (validates the real SqlServer-provider Identity schema).

### Manual Testing Steps:

1. Run the API locally; `POST /api/auth/register` a fresh account → expect 200.
2. `POST /api/auth/login` with those credentials → expect a JWT + expiry.
3. `GET /api/auth/me` with `Authorization: Bearer <token>` → expect 200 + claims; without the header → expect 401.
4. `GET /health` → still `Healthy`.
5. `git grep -i "SigningKey"` over tracked files reveals no real key value.

## Performance Considerations

Negligible load (single household). Password hashing (PBKDF2 via Identity defaults) is intentionally
CPU-bound but trivial at this volume. JWT validation is in-process and stateless — no DB round-trip
per request — which suits the Basic-tier 5-DTU budget. `EnableRetryOnFailure` remains enabled on the
DbContext (`Program.cs:16`); Identity store operations are single `SaveChanges` calls and do not span
the retry boundary.

## Migration Notes

The forward migration is `AddIdentity`: it **adds** the `AspNet*` Identity tables and **drops** the
`SchemaProbes` table (the placeholder retired this slice). The drop is destructive but safe — the
`SchemaProbe` table held only throwaway probe rows and is not referenced by any application code after
this change. Per `src/Homdutio.Data/MIGRATIONS.md`, reverting is manual (no auto-rollback, no slots on
B1); generate and review a down-script before any out-of-band revert.

**Deferred to the next deploy (not this slice):** applying `AddIdentity` to Azure SQL and setting the
prod `Jwt__SigningKey` App Service setting — paired with the deploy that carries the auth code (F-04
CI auto-deploy or a manual `dotnet publish` → `tar.exe` zip → `az webapp deploy`), consistent with
F-01's deferred prod verification.

## References

- Roadmap item: `context/foundation/roadmap.md` → F-02 (`auth-identity-plumbing`)
- PRD: `context/foundation/prd.md` → Access Control, FR-001/002/003
- JWT decision + signing-key handling: `context/foundation/roadmap.md` F-02 decision (2026-05-30); `context/foundation/infrastructure.md` risk register + Unknown Unknowns
- Prerequisite (EF store): `context/changes/persistence-baseline/plan.md`; migration policy `src/Homdutio.Data/MIGRATIONS.md`
- Current host pipeline: `src/Homdutio.Api/Program.cs`, `src/Homdutio.Api/Homdutio.Api.csproj`, `src/Homdutio.Data/ApplicationDbContext.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Identity Store + Schema Migration

#### Automated

- [x] 1.1 Build clean: `dotnet build Homdutio.sln` — ec86a78
- [x] 1.2 Migration applies to LocalDB: `dotnet ef database update ...` — ec86a78
- [x] 1.3 Data-layer tests pass: `dotnet test tests/Homdutio.Data.Tests/...` — ec86a78
- [x] 1.4 No `SchemaProbe` reference remains: `git grep -i "SchemaProbe"` empty — ec86a78

#### Manual

- [x] 1.5 `AddIdentity` `Up` creates `AspNet*` tables and drops `SchemaProbes` — ec86a78
- [x] 1.6 LocalDB shows `AspNetUsers` after `database update` — ec86a78

### Phase 2: JWT Pipeline + Auth Endpoints

#### Automated

- [x] 2.1 Build clean: `dotnet build Homdutio.sln` — c24bb81
- [x] 2.2 No signing key value in tracked files: `git grep -i "SigningKey" -- '*.json'` — c24bb81
- [x] 2.3 App starts without DI/auth configuration errors — c24bb81

#### Manual

- [x] 2.4 `register` returns 200 (new) / 400 (duplicate or weak password) — c24bb81
- [x] 2.5 `login` returns a JWT for valid creds, 401 for invalid — c24bb81
- [x] 2.6 `/me` returns claims (200) with a bearer token, 401 without — c24bb81
- [x] 2.7 `/health` still `Healthy`, not shadowed by auth or SPA fallback — c24bb81

### Phase 3: Integration Test Project

#### Automated

- [x] 3.1 Full suite passes: `dotnet test Homdutio.sln`
- [x] 3.2 Test run leaves no residual database (clean re-run)

#### Manual

- [x] 3.3 Test reads as a clear template for later authorized-slice tests
