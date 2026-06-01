---
project: Homdutio
version: 1
status: draft
created: 2026-05-29
updated: 2026-06-01
prd_version: 1
main_goal: market-feedback
top_blocker: capacity
---

# Roadmap: Homdutio

> Derived from `context/foundation/prd.md` (v1) + auto-researched codebase baseline (2026-05-29).
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Vision recap

Homdutio is a shared-household chore board where every action — create, claim, mark done, confirm — is attributed to a named member with a timestamp, and a "Done" task only closes once an admin confirms it. The core hypothesis (the single belief the whole product rides on) is *accountability via record*: making who-did-what socially visible, with admin confirmation as the felt moment, is what stops the same person quietly carrying the visible work. v1 is built for the builder's own household first; multi-household, child roles, and aggregation views are deliberately v2.

## North star

**S-03: a household member runs a task from creation through admin-confirmed closure, leaving a durable record** — admin confirmation is the felt accountability event, so a working create → claim → done → confirm loop is the validation milestone for the entire product.

> "North star" here means the smallest end-to-end slice whose successful delivery would prove the core product hypothesis — placed as early as its prerequisites allow, because everything else only matters if this loop works. It sits behind the auth + household + board chain (S-01 → S-02), so those come first; the loop is then sequenced immediately, ahead of secondary task operations, per the `market-feedback` goal.

## At a glance

| ID    | Change ID                     | Outcome (user can …)                                                  | Prerequisites | PRD refs                                          | Status   |
| ----- | ----------------------------- | --------------------------------------------------------------------- | ------------- | ------------------------------------------------- | -------- |
| F-01  | persistence-baseline          | (foundation) EF Core + provisioned Azure SQL wired; data persists     | —             | NFR-3                                             | done     |
| F-02  | auth-identity-plumbing        | (foundation) ASP.NET Identity + JWT bearer pipeline issuing tokens     | F-01          | Access Control                                    | done     |
| F-03  | live-update-transport         | (foundation) board mutations propagate to other members within 5s     | —             | NFR-1                                             | ready    |
| F-04  | ci-auto-deploy                | (foundation) merge → build + smoke-gate → deploy, no manual zip        | —             | tech-stack ci_default_flow                        | done     |
| S-01  | account-access                | register, log in, and log out                                          | F-01, F-02    | FR-001, FR-002, FR-003                            | done     |
| S-02  | household-and-board           | create a household (become admin) and see the empty shared board       | S-01          | FR-004, FR-017, NFR-2                             | proposed |
| S-03  | accountability-loop           | create → claim → mark done → admin-confirm a task into a closed record | S-02          | US-01, FR-010, FR-013, FR-014, FR-015, FR-016, FR-018, NFR-3 | proposed |
| S-04  | task-management-and-priority  | edit, delete, and reorder tasks to manage and prioritise the backlog   | S-03          | FR-011, FR-012, FR-021                            | proposed |
| S-05  | loop-recovery                 | unclaim a stuck task; admin can send sloppy work back with a comment   | S-03          | FR-022, FR-023                                    | proposed |
| S-06  | invite-and-multiplayer-board  | invite a second adult who joins and shares one live board              | S-02, F-03    | US-02, FR-005, FR-006, FR-007, NFR-1              | proposed |
| S-07  | household-data-isolation      | be certain no one sees another household's tasks                       | S-03          | US-02, FR-019                                     | proposed |
| S-08  | password-reset                | reset a forgotten password via an emailed link                         | S-01          | FR-020                                            | proposed |
| S-09  | member-administration         | (admin) promote a member to admin and remove a member                  | S-06          | FR-008, FR-009                                    | proposed |
| S-10  | session-persistence           | stay logged in across a page reload (refresh-token flow)               | S-01          | Access Control                                    | proposed |

## Streams

Navigation aid — groups items that share a Prerequisites chain. Canonical ordering still lives in the dependency graph below; this table is the proposed reading order across parallel tracks.

| Stream | Theme                  | Chain                                                       | Note                                                                          |
| ------ | ---------------------- | ----------------------------------------------------------- | ----------------------------------------------------------------------------- |
| A      | Accountability spine   | `F-01` → `F-02` → `S-01` → `S-02` → `S-03` → `S-04` / `S-05` | The critical path to the north star (`S-03`); `S-04`/`S-05` branch off `S-03`. |
| B      | Multiplayer & freshness| `F-03` → `S-06` → `S-09`                                     | Branches off `S-02`'s board; transport decided (polling), so F-03 is buildable.|
| C      | Hardening & recovery   | `S-07` / `S-08` / `S-10`                                    | `S-07` hardens `S-03`; `S-08` and `S-10` extend `S-01`. All run parallel to the spine. |
| D      | Delivery automation    | `F-04`                                                      | Standalone infra; parallel with everything, gates no slice.                    |

## Baseline

What's already in place in the codebase as of 2026-05-29 (auto-researched + user-confirmed).
Foundations below assume these are present and do NOT re-scaffold them.

- **Frontend:** present — Angular 21 SPA in `web/`, built into `wwwroot/` via the `BuildAngularSpa` MSBuild target; router wired but only the default shell (no feature UI, no component/drag-reorder libs). Tests: vitest.
- **Backend / API:** partial — ASP.NET Core .NET 9 minimal API (`src/Homdutio.Api/Program.cs`); only the template `/weatherforecast` endpoint + SPA fallback. No `/api` controllers or domain routes.
- **Data:** present (F-01, 2026-05-30) — EF Core 9 + `ApplicationDbContext` in a `Homdutio.Data` library, `InitialCreate` migration applied to LocalDB + Azure SQL Basic (`homdutio-db`), connection string via user-secrets (local) / App Service connection-strings (prod). Schema is plumbing only (throwaway `SchemaProbe`); real domain tables arrive with their slices.
- **Auth:** present — ASP.NET Core Identity on the EF store + HS256 JWT bearer pipeline (F-02, 2026-05-31): `register`/`login`/`me` endpoints in `src/Homdutio.Api/Auth/`, stateless tokens, Identity default password policy, signing key via App Service settings. SPA auth layer wired on top (S-01, 2026-06-01): in-memory token, bearer/401 interceptors, guard, login/register/home UI. JWT bearer supersedes `tech-stack.md`'s cookie-auth note. No refresh/revocation yet (deferred).
- **Deploy / infra:** present — Azure App Service live (B1, Poland Central, HTTPS-only) at `homdutio.azurewebsites.net`. Azure SQL Basic provisioned + wired (F-01, 2026-05-30). GitHub Actions pipeline live (F-04, 2026-06-01): push to `main` → build + test gate → migrate-first → deploy → `/health` smoke test, OIDC auth, replacing the manual zip step. Budget alert still pending (combined run-rate ≈ $18/mo).
- **Observability:** absent — default ASP.NET console logging only; no error tracking, structured logging, metrics, or dashboards.

## Foundations

### F-01: Persistence baseline

- **Outcome:** (foundation) EF Core and an `ApplicationDbContext` are wired to a provisioned Azure SQL database, with a runnable migration workflow and the connection string supplied via App Service settings — data persists.
- **Change ID:** persistence-baseline
- **PRD refs:** NFR-3 (a durable audit record requires a real database, not in-memory state)
- **Unlocks:** F-02 (Identity needs an EF store), S-01, and every data-bearing slice (S-02–S-09); satisfies NFR-3's durability precondition.
- **Prerequisites:** —
- **Parallel with:** F-04
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Sequenced first because nothing persists without it. Provisioned Basic SQL caps at 2 GB / 5 DTU (infra risk register) and EF migrations do not auto-roll-back — keep migrations backward-compatible from day one. Scope is the DbContext + DB plumbing only; domain entities arrive with the slices that need them.
- **Delivered (2026-05-30):** EF Core 9 + `ApplicationDbContext` wired (split `Homdutio.Data` library); `InitialCreate` migration applied to both LocalDB and the provisioned Azure SQL **Basic** `homdutio-db`; `/health` DB-connectivity check + xUnit smoke test in place; connection string lives only in user-secrets (local) and App Service connection-strings (prod). Provisioning recorded in `deploy-plan.md` (commit `74225da`). **Carry-over RESOLVED (2026-06-01):** prod `/health` now verified `Healthy` against Azure SQL via the F-04 CI deploy (App Service ↔ SQL DbContext connectivity confirmed). Budget alert still pending (now recommended; combined run-rate ≈ $18/mo).
- **Status:** done

### F-02: Auth + identity plumbing

- **Outcome:** (foundation) ASP.NET Core Identity is mounted on the EF store as the user store, and a JWT bearer pipeline issues and validates signed tokens — no user-facing pages yet, just the token endpoints, JWT validation middleware, and Identity tables.
- **Change ID:** auth-identity-plumbing
- **PRD refs:** Access Control (email + password, JWT bearer tokens, one account per person)
- **Unlocks:** S-01 (register/login/logout flow) and the household-scoped authorization that S-07 hardens.
- **Prerequisites:** F-01
- **Parallel with:** F-04
- **Blockers:** —
- **Unknowns:** —
- **Decision (2026-05-30):** JWT bearer tokens, not cookie sessions. Keeps the first-party Identity user store but makes the API stateless — sidestepping the data-protection key-ring logout-on-restart/scale-out footgun the cookie path carried (infra risk register). This supersedes `tech-stack.md`'s cookie-auth recommendation; the trade is that the Angular SPA now owns token storage and refresh.
- **Risk:** Stateless JWT removes the key-ring concern but moves responsibility to two new places: a **signing key/secret** (store in App Service settings / Key Vault, never the repo) and **SPA token handling** (storage + refresh, and avoiding XSS-exposed tokens). Logout becomes client-side token discard plus short token lifetimes (optionally a server-side revocation/refresh-token store if instant invalidation is needed — defer unless required). Scope is the auth pipeline only; it completes no user-facing capability on its own.
- **Delivered (2026-05-31):** ASP.NET Core Identity mounted on the EF store + HS256 JWT bearer pipeline (`src/Homdutio.Api/Auth/` — `AuthEndpoints.cs`, `JwtTokenService.cs`, `JwtOptions.cs`). Endpoints: `POST /api/auth/register` (200 / 400 `ValidationProblem`), `POST /api/auth/login` (200 `{ accessToken, expiresAtUtc }` / 401), JWT-protected `GET /api/auth/me`. Stateless, `MapInboundClaims=false`, Identity default password policy; lazy JWT/DB config with xUnit integration tests (`AuthEndpointsTests.cs`). 3 phases + epilogue, commit `c4e7059`. Consumed by S-01 with no backend changes.
- **Status:** done

### F-03: Live-update transport

- **Outcome:** (foundation) client-side polling refreshes the board on a short interval so mutations propagate to other household members within 5 seconds without manual refresh.
- **Change ID:** live-update-transport
- **PRD refs:** NFR-1 (cross-device freshness ≤ 5s)
- **Unlocks:** the live-board experience in S-06 (two members observing the same state); reduces the NFR-1 transport unknown for every board mutation.
- **Prerequisites:** —
- **Parallel with:** F-01, F-02, F-04
- **Blockers:** —
- **Unknowns:** —
- **Decision (2026-05-30):** Polling, not SignalR. The contract is "≤5s freshness," which a short poll interval meets on the single-instance B1 / single-household MVP with near-zero added complexity — no stateful connection and no scale-out backplane/key-ring concern (both deferred per infra risk register). This supersedes the re-run-interview SignalR lean and aligns with `tech-stack.md` `has_realtime: false`. SignalR remains the reversible upgrade path post-validation if freshness/UX demands push.
- **Risk:** Polling is chattier than push but trivially correct and cheap to run at single-household scale; cap the interval so server load stays bounded. Off the north-star critical path — the loop (S-03) is verifiable on one device.
- **Status:** ready

### F-04: CI auto-deploy

- **Outcome:** (foundation) a merge to main runs the Release build (which bundles the Angular SPA) behind a build + smoke-test gate, then deploys to App Service — replacing the manual Windows zip step.
- **Change ID:** ci-auto-deploy
- **PRD refs:** tech-stack.md `ci_default_flow: auto-deploy-on-merge`; infra risk register (coupled single artifact, botched-manual-deploy)
- **Unlocks:** a gated build + smoke-test verification path before every subsequent slice deploys — mitigates the single-coupled-artifact risk (a bad `ng build` taking API + UI down) and the `Compress-Archive` zip gotcha from `deploy-plan.md`.
- **Prerequisites:** —
- **Parallel with:** F-01, F-02, F-03, and all slices
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Low — manual zip deploy already works, so this is hygiene, not a gate. Without it, every after-hours ship is a hand-built zip with a known forward-slash pitfall on Windows; automating it removes that footgun. Use OIDC over a stored publish profile.
- **Delivered (2026-06-01):** `.github/workflows/deploy.yml` runs the full pipeline on push to `main` — Release build (bundling the Angular SPA) + .NET/Vitest tests → gated `Deploy (production)` job that opens a temporary SQL firewall rule, applies EF migrations (migrate-first, before code swap), deploys to App Service, and smoke-tests `/health`. Auth is OIDC (no stored publish profile). Shakeout fixed four issues in sequence: local `dotnet-ef` tool restore (manifest pin 9.0.9, not a global install), the prod Azure SQL connection-string secret, a PowerShell `${i}` parser bug in the smoke step, and App Service ↔ Azure SQL connectivity for the DbContext health check. End-to-end green: prod `/health` reports `Healthy` against Azure SQL.
- **Status:** done

## Slices

### S-01: Account access

- **Outcome:** A person can register an account with email + password, log in, and log out.
- **Change ID:** account-access
- **PRD refs:** FR-001, FR-002, FR-003
- **Prerequisites:** F-01, F-02
- **Parallel with:** F-03, F-04
- **Blockers:** —
- **Unknowns:** —
- **Risk:** The onboarding entry point — Success Criteria step 1 depends on it. Conventional Identity flow on the JWT pipeline from F-02; low novelty. Logout is a client-side token discard (no server session to drop), so keep token lifetimes short.
- **Delivered (2026-06-01):** Angular SPA auth layer on the F-02 backend — `provideHttpClient` + bearer/401 functional interceptors, `AuthService` (in-memory token signal, no persistent storage), functional `authGuard`, dev proxy. Reactive-form login/register with client validation + mapped `ValidationProblem` errors; **after register the user is routed to `/login`** with a success notice + prefilled email (explicit login, no auto-login — decided during planning); placeholder guarded home with client-side logout; starter shell removed. Mobile-first ≤400px (NFR-2); 25 vitest specs; Release build exercises `BuildAngularSpa`. 2 phases + epilogue, commit `3b318e2`. Not yet archived.
- **Status:** done

### S-02: Household and board

- **Outcome:** A logged-in user with no household can create one (becoming its first admin) and see the empty three-column kanban board, fully usable on a ≤ 400px phone screen.
- **Change ID:** household-and-board
- **PRD refs:** FR-004, FR-017, NFR-2
- **Prerequisites:** S-01
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Establishes the board surface the north star renders into. NFR-2 (mobile-first, no horizontal scroll at ≤ 400px) is set here and inherited by every later board slice; getting the responsive column layout right early avoids reworking it under the task cards.
- **Status:** proposed

### S-03: Accountability loop  *(north star)*

- **Outcome:** A household member can create a task, claim it, mark it done, and an admin can confirm it — closing the task off the board while a durable record (creator, claimer, confirmer, timestamps, `self-attested` flag) persists.
- **Change ID:** accountability-loop
- **PRD refs:** US-01, FR-010, FR-013, FR-014, FR-015, FR-016, FR-018, NFR-3
- **Prerequisites:** S-02
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** The assumption most likely to be wrong — whether the confirm step actually changes household behaviour — lives here, so it is sequenced as early as the auth + board chain allows (`market-feedback` goal). FR-015's cross-member confirm is only fully exercised once a second member exists (S-06); meanwhile FR-016's self-attested path lets a single admin verify the whole loop end-to-end on one device. NFR-3 means the record must outlive the visible card — design closure as a state transition, not a delete.
- **Status:** proposed

### S-04: Task management and priority

- **Outcome:** A member can edit a task's title/description/category and delete it while in "To do", and reorder tasks within a column so the top of "To do" reads as the priority.
- **Change ID:** task-management-and-priority
- **PRD refs:** FR-011, FR-012, FR-021
- **Prerequisites:** S-03
- **Parallel with:** S-05, S-07
- **Blockers:** —
- **Unknowns:** —
- **Risk:** FR-021 drag-reorder is the *only* priority surface in v1 (no priority field, by Non-Goal), and the shared order must be consistent across members. Drag-and-drop at ≤ 400px (NFR-2) is the fiddly part; edit/delete are constrained to "To do" so they stay simple.
- **Status:** proposed

### S-05: Loop recovery

- **Outcome:** The claimer of an in-progress task can unclaim it back to "To do", and an admin reviewing a "Done" task can send it back to "In progress" with a short comment, keeping the original claimer attached.
- **Change ID:** loop-recovery
- **PRD refs:** FR-022, FR-023
- **Prerequisites:** S-03
- **Parallel with:** S-04, S-07
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Closes the two failure modes the lifecycle creates — stuck-in-progress (unclaim) and sloppy-work disputes (send-back). Both transitions must extend, not overwrite, the audit trail (NFR-3). Off the north-star path but needed before real-household use to keep the board honest.
- **Status:** proposed

### S-06: Invite and multiplayer board

- **Outcome:** A member can generate a single-use invite link; a second adult opening it joins the household (creating an account in the same flow if needed, bound to exactly one household), and both members see the shared board update within 5 seconds.
- **Change ID:** invite-and-multiplayer-board
- **PRD refs:** US-02, FR-005, FR-006, FR-007, NFR-1
- **Prerequisites:** S-02, F-03
- **Parallel with:** S-04, S-05
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Turns the loop genuinely multiplayer — a real second member makes FR-015's cross-member confirm verifiable and delivers NFR-1 freshness via F-03's polling transport. Single-use link invalidation (FR-005) and one-household-per-user (FR-007) are the correctness-sensitive parts. Still gated on its prerequisites (S-02, F-03), but no longer blocked on an open decision.
- **Status:** proposed

### S-07: Household data isolation

- **Outcome:** A user can view, create, claim, or confirm only their own household's tasks; a request for a foreign household's task returns not-found (no existence leak) and invite tokens grant access to exactly one household.
- **Change ID:** household-data-isolation
- **PRD refs:** US-02, FR-019
- **Prerequisites:** S-03
- **Parallel with:** S-04, S-05, S-06
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Cross-household leakage is the worst-possible bug per the PRD guardrails. Scoping must be built into every query and endpoint from the first one in S-03 — this slice exists to *harden and verify* that boundary across all surfaces (foreign-ID not-found, invite-token scoping from S-06), not to add it as an afterthought.
- **Status:** proposed

### S-08: Password reset

- **Outcome:** A registered user can request a password-reset email and set a new password from the emailed link.
- **Change ID:** password-reset
- **PRD refs:** FR-020
- **Prerequisites:** S-01
- **Parallel with:** S-02, S-03, S-04, S-05, S-06, S-07
- **Blockers:** —
- **Unknowns:** —
- **Decision (2026-05-30):** SendGrid — chosen over Azure Communication Services Email because ACS requires a verified sender *domain*, whereas SendGrid offers single-sender verification (verify one from-address, no domain ownership needed), and its free tier (~100 emails/day) more than covers reset-only v1 volume. The trade is a third-party account + API key to manage (store in App Service settings / Key Vault, never the repo) rather than same-vendor co-location.
- **Risk:** The only email pipeline in v1 (every other path is out-of-band by Non-Goal) — keep it to one provider, reset-only, so it does not balloon into a general email surface. Single-sender verification is the one setup step; consider domain authentication later only if deliverability needs it.
- **Status:** proposed

### S-09: Member administration

- **Outcome:** An admin can promote an adult member to admin and remove an adult member from the household.
- **Change ID:** member-administration
- **PRD refs:** FR-008, FR-009
- **Prerequisites:** S-06
- **Parallel with:** S-07
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Both FRs are nice-to-have, so this is sequenced last and gates nothing on the north-star path. Needs members to manage, hence the S-06 prerequisite.
- **Status:** proposed

### S-10: Session persistence (refresh-token flow)

- **Outcome:** A logged-in user stays authenticated across a full page reload instead of being bounced to `/login`. The SPA keeps the access token in memory (no XSS-exposed storage) but, on startup, silently re-mints a short-lived access token from a persisted refresh credential — so a refresh, a reopened tab, or a returning session resumes without re-entering the password.
- **Change ID:** session-persistence
- **PRD refs:** Access Control (un-defers the refresh/revocation F-02 explicitly postponed)
- **Prerequisites:** S-01 (builds on the SPA in-memory-token auth layer — `AuthService`, bearer/401 interceptors, guard — and the F-02 token pipeline)
- **Parallel with:** S-02, S-03, S-04, S-05, S-06, S-07, S-08 (off the north-star path)
- **Blockers:** —
- **Unknowns:** Refresh-token transport + storage — **httpOnly cookie** (XSS-safe, but needs CSRF defense and a same-site/path decision) vs a refresh token in web storage (reintroduces the exact XSS exposure F-02 flagged). Lean httpOnly cookie; confirm during `/10x-plan`.
- **Risk:** F-02 deliberately deferred refresh/revocation and S-01 kept the access token in memory only, accepting logout-on-reload as the v1 trade. This slice removes that UX cost **without** reintroducing XSS exposure: the access token stays in memory; a server-side `refresh` endpoint plus an httpOnly refresh cookie and a startup silent-refresh restore the session. It also adds the server-side token store F-02 noted as optional, which turns logout into real server-side revocation rather than a client-only token discard. Pure UX + security hardening; gates nothing on the north star. Watch token-rotation/replay handling and a clean expiry path (refresh expired → land on `/login`, no redirect loop).
- **Status:** proposed

## Backlog Handoff

| Roadmap ID | Change ID                     | Suggested issue title                                      | Ready for `/10x-plan` | Notes |
| ---------- | ----------------------------- | ---------------------------------------------------------- | --------------------- | ----- |
| F-01       | persistence-baseline          | Wire EF Core + provisioned Azure SQL persistence baseline   | done                  | Delivered 2026-05-30 (`74225da`); prod `/health` verify deferred to F-04 |
| F-02       | auth-identity-plumbing        | Mount ASP.NET Identity + JWT bearer pipeline                | done                  | Delivered 2026-05-31 (`c4e7059`); JWT, not cookie sessions; consumed by S-01 |
| F-03       | live-update-transport         | Establish 5s live-update transport via polling              | yes                   | Transport decided: polling (Open Q #1 resolved) |
| F-04       | ci-auto-deploy                | GitHub Actions build+smoke gate + auto-deploy on merge      | done                  | Delivered 2026-06-01; prod `/health` green against Azure SQL |
| S-01       | account-access                | Register, log in, log out                                   | done                  | Delivered 2026-06-01 (`3b318e2`); not yet archived |
| S-02       | household-and-board           | Create household + empty mobile-first kanban board          | no                    | Needs S-01 |
| S-03       | accountability-loop           | Task lifecycle: create → claim → done → admin-confirm        | no                    | North star; needs S-02 |
| S-04       | task-management-and-priority  | Edit/delete tasks + drag-reorder priority                   | no                    | Needs S-03 |
| S-05       | loop-recovery                 | Unclaim + admin send-back with comment                      | no                    | Needs S-03 |
| S-06       | invite-and-multiplayer-board  | Single-use invite, join, live shared board                  | no                    | Needs S-02, F-03 (polling) |
| S-07       | household-data-isolation      | Enforce + verify no cross-household leakage                 | no                    | Needs S-03; worst-bug guardrail |
| S-08       | password-reset                | Password reset via emailed link                             | no                    | Needs S-01; email via SendGrid (Open Q #2 resolved) |
| S-09       | member-administration         | Admin promote / remove member                               | no                    | Nice-to-have; needs S-06 |
| S-10       | session-persistence           | Refresh-token flow: survive reload, no re-login             | yes                   | Needs S-01 (done); un-defers F-02 refresh/revocation; lean httpOnly cookie |

This table is the clean handoff to Jira/Linear or any MCP-backed backlog. One row per `F-NN` and `S-NN`.

## Open Roadmap Questions

The PRD's own `## Open Questions` are all marked RESOLVED, so none carry forward. These are cross-cutting decisions surfaced during sequencing — both now resolved (2026-05-30):

1. **Live-update transport — polling vs SignalR for the NFR-1 5s freshness contract.** ✅ RESOLVED (2026-05-30): **Polling.** A short poll interval meets the ≤5s contract on the single-instance B1 / single-household MVP without a stateful connection or scale-out backplane/key-ring concern, and aligns with `tech-stack.md` `has_realtime: false`. Supersedes the re-run-interview SignalR lean; SignalR is the reversible post-validation upgrade if push is later warranted. Unblocked: F-03, S-06.
2. **Transactional email provider for password reset (FR-020).** ✅ RESOLVED (2026-05-30): **SendGrid.** Initially set to Azure Communication Services Email for co-location, but ACS requires a verified sender *domain*; SendGrid's single-sender verification (one from-address, no domain ownership) and ~100/day free tier fit reset-only v1 better. Trade: a third-party account + API key (store in App Service settings / Key Vault). Affects: S-08.

## Parked

- **Built-in priority / urgency algorithm** — Non-Goal: column order (FR-021) is the only priority surface; keeps the rule frozen on accountability.
- **Aggregation / reporting views ("who did what this week")** — Non-Goal: v2 surface; the audit trail is durable (NFR-3) but unaggregated in v1.
- **Invite emails / broader email pipeline** — Non-Goal: invite links shared out-of-band; password reset (S-08) is the only email.
- **Multi-household membership + switcher** — Non-Goal + FR-007: one household per user in v1; switcher is v2.
- **Child role and parent-managed accounts** — Non-Goal: two roles only in v1; child role + ownership-transfer flow is v2.
- **Notifications (push / in-app / email)** — Non-Goal: a v2 conversation.
- **Comments / multimedia / chat on tasks** — Non-Goal: turns tasks into messages; off-charter.
- **Recurring / scheduled tasks** — Non-Goal: adds a scheduler + template model that doubles scope.
- **AI-generated tasks or AI ranking** — Non-Goal: not part of the v1 accountability rule.
- **Gamification / points / leaderboard** — Non-Goal: engagement feature that doesn't serve accountability.
- **Native mobile app** — Non-Goal: browser-only; NFR-2 covers mobile use at ≤ 400px.
- **Offline mode** — Non-Goal: connectivity assumed; the 5s freshness NFR presumes a live connection.
- **Multi-region / HA commitment** — Non-Goal: single-region, single-instance is acceptable for one household.
- **Compliance certifications beyond GDPR hygiene** — Non-Goal: no SOC 2 / ISO / HIPAA attestation in v1.

## Done

(Empty on first generation. `/10x-archive` appends an entry here — and flips that item's `Status` to `done` — when a change whose `Change ID` matches the item is archived. Do NOT pre-populate.)
