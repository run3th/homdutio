# Password Reset Implementation Plan

## Overview

Add forgotten-password recovery (S-08 / FR-020): a registered user requests a reset email, then
sets a new password from a time-limited, same-origin link. This introduces the application's first
and only v1 transactional-email pipeline (Azure Communication Services Email — superseding the
roadmap's original SendGrid choice on 2026-06-25), kept deliberately narrow to the reset path.

## Current State Analysis

The auth backend is mature but has **no reset path and no email capability**:

- Endpoints live in `src/Homdutio.Api/Auth/AuthEndpoints.cs` on `app.MapGroup("/api/auth")`:
  `register`, `login`, `refresh`, `logout`, `me`. Patterns: success → `Results.Ok(...)`;
  validation failure → `Results.ValidationProblem(Dictionary<code, string[]>)`; auth failure →
  `Results.Unauthorized()` with **no detail** (deliberate anti-enumeration posture).
- Identity is registered in `Program.cs` as
  `AddIdentityCore<ApplicationUser>(o => o.SignIn.RequireConfirmedAccount = false)
  .AddEntityFrameworkStores<ApplicationDbContext>().AddSignInManager()` — **`AddDefaultTokenProviders()`
  is NOT called**, so `UserManager.GeneratePasswordResetTokenAsync` would throw today.
- `RefreshTokenService` (scoped, `src/Homdutio.Api/Auth/`) hashes tokens, rotates them, and can
  **revoke an entire family** — the mechanism for booting active sessions on reset.
- **No `IEmailSender`, no email SDK package, no email config** anywhere (confirmed by grep).
  `Program.cs` explicitly notes reset is the only permitted v1 email.
- Tests: `tests/Homdutio.Api.Tests/AuthApiFactory.cs` (`WebApplicationFactory<Program>`, per-test
  throwaway LocalDB, in-memory config overrides) + `AuthEndpointsTests.cs` (register/login/refresh
  helpers, scoped-DbContext DB assertions). `public partial class Program;` exists for the factory.
- SPA (`web/src/app/`): `AuthService` (signals; in-memory access token + localStorage refresh
  token); `unauthorized.interceptor.ts` holds an `AUTH_ENDPOINTS` allowlist whose 401s must not
  trigger a refresh; `validation-problem.ts#mapValidationProblem` flattens RFC-7807 bodies;
  `register.component.ts` exports `passwordPolicyValidator` and demonstrates the register→login
  hand-off via Router navigation `state` (notice + prefilled email); `login.component.ts` reads
  that state and guards `returnUrl` against open redirects.

## Desired End State

A user clicks "Forgot password?" on `/login`, enters their email, and always sees the same generic
confirmation. If the email matches an account, Azure Communication Services delivers a link to a
same-origin `/reset-password?email=…&token=…` page (valid 1 hour). On that page they set a new
password meeting the existing Identity policy; on success every active session for that account is
revoked and they are redirected to `/login` with a "Password updated — please log in" notice and
their email prefilled. Unknown emails, expired/invalid tokens, and email-send failures never leak
whether an account exists. Verified by integration tests (fake email sender captures the link/token)
and vitest specs, plus manual UI testing at ≤400px.

### Key Discoveries:

- `Program.cs:37-42` — Identity registration lacks `AddDefaultTokenProviders()`; reset tokens
  require it.
- `src/Homdutio.Api/Auth/RefreshTokenService.cs` — already supports family revocation; reuse it for
  "revoke all sessions on reset" rather than inventing a new mechanism.
- `tests/Homdutio.Api.Tests/AuthApiFactory.cs:27-41` — `ConfigureWebHost` injects in-memory config;
  the same hook swaps the real ACS sender for a capturing fake in tests.
- `web/src/app/auth/unauthorized.interceptor.ts:13-18` — new public endpoints MUST be added to
  `AUTH_ENDPOINTS` or their 400/401 will spuriously trigger a token refresh.
- `web/src/app/auth/register/register.component.ts:20-34` — `passwordPolicyValidator` lives here;
  the reset form needs it, so extract to a shared module.
- `web/src/app/auth/login/login.component.ts:48-58` — navigation-`state` notice+prefill pattern to
  mirror after a successful reset.

## What We're NOT Doing

- **No "change password while logged in"** — FR-020 is the forgotten-password path only.
- **No email confirmation / account-verification** email (PRD non-goal; reset is the only email).
- **No invite or notification emails** — the email surface stays reset-only.
- **No general-purpose email framework** — a minimal `IEmailSender` with one reset method only.
- **No background-job queue** — email sends synchronously within the request (stack has no jobs).
- **No custom email-domain authentication** — start on the ACS Azure-managed domain (instant, no
  DNS); revisit a custom verified domain only if deliverability needs it.
- **No production ACS resource provisioning / DNS** — creating the Communication Services + Email
  resource and connecting a domain is an ops step outside this code change.
- **No custom reset-token table** — Identity's stateless DataProtector token is used (decided).

## Implementation Approach

Three phases, each independently verifiable: (1) stand up the email abstraction + Azure
Communication Services implementation + config behind a fake-swappable seam, so nothing user-facing
depends on a live provider; (2) wire the token provider, the two endpoints, rate limiting, and session revocation,
proven end-to-end with a capturing fake email sender; (3) build the SPA flow on the established
auth conventions. Security defaults throughout: always-200 forgot-password, generic reset failures,
1-hour token, revoke-all-sessions on success, configurable (not Host-derived) link base URL.

## Critical Implementation Details

- **Token provider registration is load-bearing.** `UserManager.GeneratePasswordResetTokenAsync`
  throws "No IUserTwoFactorTokenProvider named 'Default' is registered" unless
  `.AddDefaultTokenProviders()` is added to the Identity builder. The 1-hour lifetime is set via
  `services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1))`;
  this governs all DataProtector tokens, which is fine since reset is the only one in use.
- **Reset tokens are not URL-safe.** Identity's token contains characters that break in a query
  string. Base64Url-encode the token when building the email link and decode it in the
  `reset-password` handler before calling `ResetPasswordAsync`. The email value must also be
  URL-encoded. A mismatch here surfaces as a generic "invalid token" — easy to misdiagnose.
- **Enumeration safety spans both endpoints.** `forgot-password` always returns the same 200 body
  regardless of whether the user exists or whether ACS accepted the message. `reset-password`
  returns one generic failure for both "user not found" and "invalid/expired token" — never a
  distinct "no such account" signal.
- **Middleware ordering.** `app.UseRateLimiter()` must be added to the pipeline (it is not present
  today); place it before endpoint routing/mapping so the forgot-password policy applies.

## Phase 1: Email Infrastructure (Azure Communication Services + abstraction + config)

### Overview

Introduce a minimal, reset-scoped email abstraction with an Azure Communication Services (ACS)
implementation and a test/dev fake, plus the configuration surface — with no endpoints or
user-facing behavior yet.

### Changes Required:

#### 1. Email sender abstraction

**File**: `src/Homdutio.Api/Email/IEmailSender.cs` (new)

**Intent**: Define the narrow seam the reset flow depends on, so the live provider can be swapped
for a fake in tests and Development. Keep it reset-shaped, not a general email API.

**Contract**: An interface exposing a single async method to send a password-reset email — inputs
are the recipient address and the fully-built reset link (link construction lives in the endpoint
layer, not the sender). Returns a result/bool the caller can log on failure. No snippet needed.

#### 2. ACS implementation

**File**: `src/Homdutio.Api/Email/AcsEmailSender.cs` (new)

**Intent**: Implement `IEmailSender` using the `Azure.Communication.Email` SDK (`EmailClient`),
composing the reset email (subject + plain-text and minimal HTML body containing the link) and
sending from the verified sender address on the connected domain. On a failed request, log and
return failure (the caller still returns 200 — enumeration safety).

**Contract**: Implements `IEmailSender`; constructor takes the ACS `EmailClient`, options, and an
`ILogger`. Reads the sender address from `AcsEmailOptions`. Sends via `SendAsync(WaitUntil.Started,
…)` so the request isn't blocked polling for terminal delivery. Add the `Azure.Communication.Email`
NuGet package to `Homdutio.Api.csproj`.

#### 3. Options + configuration keys

**File**: `src/Homdutio.Api/Email/AcsEmailOptions.cs` (new), `src/Homdutio.Api/appsettings.json`

**Intent**: Bind an `AcsEmail` config section (`Endpoint`, `SenderAddress`) — both **non-secret**, so
committed in `appsettings.json` like `Jwt:Issuer`/`Audience` — plus an `AppBaseUrl` key used to build
the reset link. Auth is by managed identity (no key/connection string). `appsettings.Development.json`
blanks `AcsEmail:Endpoint` so local dev and the test host fall back to the no-op sender.

**Contract**: `AcsEmailOptions { SectionName = "AcsEmail"; Endpoint; SenderAddress }` mirroring
`JwtOptions`. `AppBaseUrl` lives as a top-level config key. `appsettings.json` carries the real
non-secret `AcsEmail` `Endpoint`+`SenderAddress` and `AppBaseUrl`; `appsettings.Development.json`
overrides `Endpoint` to empty (→ no-op sender locally + tests).

#### 4. DI registration + dev/test fake

**File**: `src/Homdutio.Api/Program.cs`, `src/Homdutio.Api/Email/NoOpEmailSender.cs` (new)

**Intent**: Register `IEmailSender` → `AcsEmailSender` (with a singleton `EmailClient` built from the
endpoint + `DefaultAzureCredential`) and bind `AcsEmailOptions`. Provide a no-op/logging fake sender
so local Development (and the test factory) never call ACS; select it when no endpoint is configured.
This keeps `dotnet run` working without an ACS resource.

**Contract**: `Configure<AcsEmailOptions>(...)` + `AddScoped<IEmailSender, …>()` in `Program.cs`;
`NoOpEmailSender : IEmailSender` logs the link instead of sending. Selection by presence of
`AcsEmail:Endpoint`. The live client authenticates by Entra ID / managed identity (`DefaultAzureCredential`).

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- Existing tests still pass: `dotnet test`
- A unit test asserts the reset email body contains the provided link and targets the recipient

#### Manual Verification:

- `dotnet run` starts with no ACS endpoint configured and logs (not sends) via the fake sender
- With a real ACS endpoint + verified sender (and the host identity granted access to the ACS resource), a manual send lands in an inbox

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before
proceeding to the next phase. The `## Progress` section owns the checkbox state for these items.

---

## Phase 2: Backend Reset Endpoints

### Overview

Wire the token provider, add the two reset endpoints with security defaults, rate-limit the public
forgot-password endpoint, revoke active sessions on success, and prove it all with integration tests.

### Changes Required:

#### 1. Identity token provider + lifetime

**File**: `src/Homdutio.Api/Program.cs`

**Intent**: Enable password-reset token generation (`AddDefaultTokenProviders()`) and set the reset
link's validity to 1 hour. Without this the reset endpoints throw at runtime.

**Contract**: Append `.AddDefaultTokenProviders()` to the existing Identity builder chain
(`Program.cs:37-42`); add
`services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1))`.

#### 2. Rate limiter

**File**: `src/Homdutio.Api/Program.cs`

**Intent**: Cap abuse of the unauthenticated forgot-password endpoint (email bombing / ACS send
quota) using ASP.NET Core's built-in rate limiter, scoped to that endpoint only.

**Contract**: `builder.Services.AddRateLimiter(...)` defining a named fixed-window policy (e.g.
per-IP, a few requests per window) with a 429 rejection; `app.UseRateLimiter()` added to the
pipeline before endpoint mapping; the forgot-password endpoint carries `.RequireRateLimiting("…")`.

#### 3. Forgot-password endpoint

**File**: `src/Homdutio.Api/Auth/AuthEndpoints.cs`

**Intent**: Accept an email, and — only if a matching account exists — generate a reset token, build
the same-origin reset link from `AppBaseUrl`, and send it via `IEmailSender`. Always return the same
generic 200, regardless of account existence or send outcome (log send failures).

**Contract**: `POST /api/auth/forgot-password` taking `ForgotPasswordRequest(string Email)`; injects
`UserManager<ApplicationUser>`, `IEmailSender`, options/config for `AppBaseUrl`, `ILogger`. Always
`Results.Ok(...)`. Carries `.RequireRateLimiting("…")`. Base64Url-encode the token + URL-encode the
email when composing the link (see Critical Implementation Details).

#### 4. Reset-password endpoint

**File**: `src/Homdutio.Api/Auth/AuthEndpoints.cs`

**Intent**: Accept email + token + new password, decode the token, call
`UserManager.ResetPasswordAsync`. On success, revoke all of that user's refresh-token families so
every active session must re-authenticate. Return a single generic failure for unknown-user and
invalid/expired-token; surface Identity password-policy errors as `ValidationProblem`.

**Contract**: `POST /api/auth/reset-password` taking
`ResetPasswordRequest(string Email, string Token, string NewPassword)`; injects
`UserManager<ApplicationUser>` and `RefreshTokenService`. Success → `Results.Ok()`; weak password →
`Results.ValidationProblem(...)` (Identity codes); user-not-found/bad-token → one generic
`ValidationProblem`/400 with no enumeration signal. Base64Url-decode the token before
`ResetPasswordAsync`.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build`
- `dotnet test` green, including new integration tests:
  - forgot-password returns 200 for both a known and an unknown email (identical response)
  - a known email triggers the (fake) sender, capturing a non-empty link/token
  - reset-password with the captured token + a valid new password returns 200 and the new password logs in
  - reset-password with an invalid/expired token returns a generic failure (no enumeration)
  - reset-password with a weak new password returns `ValidationProblem`
  - after a successful reset, a previously issued refresh token is rejected (sessions revoked)
  - forgot-password is rate-limited (429 after the configured threshold)

#### Manual Verification:

- End-to-end against LocalDB + a real ACS sender: request → receive email → reset → log in
- A reset link older than 1 hour is rejected with the generic failure

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human before proceeding to Phase 3.

---

## Phase 3: SPA Reset Flow

### Overview

Build the user-facing request and set-new-password screens on the established SPA auth conventions,
wire routing and the interceptor allowlist, and link the flow from `/login`.

### Changes Required:

#### 1. Shared password-policy validator

**File**: `web/src/app/auth/password-policy.validator.ts` (new), `web/src/app/auth/register/register.component.ts`

**Intent**: Extract `passwordPolicyValidator` out of `register.component.ts` into a shared module so
the reset form reuses the exact same client-side policy; update register's import.

**Contract**: Move the exported `passwordPolicyValidator` function unchanged; `register.component.ts`
imports it from the new location. No behavior change.

#### 2. AuthService methods

**File**: `web/src/app/auth/auth.service.ts`

**Intent**: Add `requestPasswordReset(email)` → `POST /api/auth/forgot-password` and
`resetPassword(email, token, newPassword)` → `POST /api/auth/reset-password`. Neither touches
in-memory auth state (the user is logged out during this flow).

**Contract**: Two methods returning `Observable<void>`, following the existing `register()` shape.

#### 3. Interceptor allowlist

**File**: `web/src/app/auth/unauthorized.interceptor.ts`

**Intent**: Add `/api/auth/forgot-password` and `/api/auth/reset-password` to `AUTH_ENDPOINTS` so
their 400/401/429 responses render inline instead of triggering a silent refresh.

**Contract**: Two new entries in the `AUTH_ENDPOINTS` array.

#### 4. Request-reset component

**File**: `web/src/app/auth/forgot-password/forgot-password.component.ts` (+ template/styles, new)

**Intent**: A single-field email form that calls `requestPasswordReset` and always shows the same
generic "if an account exists, we've sent a link" confirmation (mirrors the backend's
non-enumeration). Mobile-first (≤400px).

**Contract**: Standalone component, reactive form (email required + email validator), success notice
signal, pending signal. Routed at `/forgot-password`.

#### 5. Set-new-password component

**File**: `web/src/app/auth/reset-password/reset-password.component.ts` (+ template/styles, new)

**Intent**: Read `email` and `token` from the query string, present a new-password form validated by
the shared `passwordPolicyValidator`, call `resetPassword`, and on success navigate to `/login` with
a "Password updated — please log in" notice and the email prefilled (reusing the navigation-`state`
pattern). Map server `ValidationProblem` errors via `mapValidationProblem`; show a generic message
for invalid/expired-token failures. Handle a missing/blank token by showing the generic error.

**Contract**: Standalone component, reactive form (new password + policy validator), reads
`route.snapshot.queryParamMap`, uses `mapValidationProblem`, navigates with
`state: { notice, email }`. Routed at `/reset-password`.

#### 6. Routing + login link

**File**: `web/src/app/app.routes.ts`, `web/src/app/auth/login/login.component.html`

**Intent**: Add public (unguarded) lazy routes for `/forgot-password` and `/reset-password`, and add
a "Forgot password?" link to the login page pointing at `/forgot-password`.

**Contract**: Two `loadComponent` route entries alongside `login`/`register`; a `routerLink` in the
login template. No guards (the user is logged out).

### Success Criteria:

#### Automated Verification:

- Frontend builds: `npm run build` (in `web/`)
- Lint passes: `npm run lint` (in `web/`)
- `npm test` (vitest) green, including new specs:
  - forgot-password component shows the generic confirmation after submit
  - reset-password component reads email+token from query, posts them, and on success navigates to `/login` with notice + prefilled email
  - reset-password maps a weak-password `ValidationProblem` to inline messages
  - reset-password shows the generic error for a missing/invalid token
  - both new endpoints are present in the interceptor's `AUTH_ENDPOINTS`

#### Manual Verification:

- Full path in a browser: `/login` → "Forgot password?" → submit email → open the emailed link →
  set a new password → land on `/login` (notice + prefilled email) → log in successfully
- All four screens are usable at ≤400px (NFR-2)
- An expired or tampered link shows the generic error, not an account-existence signal

**Implementation Note**: After completing this phase and all automated verification passes, pause
for manual confirmation from the human.

---

## Testing Strategy

### Unit Tests:

- Reset email composition includes the link and targets the recipient (Phase 1)
- Token Base64Url round-trip (encode in link build, decode in handler) — covered implicitly by the
  Phase 2 happy-path integration test

### Integration Tests (`tests/Homdutio.Api.Tests`):

- Register a capturing fake `IEmailSender` via the `AuthApiFactory` config/DI hook
- forgot-password: identical 200 for known vs unknown email; known email captures a link/token
- reset-password: happy path (new password logs in); invalid/expired token → generic failure;
  weak password → `ValidationProblem`; sessions revoked (prior refresh token rejected)
- rate limit: 429 after the configured threshold on forgot-password

### Manual Testing Steps:

1. Configure a real ACS endpoint + verified sender (host identity granted access to the ACS resource);
   request a reset; confirm the email arrives and the link opens the SPA reset page.
2. Set a new password; confirm redirect to `/login` with notice + prefilled email; log in.
3. Confirm a second device's session is logged out after the reset (refresh fails → `/login`).
4. Wait out the 1-hour token (or use a stale link) and confirm the generic invalid-token error.
5. Submit forgot-password for a non-existent email; confirm the identical generic confirmation.
6. Verify all screens at ≤400px.

## Performance Considerations

Negligible. The email send is synchronous within the request but reset volume is tiny (one user,
on demand). The rate limiter bounds worst-case email volume against the ACS send tier.
Session revocation reuses the existing indexed refresh-token family sweep.

## Migration Notes

No schema migration. `AddDefaultTokenProviders()` uses the stateless DataProtector (no DB table).
Operational prerequisites (not code): an Azure Communication Services resource with an Email
resource and a connected domain (Azure-managed or custom-verified); the App Service managed identity
granted access to the ACS resource (Entra ID auth — no connection string/key); and the
`AcsEmail:Endpoint`, `AcsEmail:SenderAddress`, and `AppBaseUrl` settings set per environment.

## References

- Roadmap slice: `context/foundation/roadmap.md` (S-08; provider switched SendGrid → Azure
  Communication Services Email on 2026-06-25)
- PRD: `context/foundation/prd.md` (FR-020)
- Prior auth work: `context/archive/2026-05-31-auth-identity-plumbing/plan.md`,
  `context/archive/2026-06-01-account-access/plan.md`
- Session revocation pattern: `src/Homdutio.Api/Auth/RefreshTokenService.cs`
- Test harness: `tests/Homdutio.Api.Tests/AuthApiFactory.cs`, `AuthEndpointsTests.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Email Infrastructure

#### Automated

- [x] 1.1 Solution builds: `dotnet build` — 447fbd3
- [x] 1.2 Existing tests still pass: `dotnet test` — 447fbd3
- [x] 1.3 Unit test asserts reset email body contains the link and targets the recipient — 447fbd3

#### Manual

- [x] 1.4 `dotnet run` starts with no ACS endpoint and logs (not sends) via the fake sender — 447fbd3
- [x] 1.5 With a real ACS endpoint + verified sender (host identity granted ACS access), a manual send lands in an inbox — 447fbd3

### Phase 2: Backend Reset Endpoints

#### Automated

- [x] 2.1 Solution builds: `dotnet build` — 6a7324a
- [x] 2.2 forgot-password returns identical 200 for known and unknown email — 6a7324a
- [x] 2.3 Known email triggers the fake sender, capturing a non-empty link/token — 6a7324a
- [x] 2.4 reset-password with captured token + valid new password returns 200 and new password logs in — 6a7324a
- [x] 2.5 reset-password with invalid/expired token returns a generic failure (no enumeration) — 6a7324a
- [x] 2.6 reset-password with a weak new password returns `ValidationProblem` — 6a7324a
- [x] 2.7 After a successful reset, a previously issued refresh token is rejected — 6a7324a
- [x] 2.8 forgot-password is rate-limited (429 after threshold) — 6a7324a

#### Manual

- [x] 2.9 End-to-end against LocalDB + real ACS: request → email → reset → log in — 6a7324a
- [x] 2.10 A reset link older than 1 hour is rejected with the generic failure — 6a7324a

### Phase 3: SPA Reset Flow

#### Automated

- [x] 3.1 Frontend builds: `npm run build` — a63f574
- [x] 3.2 Lint passes: `npm run lint` — a63f574
- [x] 3.3 forgot-password component shows the generic confirmation after submit — a63f574
- [x] 3.4 reset-password reads email+token from query, posts them, navigates to `/login` with notice + prefilled email on success — a63f574
- [x] 3.5 reset-password maps a weak-password `ValidationProblem` to inline messages — a63f574
- [x] 3.6 reset-password shows the generic error for a missing/invalid token — a63f574
- [x] 3.7 Both new endpoints present in the interceptor's `AUTH_ENDPOINTS` — a63f574

#### Manual

- [x] 3.8 Full browser path: login → forgot → email link → reset → login with notice + prefill → log in — a63f574
- [x] 3.9 All four screens usable at ≤400px — a63f574
- [x] 3.10 An expired/tampered link shows the generic error, not an account-existence signal — a63f574
