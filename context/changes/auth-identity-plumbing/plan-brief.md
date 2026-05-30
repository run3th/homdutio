# Auth + Identity Plumbing — Plan Brief

> Full plan: `context/changes/auth-identity-plumbing/plan.md`

## What & Why

Stand up the authentication foundation for Homdutio: mount ASP.NET Core Identity on the existing EF
store and issue/validate **stateless JWT bearer** tokens. This is foundation **F-02** — plumbing
only (token endpoints, JWT validation middleware, Identity tables). It unlocks S-01 (the user-facing
register/login/logout flow) and the household-scoped authorization S-07 later hardens.

## Starting Point

F-01 left a working `ApplicationDbContext` (plain `DbContext`, only a throwaway `SchemaProbe` table)
on LocalDB + provisioned Azure SQL. There is **no auth** anywhere: no Identity packages, no JWT
packages, no `UseAuthentication`/`UseAuthorization` in `Program.cs`, and the Angular app is a bare
shell with no HTTP client. This slice adds the auth layer on top of that store.

## Desired End State

The API exposes `POST /api/auth/register`, `POST /api/auth/login` (returns a signed JWT), and a
JWT-protected `GET /api/auth/me`. An unauthenticated `/me` returns 401; a valid bearer token returns
the caller's claims. The Identity (`AspNet*`) tables exist on LocalDB, the signing key lives only in
user-secrets, and an integration-test project proves the whole register → login → authorized-call
loop. No UI, no SPA wiring, no prod deploy.

## Key Decisions Made

| Decision                  | Choice                                              | Why (1 sentence)                                                                 | Source |
| ------------------------- | --------------------------------------------------- | -------------------------------------------------------------------------------- | ------ |
| Token type                | Stateless JWT bearer (not cookies, not opaque)      | Locked upstream; keeps API stateless and sidesteps the cookie key-ring footgun.  | Roadmap |
| Auth surface              | `AddIdentityCore` + hand-minted JWT (not `MapIdentityApi`) | `MapIdentityApi` issues opaque tokens, contradicting the JWT decision.   | Plan |
| User entity               | Custom empty `ApplicationUser : IdentityUser`       | Future household FK / metadata become additive migrations, no type swap.         | Plan |
| Token/session model       | Single moderate-lifetime access token, no refresh store | Roadmap defers refresh/revocation; logout = client-side discard.             | Roadmap/Plan |
| Endpoints shipped         | register + login + protected `/me` probe            | register makes login testable; `/me` proves the validation middleware gates.     | Plan |
| Email confirmation        | Disabled (`RequireConfirmedAccount = false`)        | Only v1 email is password reset (S-08); PRD cuts every other email pipeline.     | Plan |
| Tests                     | New `Homdutio.Api.Tests` integration project (WebApplicationFactory + LocalDB) | Only way to prove the real middleware; reusable harness.      | Plan |
| SchemaProbe               | Removed this slice (destructive migration)          | Identity tables are now real; placeholder's job is done.                         | Plan (user) |
| SPA scope                 | Backend-only                                        | Roadmap boundary: "no user-facing pages yet" — that's S-01.                      | Roadmap |

## Scope

**In scope:** Identity EF stores + `ApplicationUser`; `IdentityDbContext` switch + `AddIdentity`
migration (drops `SchemaProbes`); JWT issue/validate pipeline + signing-key config; register/login/`me`
endpoints; auth middleware; integration tests; repointed F-01 data-layer smoke test.

**Out of scope:** Angular UI / token storage / interceptor (S-01); refresh tokens & revocation; roles &
household scoping (S-02/S-07); email confirmation & password reset (S-08); production deploy / prod
migration apply; external identity providers.

## Architecture / Approach

`ApplicationDbContext` becomes `IdentityDbContext<ApplicationUser>` (single shared store). `Program.cs`
registers `AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<…>().AddSignInManager()` plus
`AddAuthentication().AddJwtBearer(…)` with issuer/audience/key from config. A `JwtTokenService` mints
HS256 JWTs (`sub`, `email`, `jti`, `exp`). Three minimal-API endpoints under `/api/auth`; `/me` carries
`.RequireAuthorization()`. `UseAuthentication`/`UseAuthorization` run before the SPA fallback.

## Phases at a Glance

| Phase                              | What it delivers                                            | Key risk                                                             |
| ---------------------------------- | ----------------------------------------------------------- | ------------------------------------------------------------------- |
| 1. Identity store + migration      | Identity schema on LocalDB; `SchemaProbe` removed; test repointed | Destructive `SchemaProbes` drop; must call `base.OnModelCreating`. |
| 2. JWT pipeline + endpoints        | Working register/login/`me` with JWT issue + validate       | Middleware ordering vs SPA fallback; 256-bit key length; secret hygiene. |
| 3. Integration test project        | `WebApplicationFactory` proof of the full auth loop          | `public partial class Program;` needed; test-host config overrides. |

**Prerequisites:** F-01 (done) — EF store + migration workflow in place.
**Estimated effort:** ~1–2 sessions across 3 phases (well-trodden ASP.NET patterns; biggest call already locked).

## Open Risks & Assumptions

- **Signing-key hygiene** — a leaked/committed key makes tokens forgeable; mitigated by user-secrets locally and a documented App Service setting for prod (never the repo).
- **No revocation** — a stateless token is valid until expiry; acceptable at single-household scale, revisit if instant logout is ever required (refresh/revocation deferred).
- **Prod migration deferred** — `AddIdentity` is not applied to Azure SQL in this slice; it rides the next deploy (F-04 or manual), same posture as F-01's deferred prod verify.
- **Password policy** — Identity defaults kept; S-01 may revisit UX/validation.

## Success Criteria (Summary)

- A new account can be registered, then logged in to receive a signed JWT.
- That JWT grants access to a protected endpoint; absence/invalidity yields 401.
- The full register → login → authorized-call loop passes in automated integration tests against LocalDB, with no secret in any tracked file.
