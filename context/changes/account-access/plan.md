# Account Access (Register / Log in / Log out) Implementation Plan

## Overview

Build the user-facing authentication layer for Homdutio in the Angular SPA, on top of the
already-implemented F-02 backend. A person can register an account with email + password, log in,
and log out. Concretely this slice wires `provideHttpClient`, an `AuthService` that holds the JWT
in an **in-memory signal**, a **bearer interceptor** (attach `Authorization`), a **401 interceptor**
(discard token + redirect to `/login`), an **auth guard**, two reactive-form components
(`LoginComponent`, `RegisterComponent`) with client validation + mapped server errors, **forced
login after register** (a successful register routes to `/login` with a success notice and prefilled
email), **client-side logout**, and a **placeholder authenticated home** that S-02 later
replaces. Every primary flow is usable at ≤ 400px (NFR-2). This is the foundational frontend slice:
the patterns set here (auth state, token handling, interceptors, guard, routing, form/error
conventions) are inherited by every later board slice.

## Current State Analysis

The auth backend is done; the frontend is a bare starter shell.

**Backend (F-02, implemented — do NOT modify in this slice):**

- `POST /api/auth/register` — body `{ email, password }`; 200 (empty body) on success, `400`
  `ValidationProblem` keyed by Identity error code (e.g. `DuplicateUserName`, `PasswordTooShort`)
  on failure (`src/Homdutio.Api/Auth/AuthEndpoints.cs:17-26`).
- `POST /api/auth/login` — body `{ email, password }`; 200 `{ accessToken, expiresAtUtc }` on
  success, `401` on bad credentials or unknown email (`AuthEndpoints.cs:28-48`).
- `GET /api/auth/me` — `.RequireAuthorization()`; 200 `{ sub, email }` with a valid bearer token,
  `401` otherwise (`AuthEndpoints.cs:50-54`).
- JWT is HS256, stateless, `MapInboundClaims=false`, claims `sub`/`email`/`jti`, lifetime
  `Jwt:AccessTokenMinutes` (`JwtTokenService.cs`, `Program.cs:49-72`). No refresh, no revocation.
- Identity default password policy is in force (`AddIdentityCore`, `Program.cs:35-40`): min length
  6, requires uppercase, lowercase, digit, and a non-alphanumeric character.

**Frontend (`web/`, stock Angular 21 starter):**

- `web/src/app/app.config.ts` — providers are only `provideBrowserGlobalErrorListeners()` +
  `provideRouter(routes)`. **No `provideHttpClient`.**
- `web/src/app/app.routes.ts` — `export const routes: Routes = [];` (empty).
- `web/src/app/app.ts` / `app.html` — placeholder starter component + the large Angular-welcome
  template; ends with `<router-outlet />` (`app.html:344`).
- No auth service, token storage, interceptor, or guard exist anywhere.
- Standalone components, signals, and the new control-flow syntax are the established idiom
  (`app.ts`, `app.html`). Tests run on **vitest** (`web/package.json:9,30`); `app.spec.ts` uses
  `TestBed`. `@angular/forms` is already a dependency (`package.json:17`).

**Build / serve topology:** Release builds the SPA into `wwwroot` via the `BuildAngularSpa` MSBuild
target (`Homdutio.Api.csproj:25-29`); `Program.cs` routes `/api/**` + `/health` to endpoints and
falls everything else back to `index.html` (`Program.cs:91-94`). In `ng serve` dev, the SPA and API
run on different origins, so the API base URL must be configurable (see Critical Implementation
Details).

### Key Discoveries:

- **Backend is complete and stays untouched** — the three endpoints and their DTOs
  (`AuthEndpoints.cs:60-66`) are the fixed contract this slice consumes. No C# changes are in scope.
- **Login returns no user identity, only a token** — to show "who am I" (e.g. email on the home
  page), the SPA decodes the JWT payload or calls `GET /api/auth/me`. The `sub`/`email` claims are
  un-remapped (`Program.cs:58` `MapInboundClaims=false`), so the raw claim names apply.
- **Register returns 200 with an empty body and no token** — registration does not authenticate the
  user; a successful register routes them to `/login` (with a success notice and prefilled email) to
  log in explicitly. No `login` call is made on the register path.
- **Server password policy is Identity's default** — the client-side validator and the on-form
  rules text must mirror: ≥6 chars, upper, lower, digit, non-alphanumeric (`Program.cs:35-40`).
- **`ValidationProblem` shape** — 400 responses are RFC-7807 `{ errors: { <code>: [msg, ...] } }`;
  the error-mapping helper keys off this. Login failure is a bare `401` (no body).
- **vitest + standalone + signals is the house style** — new components/services follow `app.ts`'s
  standalone pattern; tests follow `app.spec.ts`'s `TestBed` pattern, run by `npm test` (vitest).

## Desired End State

Visiting the app unauthenticated redirects to `/login`. A new user can open `/register`, see the
password rules, submit valid email + password, and be **routed to `/login` with an "account created
— please log in" notice and their email prefilled**; after logging in they land on a **protected home
page** showing their email and a **Log out** button. An existing user can log in at
`/login`. Logging out discards the in-memory token and returns to `/login`; navigating back to
`/home` while logged out redirects to `/login`. A token that expires mid-session causes the next
API call's `401` to discard auth state and redirect to `/login`. Invalid input shows inline
messages; a duplicate email or weak password on register shows the server's mapped message; bad
login credentials show a generic "invalid email or password". The whole flow works at ≤ 400px with
no horizontal scroll. `npm test` (vitest) is green, covering the service, guard, both interceptors,
and both form components.

**Verification:** `npm run build` (and a Release `dotnet build` exercising `BuildAngularSpa`)
succeeds; `npm test` is green; running the API + `ng serve` (or the built SPA) demonstrates
register → auto-login → home → logout, the guard redirect, the 401 redirect, and the error paths;
the placeholder Angular-welcome content is gone.

## What We're NOT Doing

- **No backend changes** — the F-02 endpoints, JWT config, and Identity policy are fixed inputs.
- **No refresh tokens / no server-side revocation** — single in-memory access token; logout and
  expiry are client-side per F-02's deferral. A page reload logs the user out (accepted for v1).
- **No persistent token storage** — not `localStorage`/`sessionStorage` (XSS exposure the roadmap
  warns about). In-memory signal only.
- **No household / board / task UI** — the post-login home is a deliberate throwaway placeholder;
  S-02 (`household-and-board`) replaces it.
- **No password reset** — that is S-08 (SendGrid).
- **No email confirmation** — `RequireConfirmedAccount=false` upstream; registration is immediate.
- **No E2E/browser test tooling** (Playwright/Cypress) — vitest unit/component tests + the manual
  gate cover this slice; E2E is a separate infra investment.
- **No external identity providers** — email + password only (PRD Access Control).
- **No prod deploy concerns beyond the existing CI** — F-04 already deploys on merge; this slice
  adds no new prod secret (the SPA needs none) but does depend on the prod `Jwt:SigningKey` already
  set when F-02's code deployed.

## Implementation Approach

Build bottom-up in two phases, each independently verifiable. Phase 1 stands up the non-visual auth
plumbing (HTTP client, `AuthService` with the in-memory token signal, the two interceptors, the
guard, API base config) and proves it with unit tests — no routes or components yet. Phase 2 adds
the routing table, the two reactive-form components, the placeholder home, logout, and the shell
cleanup, proving the full click-through flow and component tests. Tests are folded into each phase
so nothing ships unverified. All work is local (`ng serve` against the locally-run API); nothing
touches Azure or costs money.

## Critical Implementation Details

- **API base URL across origins** — under `ng serve` the SPA (e.g. `localhost:4200`) and API (e.g.
  `localhost:5xxx`) are different origins, but in production they are same-origin (SPA served from
  `wwwroot`). The cleanest fix is a **dev proxy** (`web/proxy.conf.json` mapping `/api` →
  the API origin, wired via `ng serve --proxy-config`) so the app can always call **relative**
  `/api/...` URLs and needs no environment-specific base. Alternatively use Angular `environment.ts`
  files. Pick one and document it; relative URLs + dev proxy is preferred because production is
  genuinely same-origin and it keeps the code origin-agnostic.
- **401 interceptor must not loop on the auth endpoints** — a `401` from `POST /api/auth/login` is a
  normal "bad credentials" result the login form handles, not a session-expiry signal. The 401
  interceptor must **ignore `/api/auth/login` and `/api/auth/register`** (let the component handle
  those) and only discard-and-redirect for `401`s on other (protected) calls.
- **Guard + redirect ordering** — the auth guard reads the `AuthService` signal synchronously
  (in-memory token), so no async resolution race; an unauthenticated hit on `/home` returns a
  `UrlTree` redirect to `/login`. Because the token is in-memory, after a full page reload the user
  is unauthenticated and the guard sends them to `/login` — expected.
- **Register → login handoff** — register does NOT auto-authenticate. On a 200, the app navigates to
  `/login` carrying two pieces of state: a success notice ("account created — please log in") and the
  just-registered email to prefill the login form. Pass this via the `Router` navigation `state`
  (read once through `getCurrentNavigation()` / `history.state` in `LoginComponent`) rather than query
  params, so the email never lands in the URL or browser history. The notice clears on the next
  navigation; the prefilled email is editable.

## Phase 1: Core Auth Plumbing (HTTP client, AuthService, interceptors, guard)

### Overview

Stand up everything the UI sits on, with no routes or components yet: HTTP client provider, the
`AuthService` (in-memory token signal + `register`/`login`/`logout` and a derived auth-state
signal), a bearer interceptor, a 401 interceptor, an auth guard, and the API-access config (dev
proxy or environment). Prove each with vitest unit tests.

### Changes Required:

#### 1. Provide the HTTP client

**File**: `web/src/app/app.config.ts`

**Intent**: Enable `HttpClient` app-wide and register the functional interceptors so every request
carries the bearer token and every protected `401` is handled centrally.

**Contract**: Add `provideHttpClient(withInterceptors([bearerInterceptor, unauthorizedInterceptor]))`
to the providers array (keep existing providers). Functional interceptors (not class-based) match
modern Angular idiom.

#### 2. AuthService with in-memory token

**File**: `web/src/app/auth/auth.service.ts` (new)

**Intent**: Single source of truth for auth state and the only place the token lives. Holds the JWT
in a private signal, exposes a public read-only auth-state signal, and wraps the three API calls.

**Contract**: An `@Injectable({ providedIn: 'root' })` service exposing:
- a private `signal<string | null>` token + a public computed/read-only `isAuthenticated`
  signal (and an `email`/identity signal derived from the logged-in user);
- `register(email, password): Observable<...>` → `POST /api/auth/register`;
- `login(email, password): Observable<...>` → `POST /api/auth/login`, on success stores the
  `accessToken` in the signal (and captures the user's email for display);
- `logout(): void` → clears the token signal (no API call) — caller handles navigation;
- a `token` accessor the bearer interceptor reads.
DTO interfaces (`LoginResponse { accessToken; expiresAtUtc }`, request bodies) live alongside.
The email shown on home can come from the login request's email or a `GET /api/auth/me` call —
decide in implementation; calling `me` is the more honest source.

#### 3. Bearer interceptor

**File**: `web/src/app/auth/bearer.interceptor.ts` (new)

**Intent**: Attach `Authorization: Bearer <token>` to outgoing API requests when a token is present.

**Contract**: A functional `HttpInterceptorFn` that reads the token from `AuthService` (via
`inject`); if present, clones the request adding the `Authorization` header; otherwise passes
through unchanged. No header is added when there is no token.

#### 4. Unauthorized (401) interceptor

**File**: `web/src/app/auth/unauthorized.interceptor.ts` (new)

**Intent**: Centralize session-expiry recovery — a `401` on a protected call discards auth state and
redirects to `/login`.

**Contract**: A functional `HttpInterceptorFn` that catches `HttpErrorResponse` with `status === 401`
**only for requests other than `/api/auth/login` and `/api/auth/register`**; on such a 401 it calls
`AuthService.logout()` and navigates to `/login` (via injected `Router`), then re-throws. Auth-endpoint
401s pass through untouched for the components to handle. (See Critical Implementation Details.)

#### 5. Auth guard

**File**: `web/src/app/auth/auth.guard.ts` (new)

**Intent**: Protect authenticated routes; send unauthenticated users to `/login`.

**Contract**: A functional `CanActivateFn` reading `AuthService.isAuthenticated()` synchronously;
returns `true` when authenticated, else returns a `UrlTree` redirect to `/login` (via injected
`Router`).

#### 6. API access configuration

**File**: `web/proxy.conf.json` (new) + `web/angular.json` serve config (or `web/src/environments/*`)

**Intent**: Let the SPA call relative `/api/...` URLs in both dev (different origin) and prod
(same origin) without per-environment base-URL branching.

**Contract**: Add a dev proxy mapping `/api` → the local API origin and wire it into the `serve`
target so `ng serve` proxies API calls; document the dev run command. (If the environment-file
approach is chosen instead, add an `apiBaseUrl` token and inject it where requests are built.)

### Success Criteria:

#### Automated Verification:

- Build clean: `npm run build` (from `web/`)
- Unit tests pass: `npm test` (vitest) — AuthService, bearer interceptor, unauthorized interceptor,
  and guard specs all green
- No persistent-storage usage: no `localStorage`/`sessionStorage` reference in `web/src/app/auth/**`

#### Manual Verification:

- With the API running and `ng serve --proxy-config` (or chosen config), an unauthenticated
  request to a protected call returns `401` and is observed redirecting to `/login` (verified in
  Phase 2 once routes exist; here confirm the interceptor wiring compiles and registers).
- Inspecting the running app shows the token is held only in memory (not in
  `localStorage`/`sessionStorage`).

**Implementation Note**: After automated verification passes, pause for manual confirmation that the
plumbing is wired correctly before building the UI in Phase 2.

---

## Phase 2: Auth UI, Routing & Placeholder Home

### Overview

Add the routing table, the `LoginComponent` and `RegisterComponent` (reactive forms with client
validation, password-rules display, and mapped server errors), the forced-login-after-register
handoff (notice + prefilled email), the guarded placeholder `HomeComponent` with logout, and replace
the placeholder Angular-welcome shell. Prove the full flow manually and with component vitest specs.
End state: the complete register → log in → log out experience, mobile-first.

### Changes Required:

#### 1. Routing table

**File**: `web/src/app/app.routes.ts`

**Intent**: Define the auth routes and protect the home route.

**Contract**: Routes for `'login'` → `LoginComponent`, `'register'` → `RegisterComponent`, `'home'`
→ `HomeComponent` with `canActivate: [authGuard]`, a default redirect (`''` → `'home'`), and a
wildcard redirect to a sensible default. Lazy `loadComponent` is optional but matches Angular idiom.

#### 2. Shared error-mapping helper

**File**: `web/src/app/auth/validation-problem.ts` (new)

**Intent**: Turn the backend `400 ValidationProblem` body into displayable messages so both forms
render server errors consistently.

**Contract**: A pure function taking an `HttpErrorResponse` and returning a flat list (or
field-keyed map) of messages from `error.error.errors` (RFC-7807 shape). Establishes the
server-error-display convention later slices reuse.

#### 3. LoginComponent

**File**: `web/src/app/auth/login/login.component.ts` + `.html` + `.scss` (new)

**Intent**: Let an existing user authenticate; on success route to `/home`.

**Contract**: Standalone component using `ReactiveFormsModule` with `email` (required + email
format) and `password` (required) controls. On init, reads the `Router` navigation `state` (see
Critical Implementation Details): if a post-register notice is present it shows an "account created —
please log in" banner and prefills the `email` control from the carried value. Submit calls
`AuthService.login`; on success navigates to `/home`; on `401` shows a single generic "invalid email
or password" message (no field-level leak); inline messages for client-invalid fields; a link to
`/register`. Mobile-first layout, no horizontal scroll at ≤ 400px. A submit/pending state disables
the button during the request.

#### 4. RegisterComponent

**File**: `web/src/app/auth/register/register.component.ts` + `.html` + `.scss` (new)

**Intent**: Let a new user create an account, then send them to `/login` to authenticate explicitly.

**Contract**: Standalone reactive-form component with `email` (required + email) and `password`
(required + a validator mirroring Identity's policy: ≥6 chars, upper, lower, digit, non-alphanumeric).
The password rules are displayed near the field. On submit: call `AuthService.register`; on 200,
navigate to `/login` passing navigation `state` with an "account created — please log in" notice and
the registered email for prefill (see Critical Implementation Details) — no auto-login, no token is
issued on this path. On `400`, render the mapped `ValidationProblem` messages (e.g. duplicate email,
weak password) via the shared helper. A link to `/login`. Mobile-first, ≤ 400px.

#### 5. Placeholder authenticated HomeComponent

**File**: `web/src/app/home/home.component.ts` + `.html` + `.scss` (new)

**Intent**: Give a real protected destination that proves the guard + token end-to-end and hosts
logout — explicitly a throwaway S-02 will replace.

**Contract**: Standalone component (guarded by `authGuard`) showing the signed-in user's email
(from `AuthService`) and a **Log out** button that calls `AuthService.logout()` then navigates to
`/login`. A short note marks it as a temporary placeholder for the S-02 household/board. Mobile-first.

#### 6. Replace the placeholder shell

**File**: `web/src/app/app.ts`, `web/src/app/app.html`, `web/src/app/app.scss`, `web/src/app/app.spec.ts`

**Intent**: Remove the Angular-welcome starter content so the app root is just the router outlet
(plus any minimal app chrome), and repoint the existing app spec.

**Contract**: Reduce `app.html` to the `<router-outlet />` (and optional minimal shell markup);
strip the large inline `<style>` welcome block; simplify `app.ts` (drop the `title` placeholder or
repurpose). Update `app.spec.ts` so it no longer asserts the "Hello, web" welcome text — assert the
shell renders / the outlet is present instead. No reference to the starter template remains.

### Success Criteria:

#### Automated Verification:

- Build clean: `npm run build` (from `web/`)
- Release build exercises the SPA bundle: `dotnet build Homdutio.sln -c Release` succeeds (runs
  `BuildAngularSpa`)
- Unit/component tests pass: `npm test` (vitest) — login, register (including auto-login + error
  mapping), and home/logout specs green; updated `app.spec.ts` green
- No starter content remains: `git grep -i "Congratulations" web/src` and a grep for the welcome
  pills return nothing

#### Manual Verification:

- Register a new account with valid credentials → redirected to `/login` with an "account created —
  please log in" notice and the email prefilled; logging in lands on `/home` showing the email;
  **Log out** returns to `/login`.
- Register with a weak password or an already-used email → the server's mapped message is shown; the
  password rules are visible before submitting.
- Log in with valid credentials → `/home`; log in with wrong credentials → generic "invalid email
  or password".
- While logged out, navigating directly to `/home` redirects to `/login`.
- After login, force a `401` on a protected call (e.g. let the token expire / tamper) → app discards
  auth and redirects to `/login`.
- All four primary screens (login, register, home) are fully usable at ≤ 400px with no horizontal
  scroll (NFR-2).
- `/health` and `/api/auth/*` are unaffected; the SPA fallback still serves the Angular routes.

**Implementation Note**: After automated verification passes, pause for manual confirmation that the
full register → auto-login → home → logout flow and the error/guard/expiry paths behave as expected
before closing the slice.

---

## Testing Strategy

### Unit Tests (vitest):

- `AuthService`: `login` stores the token (auth-state flips to authenticated); `logout` clears it;
  `register` posts to the right endpoint; token accessor returns the in-memory value.
- Bearer interceptor: adds `Authorization` when a token is present, omits it when absent.
- Unauthorized interceptor: a protected-call `401` triggers logout + `/login` navigation; a
  `/api/auth/login` 401 passes through untouched.
- Auth guard: returns `true` when authenticated, a `/login` `UrlTree` when not.
- `validation-problem` helper: maps an RFC-7807 body to the expected messages.

### Component Tests (vitest + TestBed):

- `LoginComponent`: invalid form blocks submit; 401 shows the generic message; success navigates;
  post-register navigation `state` shows the notice and prefills the email.
- `RegisterComponent`: password validator enforces Identity rules; 200 navigates to `/login` with the
  notice + email in navigation `state` (and issues no login call); 400 renders mapped server errors.
- `HomeComponent`: shows the email; logout clears state and navigates.

### Integration Tests:

- Reuse the existing `tests/Homdutio.Api.Tests/AuthEndpointsTests.cs` for the API contract
  (register → login → `/me`); no new backend tests in this slice.

### Manual Testing Steps:

1. Run the API locally; `ng serve --proxy-config` (or chosen config) for the SPA.
2. Register a fresh account → expect redirect to `/login` with an "account created — please log in"
   notice and the email prefilled; then log in → expect `/home` with the email.
3. Log out → expect `/login`; navigate to `/home` directly → expect redirect to `/login`.
4. Log in with those credentials → `/home`; log in with wrong password → generic error.
5. Register with a weak password and with a duplicate email → expect the server's mapped messages.
6. Narrow the viewport to ≤ 400px → all screens usable, no horizontal scroll.
7. Force token expiry / tamper, trigger a protected call → expect discard + `/login` redirect.

## Performance Considerations

Negligible. The SPA adds two lightweight interceptors and an in-memory signal; no per-request DB or
crypto cost beyond the backend's existing in-process JWT validation. The optional `GET /api/auth/me`
for the home email is a single cheap call. Bundle growth is minimal (two small components + forms,
already a dependency).

## Migration Notes

No data or schema migration. No new production secret is introduced by the SPA (it holds no key).
The slice assumes F-02's prod `Jwt:SigningKey` App Service setting is in place from the deploy that
carried the auth code. The post-login `HomeComponent` is intentionally disposable — S-02 replaces it
without migration concern.

## References

- Roadmap item: `context/foundation/roadmap.md` → S-01 (`account-access`); prerequisites F-01, F-02
- PRD: `context/foundation/prd.md` → FR-001, FR-002, FR-003; Access Control; NFR-2 (mobile ≤400px)
- Backend contract (consumed, not changed): `src/Homdutio.Api/Auth/AuthEndpoints.cs`,
  `src/Homdutio.Api/Auth/JwtTokenService.cs`, `src/Homdutio.Api/Program.cs:35-94`
- Prerequisite plan: `context/changes/auth-identity-plumbing/plan.md` (JWT decision, deferred
  refresh/revocation, Identity policy)
- SPA shell + build topology: `web/src/app/app.config.ts`, `web/src/app/app.routes.ts`,
  `web/package.json`, `src/Homdutio.Api/Homdutio.Api.csproj:25-29`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Core Auth Plumbing

#### Automated

- [x] 1.1 Build clean: `npm run build` (from `web/`) — 315b307
- [x] 1.2 Unit tests pass: `npm test` — AuthService, bearer interceptor, unauthorized interceptor, guard — 315b307
- [x] 1.3 No `localStorage`/`sessionStorage` reference in `web/src/app/auth/**` — 315b307

#### Manual

- [x] 1.4 Interceptor wiring compiles/registers; protected-call 401 redirect path confirmed (with Phase 2 routes)
- [x] 1.5 Token held only in memory (not in localStorage/sessionStorage) — 315b307

### Phase 2: Auth UI, Routing & Placeholder Home

#### Automated

- [x] 2.1 Build clean: `npm run build` (from `web/`)
- [x] 2.2 Release build exercises SPA bundle: `dotnet build Homdutio.sln -c Release` succeeds
- [x] 2.3 Unit/component tests pass: `npm test` — login, register (auto-login + error mapping), home/logout, updated app.spec
- [x] 2.4 No starter content remains: `git grep -i "Congratulations" web/src` and welcome-pills grep empty

#### Manual

- [x] 2.5 Register valid → `/login` with notice + prefilled email; log in → `/home` shows email; logout → `/login`
- [x] 2.6 Register weak password / duplicate email → server's mapped message shown; rules visible pre-submit
- [x] 2.7 Login valid → `/home`; login wrong creds → generic "invalid email or password"
- [x] 2.8 Logged out, direct nav to `/home` → redirect to `/login`
- [x] 2.9 Protected-call 401 (expiry/tamper) → discard auth + redirect to `/login`
- [x] 2.10 Login/register/home usable at ≤ 400px, no horizontal scroll (NFR-2)
- [x] 2.11 `/health` and `/api/auth/*` unaffected; SPA fallback serves Angular routes
