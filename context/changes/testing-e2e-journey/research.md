---
date: 2026-07-02T12:43:30+02:00
researcher: Rafal Michalak
git_commit: 5369dc641bbb4a9266782c98edb37631b10dcc74
branch: main
repository: run3th/homdutio
topic: "E2E journey + CI gate for the MVP cross-stack flow (Risk #4)"
tags: [research, codebase, e2e, playwright, ci, angular, dotnet, risk-4]
status: complete
last_updated: 2026-07-02
last_updated_by: Rafal Michalak
---

# Research: E2E journey + CI gate for the MVP cross-stack flow (Risk #4)

**Date**: 2026-07-02T12:43:30+02:00
**Researcher**: Rafal Michalak
**Git Commit**: 5369dc641bbb4a9266782c98edb37631b10dcc74
**Branch**: main
**Repository**: run3th/homdutio

## Research Question

For rollout Phase 5 (`testing-e2e-journey`, Risk #4): where does each of the 8
journey steps live across the stack, exactly how are the cross-stack seams
(invite auth hop + `returnUrl`, token storage/refresh, polling refresh, board
re-render) wired, what test infrastructure exists and what is needed to stand up
the running stack for a first Playwright layer, and where does the E2E gate slot
into `.github/workflows/deploy.yml`?

## Summary

The 8-step journey (register → create household → invite → join → claim → done →
admin-confirm) is fully implementable at the browser level today, and every seam
Risk #4 names is real and locatable:

- **Invite auth hop** uses a literal `returnUrl` query param with an open-redirect
  guard; `/join/:token` is public and derives one of five screens from a computed
  signal. This is the single most important seam to exercise with a *second*
  browser context.
- **Token model**: access token is **in-memory only**; the only persisted key is
  `localStorage['homdutio.refreshToken']`. A page reload relies on a startup
  silent refresh. Driving the UI login is the reliable E2E path — you cannot seed
  auth by setting an access token.
- **Polling** is a 4000 ms `interval` → refetch; **mutations refetch immediately**
  for the acting user but the *other* member only sees changes on the next poll.
  This is exactly the both-members-see-same-state assertion Risk #4 wants — wait
  on DOM state or `waitForResponse(/\/api\/tasks$/)`, **never** a fixed sleep.
- **Locators**: **zero `data-testid` in the codebase**. Everything is reachable
  by `getByRole` / `getByLabel` / `getByText` — the accessibility-first hierarchy
  in CLAUDE.md holds without adding testids.
- **Stack startup**: SPA on `:4200` (`ng serve`) proxies `/api` → API on `:5252`
  (`web/proxy.conf.json`). API needs a `ConnectionStrings__DefaultConnection`
  (LocalDB) and `Jwt:SigningKey` supplied out-of-band (no committed default),
  migrations applied manually (`dotnet ef database update` — **never** on
  startup), DB starts empty (no seeding). `GET /health` is the readiness probe.
- **No E2E layer exists.** `@playwright/test` is absent (only the unrelated
  `@vitest/browser-playwright` adapter is in the lockfile). A `playwright.config.ts`
  + `web/e2e/` dir + npm scripts must be added.
- **CI**: single `deploy.yml`; `build-test` on `windows-latest` (LocalDB) gates
  everything; `deploy` (migrate-first + `/health` smoke) is push-to-main only. The
  E2E gate is a new job depending on `build-test`, running on **`windows-latest`**
  (LocalDB is Windows-only) on **PR + push**, blocking PRs.

## Detailed Findings

### A. The 8-Step Journey — screens, routes, and endpoints

All frontend paths under `web/src/app`; routes at `web/src/app/app.routes.ts:6-66`.
Backend routes registered in `src/Homdutio.Api/Program.cs:181-185`.

| Step | Screen / Route | Component | API endpoint |
|------|----------------|-----------|--------------|
| 1. Register | `/register` (lazy) | `RegisterComponent` — `app.routes.ts:12-16` | `POST /api/auth/register` — `Auth/AuthEndpoints.cs:23` (empty 200; **no token issued** — must then log in) |
| — Login | `/login` (lazy) | `LoginComponent` — `app.routes.ts:8-11` | `POST /api/auth/login` — `AuthEndpoints.cs:40` → `LoginResponse(AccessToken, ExpiresAtUtc, RefreshToken)` `:223` |
| 2. Create household | `/create-household` (guards `authGuard`, `requireNoHousehold`) | `CreateHouseholdComponent` — `app.routes.ts:32-39` | `POST /api/households/` — `Households/HouseholdEndpoints.cs:41` → 201 `HouseholdResponse`; 409 if already in a household |
| 3. Invite (admin) | **No route — modal dialog** from topbar "Invite" | `InviteDialogComponent` — `topbar.component.ts:44-46`, `topbar.component.html:31` | `POST /api/households/invites` — `HouseholdEndpoints.cs:87` → `InviteResponse(Token, ExpiresAtUtc)`; body `RecipientEmail` **optional** (`:500`) |
| 4. Join | `/join/:token` — **public/unguarded** | `JoinComponent` — `app.routes.ts:59-64` | preview `GET /api/households/invites/{token}` `.AllowAnonymous()` `:156,187`; accept `POST /api/households/invites/{token}/accept` `:191` → adds caller as `Member` |
| 5. Claim | Board task card | `BoardComponent`/`TaskCardComponent` — `app.routes.ts:44-51` | `POST /api/tasks/{id:guid}/claim` — `Tasks/TaskEndpoints.cs:129` (ToDo→InProgress; 409 if not ToDo) |
| 6. Done | Board task card | ″ | `POST /api/tasks/{id:guid}/done` — `TaskEndpoints.cs:164` (403 if not claimer; 409 if not InProgress) |
| 7. Admin-confirm | Board task card | ″ | `POST /api/tasks/{id:guid}/confirm` — `TaskEndpoints.cs:203` (**admin-only** → 403; Done→closed, drops off board; 409 if not Done) |
| (pre-req) Create a task | Topbar "New task" → dialog | `CreateTaskComponent` — `topbar.component.html:33-36` | `POST /api/tasks/` — `TaskEndpoints.cs:74` → 201; lands unassigned in `ToDo` |
| (board read) | `GET /api/tasks/` — `TaskEndpoints.cs:26` | — | returns non-closed tasks + **server-computed affordance flags** (`CanClaim/CanMarkDone/CanConfirm/WillSelfAttest…`, `TaskResponse` `:737`) |

**Note**: the 8-step journey as written omits an explicit *create-task* step, but a
task must exist before it can be claimed — the E2E must create one (admin, via the
"New task" dialog) between household setup and claim.

### B. Seam 1 — the invite auth hop + `returnUrl` (the critical seam)

The query param is literally **`returnUrl`**.

- `/join/:token` is **public** (`app.routes.ts:59-64`). `JoinComponent` reads auth +
  membership itself and derives one of **five** screens via a computed `screen()`
  signal — `join.component.ts:63-80`.
- Unauthenticated → `screen()` returns `'joinLoggedOut'` (`join.component.ts:71-72`),
  rendering login/register links that carry the returnUrl:
  `join.component.html:40-46` — `[routerLink]="['/login']" [queryParams]="{ returnUrl }"`
  (and `/register`). The value: `get returnUrl() { return '/join/' + this.token; }`
  — `join.component.ts:100-103`. So the URL is `/login?returnUrl=/join/<token>`.
- **Login reads it back with an open-redirect guard** — `login.component.ts:36-41`:
  accepted only if it `startsWith('/')` and **not** `startsWith('//')`, else defaults
  to `/board`. On success: `router.navigateByUrl(this.returnUrl)` — `:78-82`.
- **Register forwards but does not auto-login** — `register.component.ts:28-31,66-76`:
  on success navigates to `/login` carrying `returnUrl` (+ prefill email via nav
  state). So the second user's real flow is: register → **redirected to login (returnUrl
  preserved)** → login → land back on `/join/<token>` → `'join'` screen → **Accept & join**.
- Server side: the join **link** is built only when emailing —
  `HouseholdEndpoints.cs:134-135`: `baseUrl = config["AppBaseUrl"]; link = baseUrl + "/join/" + token`.
  The API **response** returns just the raw `Token`; the SPA builds the relative
  `/join/<token>` route itself for the copy-link path.

### C. Seam 2 — token storage / refresh

Source: `web/src/app/auth/auth.service.ts`.

- **Access token: in-memory signal only, never persisted** — `_token` (`auth.service.ts:47`).
  A full reload logs the user out unless a refresh restores it.
- **Refresh token: one localStorage key** — `REFRESH_TOKEN_KEY = 'homdutio.refreshToken'`
  (`auth.service.ts:30`; r/w/clear at `:220-230`). **The only key relevant to E2E setup/teardown.**
- **Bearer interceptor** — `auth/bearer.interceptor.ts:10-21`.
- **Refresh-on-401** — `auth/unauthorized.interceptor.ts:29-71`: one silent `auth.refresh()`,
  replay once, else `logout()` + `/login`; auth endpoints excluded (`AUTH_ENDPOINTS` `:14-21`).
  `auth.refresh()` rotates the refresh token (`auth.service.ts:127-164`).
- **Startup silent restore** — `auth/session-restore.initializer.ts:20-33` (`provideAppInitializer`,
  5 s timeout) attempts one refresh if a token is stored.
- Backend: JWT HS256, `sub/email/jti` claims (`Auth/JwtTokenService.cs:21-29`), access
  lifetime **15 min** (`JwtOptions.cs:19`; appsettings.json:12), refresh **30 days**,
  rotate-on-use + replay detect + family revoke (`Auth/RefreshTokenService.cs:32,46`).
- **E2E implication**: seed auth by driving the **UI login** (setting an access token in
  storage won't work — it's in-memory). Long runs may cross the 15-min access-token
  boundary and exercise the refresh path; that's fine (and is part of Risk #4's seam set).

### D. Seam 3 — polling refresh interval

Source: `web/src/app/board/task.service.ts`, `board.component.ts`.

- Interval: **4000 ms** — `BoardComponent.POLL_INTERVAL_MS = 4000` (`board.component.ts:32`;
  kept under NFR-1's 5 s).
- Mechanism: RxJS `interval(ms)` → `switchMap(load)` → GET `/api/tasks` → set `_tasks`
  signal (`task.service.ts:101-110`, `load()` `:90-92`). Ticks **skipped** when
  `document.hidden` or paused.
- Started in `ngOnInit` (`board.component.ts:176-180`), torn down in `ngOnDestroy` (`:182-185`).
- **Paused during drag and while any dialog is open** — `setPaused` (`task.service.ts:118-121`),
  called across `board.component.ts` and `topbar.component.ts:38-41`.
- **E2E implication**: keep the observing page **focused/visible** (`document.hidden` suppresses
  ticks — a backgrounded Playwright page would stop polling). Wait on resulting DOM state or
  `page.waitForResponse(/\/api\/tasks$/)`.

### E. Seam 4 — board re-render after mutation

- **Not optimistic — refetch after every mutation.** Each action POSTs then
  `switchMap(() => this.load())` — `task.service.ts:129-141` (`claim`/`markDone`/`confirm`).
  A confirmed task stops coming back from `GET /api/tasks`, so it drops off the board.
- `BoardComponent.run()` (`board.component.ts:191-199`) also refetches on a stale 403/409 —
  the board self-heals.
- **Acting user's board updates immediately** (own refetch). **The other member's board only
  updates on the next 4 s poll** — no cross-member push. This *is* the both-members assertion.

### F. Locators for E2E (no `data-testid` exist — verified)

Everything reachable by role/label/text. Key targets:

- **Register** (`register.component.html`): `getByLabel('Email')` (16-17),
  `getByLabel(/Display name/)` (24-25, label includes "(optional)"), `getByLabel('Password')`
  (30-33), submit `getByRole('button', { name: /Create account/ })` (62-64).
- **Login** (`login.component.html`): `getByLabel('Email')`, `getByLabel('Password')`,
  `getByRole('button', { name: /Log in/ })` (46-48). Disambiguate from the Register link + a
  Show/Hide toggle by role+name. (Login and Register share "Email"/"Password" labels — scope by route/heading.)
- **Create household** (`create-household.component.html`): `getByLabel('Household name')` (15-16),
  `getByRole('button', { name: /Create household/ })` (22-24).
- **Invite (admin)**: topbar `getByRole('button', { name: 'Invite' })` (`topbar.component.html:31`);
  dialog `role="dialog"` (`invite-dialog.component.html:1-8`), `getByLabel('Invite by email')` (21-31),
  **Send** (32-34). **Read the link** from `<code id="invite-link">` (47-49) — e.g.
  `page.locator('#invite-link')` / `getByText(/\/join\//)` — and navigate the 2nd context to it
  (avoids clipboard permissions).
- **Join accept** (`join.component.html`): `getByRole('button', { name: /Accept & join/ })` (76-78);
  `'joinTaken'` → "Go to your board" (105); `'invalid'` → alert "This invite is no longer valid." (12).
- **Task card** (`task-card.component.html`): title button `getByRole('button', { name: title })`
  (72-76); **Claim** (112-114), **Mark done** (115-117), **Confirm** → use regex
  `getByRole('button', { name: /Confirm/ })` because `willSelfAttest` makes the label
  "Confirm (self-attested)" (118-122). "Awaiting confirmation" span (123-125) is a good observing-member
  assertion. Meta text "Created by **{name}**" / "Claimed by **{name}**" (85, 90) for cross-member checks.
- **Scoping within a card** (no per-card role): filter by title, e.g.
  `page.locator('app-task-card').filter({ hasText: title }).getByRole('button', { name: 'Claim' })`.
- **Columns** (`task-column.component.html`): headers "To do"/"In progress"/"Done"
  (`board.component.ts:38-42`); empty state "No tasks here." (11).
- **New task** (`create-task.component.html`): `getByLabel('Title')` (20-27), submit "Add task" (57-59);
  topbar `getByRole('button', { name: /New task/ })`.

### G. Running the stack + database (for a real-app E2E)

- **Ports**: API `http://localhost:5252` (http profile — `launchSettings.json:8`; https profile also
  binds 7105 `:17`). SPA `http://localhost:4200` (`ng serve`, Angular default — **no port override**
  in `angular.json`). Proxy `web/proxy.conf.json`: `/api` → `http://localhost:5252`, `changeOrigin`.
- **CORS**: **none configured** anywhere. The real app serves the SPA same-origin from `wwwroot`
  (`Program.cs:170-187`); in dev the proxy keeps requests same-origin. All SPA HTTP calls are relative
  `/api/...`.
- **DB (runtime)**: EF Core SQL Server; `ConnectionStrings:DefaultConnection` read lazily
  (`Program.cs:33-37`) and is **not in any committed appsettings** — supply via env var
  `ConnectionStrings__DefaultConnection` or user-secrets (LocalDB string). No default fallback → an
  unset string fails at first DB hit.
- **`Jwt:SigningKey`** likewise not committed — supply via user-secrets/env (validated at
  `Program.cs:71-86`).
- **Migrations are NEVER applied on startup** (`src/Homdutio.Data/MIGRATIONS.md:14`); run once:
  `dotnet ef database update --project src/Homdutio.Data --startup-project src/Homdutio.Api`.
- **No seeding** anywhere — DB starts empty; the journey builds all its own data (ideal for E2E).
- **Health**: `GET /health` — `Program.cs:180` (`AddDbContextCheck`, `:39-40`); returns 200 + "Healthy".
  Use as the E2E readiness probe.
- **Dev email**: `appsettings.Development.json` sets `AcsEmail:Endpoint = ""` → `NoOpEmailSender`
  (logs the link, sends nothing). **Drive invites via the response-body token**, not email.
- **Rate limit**: real `Invite` policy is **10 / 900 s per user** (appsettings.json:25-28). Keep E2E
  invite mints under 10 per 15 min.

### H. Existing test infrastructure + what's missing

- **Frontend** (`web/package.json`): npm@10.8.2 pinned; **no `.nvmrc`/global.json**. Scripts:
  `start`(ng serve), `build`, `test`(Vitest via `@angular/build:unit-test`), `lint`, `watch`,
  `prepare`(sets `core.hooksPath .githooks`). Angular 21.2.x, `@angular/build`. 33 colocated
  `*.spec.ts` unit tests (jsdom). **`@playwright/test` is ABSENT**; only `@vitest/browser-playwright`
  4.1.7 sits in the lockfile (unrelated Vitest adapter).
- **Backend**: `Homdutio.sln`, all `net9.0`, **no global.json**. Tests: `Homdutio.Api.Tests`
  (xUnit + `WebApplicationFactory`, per-run throwaway LocalDB via `AuthApiFactory`),
  `Homdutio.Data.Tests` (smoke). Run: `dotnet test`.
- **Test host divergences that matter for real-app E2E** (`AuthApiFactory.cs`): swaps in
  `CapturingEmailSender` (:57-61), no `AppBaseUrl`, `AccessTokenMinutes=120` (:50, vs real 15),
  raised rate limits (:33-36,51-52), throwaway migrated LocalDB (:24,65,83). None of this applies to
  the running app — E2E hits the real config.
- **Hooks/gates**: `.githooks/pre-commit` runs ESLint (staged) + `tsc -b --noEmit` over `web/` on every
  commit; `.claude/settings.json` PostToolUse runs `eslint --fix` on `web/` after every Write/Edit.
  New E2E TS must pass `tsc` (a `tsconfig.e2e.json` isolating Playwright types is advisable).
- **Must be added**: `@playwright/test`, `web/playwright.config.ts`, `web/e2e/` (specs + a seed spec
  per `/10x-e2e`), npm scripts (`e2e`, `e2e:ui`, `e2e:debug`), optional `tsconfig.e2e.json`.

### I. CI gate wiring (`.github/workflows/deploy.yml` — the only workflow)

- **Triggers**: PR to `main` + push to `main`; path-ignores `context/**`, `docs/**`, `**/*.md`.
- **Job graph**: `build-test` (gates everything) → `deploy` (push-to-main only, `needs: build-test`).
- **`build-test`** on **`windows-latest`** (for MSSQLLocalDB): setup .NET 9 + Node 22 → `sqllocaldb start
  MSSQLLocalDB` → `dotnet restore/build -c Release` (the Release build fires the `BuildAngularSpa`
  MSBuild target → `npm ci` + `ng build`, so a broken SPA build fails here) → `dotnet test -c Release
  --no-build` → `npm ci` + `npm test` (Vitest, jsdom) → `dotnet publish` → upload `app` artifact.
- **`deploy`** (windows-latest, push-to-main): `dotnet tool restore` → **apply EF migrations first**
  (`dotnet ef database update`, `ConnectionStrings__DefaultConnection = secrets.AZURE_SQL_CONNECTION_STRING`,
  with a run-scoped temporary SQL firewall rule opened/closed) → `azure/webapps-deploy` → **`/health`
  smoke** (poll `https://homdutio.azurewebsites.net/health` ×10 / 15 s, require 200 + "Healthy", else fail).
- **Deploy infra**: Azure App Service `homdutio` (framework-dependent, Linux), OIDC login
  (`AZURE_CLIENT_ID/TENANT_ID/SUBSCRIPTION_ID`), Azure SQL `homdutio-db`, `Jwt__SigningKey` as an App
  Service setting. SPA served from bundled `wwwroot`.
- **Where the E2E gate slots in** (test-plan §5: "e2e on the critical journey | CI on PR | required after
  Phase 5"):
  - New `e2e` job, `needs: build-test`, triggers on **PR + push** (blocks PRs), runner
    **`windows-latest`** — **LocalDB is Windows-only; the E2E job cannot run on ubuntu without a SQL
    Server container** (Option B: `mssql/server` container on ubuntu — more prod-like, slower, more
    moving parts). For Phase 5, Windows + LocalDB is the low-friction choice and matches `build-test`.
  - Shape: download the `app` artifact (or rebuild) → start/create an **isolated** LocalDB DB
    (run-scoped name, e.g. `homdutio_e2e_${GITHUB_RUN_ID}`, to avoid colliding with `build-test`/parallel
    PRs) → `dotnet tool restore` + `dotnet ef database update` on that DB → start the API (:5252) → start
    the SPA (`ng serve` :4200, or serve the built `wwwroot` same-origin) → gate readiness on `/health` →
    `npx playwright test` → teardown (stop API, drop DB). Mark it a required status check to block PRs.

## Code References

- `web/src/app/app.routes.ts:6-66` — route table (register/login/create-household/board/join)
- `web/src/app/join/join.component.ts:63-103` — 5-screen `screen()` signal + `returnUrl` builder
- `web/src/app/join/join.component.html:40-46,76-78` — logged-out links carrying `returnUrl`; Accept & join
- `web/src/app/auth/login/login.component.ts:36-41,78-82` — `returnUrl` read-back + open-redirect guard
- `web/src/app/auth/register/register.component.ts:28-31,66-76` — forwards `returnUrl`, no auto-login
- `web/src/app/auth/auth.service.ts:30,47,127-164,220-230` — in-memory token, `homdutio.refreshToken`, rotate
- `web/src/app/auth/unauthorized.interceptor.ts:29-71` — refresh-on-401 + replay-once
- `web/src/app/auth/session-restore.initializer.ts:20-33` — startup silent refresh
- `web/src/app/board/board.component.ts:32,176-199` — 4000 ms poll, run()/self-heal
- `web/src/app/board/task.service.ts:90-141` — polling + refetch-after-mutation
- `web/src/app/board/task-card/task-card.component.html:72-125` — claim/done/confirm locators
- `web/proxy.conf.json` / `web/angular.json:63-67` / `web/package.json:4-11` — dev server + proxy
- `src/Homdutio.Api/Program.cs:33-40,71-86,170-189` — DB/JWT config, `/health`, SPA fallback
- `src/Homdutio.Api/Auth/AuthEndpoints.cs:23,40,62-101` — register/login/refresh/logout/me
- `src/Homdutio.Api/Households/HouseholdEndpoints.cs:41,87,134-135,156-241,490-506` — household/invite/accept
- `src/Homdutio.Api/Tasks/TaskEndpoints.cs:26,74,129,164,203,737` — board/create/claim/done/confirm + affordance flags
- `src/Homdutio.Api/Properties/launchSettings.json:8,17` — http :5252 / https 7105
- `src/Homdutio.Data/MIGRATIONS.md:14` — migrations never on startup
- `tests/Homdutio.Api.Tests/AuthApiFactory.cs:24-65` — test-host divergences (email/JWT/rate-limit/DB)
- `.github/workflows/deploy.yml` — build-test gate + deploy (migrate-first + `/health` smoke)
- `.githooks/pre-commit` + `.claude/settings.json` — commit/edit-time TS+ESLint gates

## Architecture Insights

- **Accessibility-first UI makes E2E cheap**: semantic HTML, labelled inputs, text-labelled buttons, zero
  testids → the CLAUDE.md locator hierarchy (role/label/text) works everywhere. Adding testids would be a
  regression against the project's own convention.
- **The "same board state for both members" invariant is a poll, not a push**: acting user refetches
  immediately; observer converges within ~4 s. The correct E2E discipline is state-waiting
  (`toBeVisible`, `waitForResponse(/\/api\/tasks$/)`), which is also exactly the anti-`waitForTimeout`
  rule in CLAUDE.md and the test plan's Risk #4 anti-patterns.
- **Auth is deliberately reload-fragile** (in-memory access token + refresh-on-startup). E2E must go
  through the UI login rather than injecting tokens — which happens to exercise the token/refresh seam
  Risk #4 targets.
- **Server-computed affordance flags** (`CanClaim/CanConfirm/WillSelfAttest`) mean the E2E can assert
  the *button the user should see* rather than re-deriving lifecycle rules — keeping the browser test
  about the cross-stack journey, not re-implementing the integration matrix (the Risk #4 anti-pattern).
- **Windows/LocalDB is the CI center of gravity**: `build-test` already runs on `windows-latest` because
  LocalDB is Windows-only. The E2E job inherits that constraint — the cheapest correct choice is to keep
  E2E on Windows + LocalDB rather than introduce a Linux SQL container.
- **Migrations-out-of-band is a hard project rule**: the E2E harness must apply migrations explicitly
  before launching the API, both locally and in CI (mirroring the deploy job's migrate-first step).

## Historical Context (from prior changes)

- `context/foundation/test-plan.md` §2 Risk #4, §2 Risk Response, §3 Phase 5, §5 Quality Gates — the
  charter: prove the 8-step journey against the real stack with both members observing the same board
  state; cheapest layer is e2e; anti-patterns = re-implementing integration coverage, asserting styling,
  brittle selectors. §4 Stack confirms "e2e: none yet". §7 excludes UI visual/snapshot tests.
- `context/foundation/test-plan.md:100-103` — the deliberate Phase 4→5 reorder (invite/token abuse stays
  `not started`; E2E driven first).
- `context/foundation/lessons.md` — the last-admin TOCTOU rule (Risk #3); not on the E2E path but a
  known-pattern prior (already actioned in Phase 3).
- Phase 1–3 cookbook notes (`test-plan.md` §6.1–§6.3) — establish the integration conventions the E2E
  layer must **not** duplicate.

## Related Research

- None prior for this change. `context/changes/testing-e2e-journey/change.md` is the only sibling artifact.
- Adjacent completed phases: `context/changes/testing-cross-household-isolation/`,
  `testing-lifecycle-guard-completeness/`, `testing-concurrency-audit-durability/` (integration layer).

## Open Questions

1. **E2E DB in CI — isolated LocalDB name vs container?** Windows + run-scoped LocalDB DB is
   recommended; confirm whether parallel PR runs on the same runner pool need name-scoping beyond
   `GITHUB_RUN_ID`.
2. **Serve the SPA via `ng serve` (:4200 + proxy) or the built `wwwroot` same-origin?** `ng serve`
   matches local dev and the proxy; serving `wwwroot` from the API matches production exactly (no proxy,
   no CORS question). The plan should pick one as the E2E `baseURL`.
3. **Two browser contexts vs two pages** for the both-members assertion — Playwright `browser.newContext()`
   per member is the clean isolation boundary (separate `localStorage`/refresh token); confirm in the plan.
4. **Access-token expiry during a run**: real lifetime is 15 min; a long journey may cross it and exercise
   refresh. Acceptable (it's a Risk #4 seam), but the plan should decide whether to assert on it explicitly.
5. **`AppBaseUrl` for the running app**: `appsettings.Development.json` sets `https://localhost:5001`
   (neither launch port). Irrelevant if the E2E uses the response-body token for `/join/<token>`; confirm
   no path depends on the emailed absolute link.
