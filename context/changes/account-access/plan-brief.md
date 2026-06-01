# Account Access (Register / Log in / Log out) — Plan Brief

> Full plan: `context/changes/account-access/plan.md`

## What & Why

The first user-facing slice (S-01): a person can register with email + password, log in, and log
out (FR-001/002/003). The F-02 auth backend is already implemented, so this is almost entirely the
Angular SPA layer that consumes it — and it sets the auth-state, token-handling, interceptor, guard,
routing, form, and error conventions that every later board slice inherits.

## Starting Point

The backend is done: `POST /api/auth/register` (200 / 400 `ValidationProblem`), `POST /api/auth/login`
(200 `{ accessToken, expiresAtUtc }` / 401), and a JWT-protected `GET /api/auth/me`, all on a
stateless HS256 bearer pipeline with Identity's default password policy. The frontend is the stock
Angular 21 starter: empty routes, placeholder welcome shell, **no `provideHttpClient`**, and no auth
service/interceptor/guard. Standalone components + signals + vitest are the house style.

## Desired End State

Unauthenticated visits redirect to `/login`. A new user registers, is **routed to `/login` with an
"account created — please log in" notice and their email prefilled**, then logs in and lands on a
protected home page showing their email with a **Log out** button. Login, logout, the guard redirect,
and a mid-session 401 → re-login all work; errors show inline (client) and mapped (server);
everything is usable at ≤ 400px (NFR-2).

## Key Decisions Made

| Decision                  | Choice                                   | Why (1 sentence)                                                              | Source |
| ------------------------- | ---------------------------------------- | ----------------------------------------------------------------------------- | ------ |
| Token storage             | In-memory signal                         | Avoids the XSS-readable persistent storage the roadmap warns about.           | Plan   |
| Post-login destination    | Placeholder authenticated home           | Proves guard+token end-to-end and gives a logout surface; S-02 replaces it.   | Plan   |
| Token expiry handling     | 401 interceptor → discard + `/login`     | Clean recovery with no refresh-token machinery (F-02 deferred refresh).       | Plan   |
| Logout scope              | Client-side token discard only           | Matches F-02's stateless-JWT design; no server session to drop.               | Plan   |
| After register            | Redirect to `/login` (notice + prefilled email) | Explicit login confirms credentials and keeps register a pure create step.   | Plan   |
| Error/validation UX       | Inline client checks + mapped server msgs| Fast feedback, no login credential leak, reuses Identity's messages.          | Plan   |
| Password rules            | Shown + client-validated to Identity policy | No surprise 400s; guides the first registration.                            | Plan   |
| Testing depth             | Vitest unit/component + reuse API tests  | Locks the new SPA conventions at the right layer without new E2E tooling.     | Plan   |

## Scope

**In scope:** HTTP client + dev proxy; `AuthService` (in-memory token); bearer + 401 interceptors;
auth guard; `/login` + `/register` reactive forms with validation and mapped errors; forced login
after register (redirect to `/login` with notice + prefilled email); placeholder guarded home with
logout; shell cleanup; vitest specs.

**Out of scope:** any backend change; refresh tokens / revocation; persistent token storage;
household/board (S-02); password reset (S-08); email confirmation; E2E browser tooling; external IdPs.

## Architecture / Approach

A root-provided `AuthService` holds the JWT in a signal and is the single source of auth state. A
**bearer interceptor** attaches the token to outgoing API calls; an **unauthorized interceptor**
turns protected-call 401s into a logout + `/login` redirect (ignoring the auth endpoints' own 401s).
A functional **auth guard** gates `/home`. The SPA calls relative `/api/...` URLs (same-origin in
prod; a dev proxy bridges `ng serve`). Two reactive-form components handle register/login; a shared
helper maps `ValidationProblem` bodies to messages.

## Phases at a Glance

| Phase                              | What it delivers                                              | Key risk                                              |
| ---------------------------------- | ------------------------------------------------------------ | ----------------------------------------------------- |
| 1. Core auth plumbing              | HttpClient, AuthService, bearer + 401 interceptors, guard, API config | 401 interceptor looping on auth-endpoint 401s         |
| 2. Auth UI, routing & home         | Routes, login/register forms, forced login after register, placeholder home, logout, shell cleanup | Mobile-first forms at ≤400px; server-error mapping fidelity |

**Prerequisites:** F-01 (done), F-02 (implemented); local API runnable; Angular toolchain present.
**Estimated effort:** ~2 sessions across 2 phases (plumbing, then UI).

## Open Risks & Assumptions

- In-memory token means a page reload logs the user out — accepted for v1 (no refresh until later).
- Client password validator duplicates Identity's server policy and must stay aligned if the server
  policy changes (noted in the plan).
- Dev proxy vs `environment.ts` for the API base is the implementer's call; relative URLs + proxy is
  preferred since prod is genuinely same-origin.

## Success Criteria (Summary)

- A new user can register, be sent to `/login` (notice + prefilled email), log in, see their email,
  and log out — end-to-end, mobile-first.
- Guard redirects unauthenticated `/home` visits to `/login`; a mid-session 401 cleanly re-routes to login.
- `npm test` (vitest) green across service, guard, both interceptors, and both form components.
