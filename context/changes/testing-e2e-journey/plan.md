# E2E Journey + CI Gate for the MVP Cross-Stack Flow (Risk #4) — Implementation Plan

## Overview

Stand up the project's **first Playwright E2E layer** and use it to prove **Risk #4** from `context/foundation/test-plan.md`: the full **register → create-household → invite → join → claim → done → admin-confirm** journey holds across the real running stack, with **both members observing the same board state** at each key transition. Then wire a **blocking E2E gate** into CI.

This is rollout **Phase 5** of the test plan ("End-to-end journey + gate wiring"). It is split into three implementation phases so the work routes to the right skill:

- **Phase 1 (scaffolding)** and **Phase 3 (CI gate)** are infrastructure — driven by `/10x-implement`.
- **Phase 2 (the journey test)** is the one browser-level risk — driven by `/10x-e2e`, which will create its two quality levers (seed spec + E2E rules), generate the test, review it against the five anti-patterns, and break-verify it.

## Current State Analysis

- **No E2E layer exists.** `@playwright/test` is absent from `web/package.json`; only the unrelated `@vitest/browser-playwright` adapter sits in the lockfile. There is no `playwright.config.*` and no `web/e2e/` directory. (`research.md` §H; `test-plan.md` §4 confirms "e2e: none yet".)
- **The 8-step journey is fully browser-drivable today.** Every step maps to a real screen/route + endpoint (`research.md` §A). Note: the journey as named omits an explicit *create-task* step, but a task must exist before it can be claimed — the E2E creates one (admin, via the "New task" dialog) between household setup and claim.
- **Auth is reload-fragile by design** (`research.md` §C): the access token is an **in-memory signal only**; the sole persisted key is `localStorage['homdutio.refreshToken']`. You **cannot** seed auth by injecting an access token — the reliable E2E path is driving the **UI login**, which itself exercises the token/refresh seam Risk #4 names.
- **The invite auth hop uses a literal `returnUrl`** (`research.md` §B): `/join/:token` is public and derives one of five screens from a computed `screen()` signal. An unauthenticated visitor gets login/register links carrying `returnUrl=/join/<token>`; login reads it back behind an open-redirect guard; register forwards to login (no auto-login). The second member's real flow is **register → redirected to login (returnUrl preserved) → login → land on `/join/<token>` → Accept & join**.
- **"Both members see the same state" is a poll, not a push** (`research.md` §D/§E): the board polls `GET /api/tasks` every **4000 ms**; a mutation refetches immediately for the acting user, but the *other* member only converges on the next poll. Ticks are suppressed when `document.hidden`.
- **Zero `data-testid` in the codebase** (`research.md` §F): everything is reachable by `getByRole`/`getByLabel`/`getByText`. Adding testids would regress the project's own accessibility-first convention (CLAUDE.md).
- **Running the stack** (`research.md` §G): API on `http://localhost:5252` (http profile), SPA on `http://localhost:4200` (`ng serve`) proxying `/api` → `:5252` via `web/proxy.conf.json`. No CORS configured. `ConnectionStrings__DefaultConnection` (LocalDB) and `Jwt:SigningKey` are **not committed** — supplied out-of-band. Migrations are **never** applied on startup. DB starts empty (no seeding). `GET /health` is the readiness probe. Dev email is a `NoOpEmailSender` — invites must be driven via the response-body/`#invite-link` token, not email.
- **CI is a single `deploy.yml`** (`research.md` §I): `build-test` on `windows-latest` (LocalDB is Windows-only) gates everything; `deploy` (migrate-first + `/health` smoke) is push-to-main only.
- **Commit/edit gates** (`research.md` §H): `.githooks/pre-commit` runs ESLint (staged) + `tsc -b --noEmit` over `web/`; `.claude/settings.json` PostToolUse runs `eslint --fix` on `web/`. New E2E TypeScript must pass `tsc` — a `tsconfig.e2e.json` isolating Playwright types avoids Vitest/Playwright global collisions.

## Desired End State

- `npm run e2e` (from `web/`) boots the full stack and runs a green Playwright suite locally against the real app.
- One hardened spec, `web/e2e/journey.spec.ts`, drives the two-member 8-step journey and asserts both members converge on the same board state — and **fails** if that cross-stack flow breaks (proven by a deliberate break during `/10x-e2e`).
- A new **required** `e2e` CI job blocks PRs when the journey regresses.
- `test-plan.md` §6.5 cookbook documents "how to add an E2E test" and §5's e2e gate reads as wired.

**Verification of end state:** `npm run e2e` passes locally; the `e2e` job appears in `deploy.yml`, runs on PR, and goes red when the journey is deliberately broken; `test-plan.md` §6.5 is no longer "TBD".

### Key Discoveries:

- Drive **UI login**, never token injection — access token is in-memory (`auth.service.ts:47`).
- Second member flow crosses the `returnUrl` hop: `register.component.ts:28-31,66-76` → `login.component.ts:36-41,78-82` → `/join/<token>` → Accept & join (`join.component.html:76-78`).
- Wait on state, never time: `waitForResponse(/\/api\/tasks$/)` or `toBeVisible()` — the observer converges within ~4 s (`board.component.ts:32`). Keep the observing page **focused/visible** or polling stops (`document.hidden`).
- Read the invite from `<code id="invite-link">` (`invite-dialog.component.html:47-49`) and navigate the second context to it — avoids clipboard permissions.
- "Confirm" button label may be "Confirm (self-attested)" — match by regex `/Confirm/` (`task-card.component.html:118-122`). "Awaiting confirmation" span is a good observing-member assertion.
- Login & Register share "Email"/"Password" labels — scope by route/heading.
- Windows/LocalDB is the CI center of gravity — the `e2e` job matches `build-test`'s runner.

## What We're NOT Doing

- **Not** adding `data-testid` attributes — the app is fully role/label/text reachable.
- **Not** covering invite/token abuse (reuse, expiry, wrong-household scoping, double-consume) — that is **Phase 4** (`testing-invite-token-abuse`), deliberately deferred (`test-plan.md:100-103`). No negative-seam cases in this phase.
- **Not** forcing/asserting the 15-min access-token refresh explicitly — it exercises naturally if a run crosses the boundary; the 401-refresh interceptor already has dedicated unit coverage.
- **Not** re-implementing integration coverage in the browser (no lifecycle-matrix, no isolation sweeps — those live in the integration layer) and **not** asserting styling/pixels (excluded by `test-plan.md` §7).
- **Not** serving the built `wwwroot` same-origin for E2E — using `ng serve` + proxy to match the dev inner loop (chosen approach).
- **Not** running E2E on ubuntu/mssql-container — Windows + LocalDB matches `build-test` and avoids new infra.
- **Not** enabling Playwright's visual/screenshot assertions — DOM snapshots verify function.
- **Not** authoring the journey spec inside Phase 1 or 3 — that is `/10x-e2e`'s job (Phase 2).

## Implementation Approach

Three phases, routed by skill. Phase 1 lays the harness and proves it boots with a disposable smoke spec. Phase 2 is handed to `/10x-e2e`, which creates the seed + rules levers and generates/hardens the real journey test. Phase 3 promotes the harness into CI as a blocking gate and updates the test-plan docs. The `baseURL` is `http://localhost:4200`; Playwright's `webServer` orchestrates both the API (`:5252`) and `ng serve` (`:4200`), gating on `/health`. Migrations are applied out-of-band before the API launches (a global setup step or documented pre-step), mirroring the deploy job's migrate-first rule.

## Critical Implementation Details

- **Migrations never on startup.** The E2E harness must run `dotnet ef database update` against its target DB **before** the API accepts requests — both locally and in CI (`src/Homdutio.Data/MIGRATIONS.md:14`, `research.md` §G/§I). Do this in Playwright `globalSetup` (or a documented pre-run script), not by relying on app startup.
- **Out-of-band secrets.** The API needs `ConnectionStrings__DefaultConnection` (LocalDB) and `Jwt:SigningKey` supplied via env/user-secrets before `webServer` starts it — there is no committed default; an unset connection string fails at first DB hit (`Program.cs:33-37,71-86`).
- **Keep the observing page visible.** A backgrounded Playwright page has `document.hidden === true`, which suppresses the 4 s poll — the observer would never converge. Both contexts' pages must stay visible during cross-member assertions (`research.md` §D).
- **Rate limit.** The real invite policy is 10 / 900 s per user (`appsettings.json:25-28`); keep invite mints in E2E well under that (one per run is fine).
- **tsconfig isolation.** Playwright and Vitest both define globals; a `tsconfig.e2e.json` scoping Playwright types to `web/e2e/` keeps the pre-commit `tsc -b --noEmit` over `web/` green (`research.md` §H).

---

## Phase 1: Playwright Harness Scaffolding (local)

> Routing: infrastructure — driven by `/10x-implement`. `/10x-e2e`'s gate will redirect this phase here.

### Overview

Install Playwright, add the config that orchestrates the running stack, create the `web/e2e/` directory and npm scripts, isolate Playwright types from the TS build, and prove the harness boots with a minimal smoke spec.

### Changes Required:

#### 1. Playwright dependency + browser

**File**: `web/package.json`

**Intent**: Add `@playwright/test` as a devDependency and ensure the Chromium browser is installed so specs can run locally and in CI.

**Contract**: New devDependency `@playwright/test`; the Chromium binary installed via `npx playwright install chromium`. No change to existing `test` (Vitest) script semantics.

#### 2. npm scripts for E2E

**File**: `web/package.json`

**Intent**: Give developers first-class commands to run the suite headless, in UI mode, and in debug mode — distinct from the Vitest `test` script.

**Contract**: Add `e2e` (`playwright test`), `e2e:ui` (`playwright test --ui`), `e2e:debug` (`playwright test --debug`). `test` continues to run Vitest.

#### 3. Playwright config with stack orchestration

**File**: `web/playwright.config.ts` (new)

**Intent**: Define the single Chromium project, set `baseURL` to the SPA, and orchestrate both servers via `webServer`, gating on the API's `/health` readiness. Encode the state-waiting defaults (no fixed timeouts) the E2E rules will rely on.

**Contract**: `baseURL: 'http://localhost:4200'`; a `webServer` array launching (a) the API on `:5252` and (b) `ng serve` on `:4200`, with `reuseExistingServer: !process.env.CI`; readiness gated on `http://localhost:5252/health` returning 200. `testDir: './e2e'`. Chromium project only. Trace/screenshot on failure for debugging (not as assertions). The API server command receives `ConnectionStrings__DefaultConnection` + `Jwt:SigningKey` from the environment.

#### 4. Migration global-setup step

**File**: `web/e2e/global-setup.ts` (new), referenced from `playwright.config.ts`

**Intent**: Apply EF migrations to the E2E database **before** any server accepts requests, honoring the "migrations never on startup" rule. Locally this targets LocalDB.

**Contract**: `globalSetup` runs `dotnet ef database update --project src/Homdutio.Data --startup-project src/Homdutio.Api` against the configured connection string. Fails fast (non-zero exit) if migrations don't apply. Assumes the DB is reachable and empty is acceptable (the journey builds its own data).

#### 5. TypeScript isolation for Playwright

**File**: `web/tsconfig.e2e.json` (new); reference from the pre-commit-relevant TS project graph as needed

**Intent**: Scope Playwright types to `web/e2e/` so `@playwright/test` and Vitest globals don't collide and the pre-commit `tsc -b --noEmit` over `web/` stays green.

**Contract**: A `tsconfig.e2e.json` including `e2e/**/*.ts` with Playwright types, excluded from the app/Vitest TS projects. `tsc -b --noEmit` passes across `web/`.

#### 6. E2E directory + smoke spec

**File**: `web/e2e/smoke.spec.ts` (new)

**Intent**: Prove the harness boots end-to-end — servers start, migrations applied, baseURL reachable — with a trivial role-based assertion. This is a harness sanity check, not a risk test.

**Contract**: One test that navigates to `/login` and asserts the login control renders via `getByRole('button', { name: /Log in/ })` (scoped per `research.md` §F). Uses only role-based locators and state-waiting — sets the pattern for the seed spec. Kept as an ongoing smoke check.

### Success Criteria:

#### Automated Verification:

- `@playwright/test` present and Chromium installed: `cd web && npx playwright --version`
- Harness boots and smoke passes: `cd web && npm run e2e`
- TypeScript build stays green: `cd web && npx tsc -b --noEmit`
- Lint passes: `cd web && npm run lint`

#### Manual Verification:

- `npm run e2e:ui` opens the Playwright UI with both servers running and the smoke test visible.
- The API's `/health` gate is respected — the suite waits for readiness rather than racing startup.
- Migrations are applied out-of-band (no startup migration); a fresh/empty DB still lets the smoke pass.

**Implementation Note**: After Phase 1 automated verification passes, pause for manual confirmation before Phase 2.

---

## Phase 2: Two-Member Journey E2E Test (Risk #4)

> Routing: the one browser-level risk — driven by `/10x-e2e testing-e2e-journey phase 2`. On its first run it creates the two quality levers (seed spec + E2E rules), then generates, reviews, and break-verifies the journey spec.

### Overview

Author and harden the single spec that proves Risk #4: two browser contexts drive the full 8-step journey and converge on the same board state.

### Changes Required:

#### 1. Quality levers — seed spec + E2E rules

**File**: `web/e2e/seed.spec.ts` (new) and the E2E rules file (per `/10x-e2e` `references/`)

**Intent**: Create the exemplar every generated test is modeled on and the rules the agent reads before generating — adapted to this app's real routes/roles. Seed quality is test quality.

**Contract**: `seed.spec.ts` demonstrates role-based locators, UI-login setup, unique test data (timestamp-suffixed emails), state-waiting (`waitForResponse(/\/api\/tasks$/)` / `toBeVisible()`), and cleanup. The rules file encodes the five anti-pattern guards. Both created once, then left in place.

#### 2. The journey spec

**File**: `web/e2e/journey.spec.ts` (new)

**Intent**: Drive the two-member 8-step journey across all four seams and assert both members see the same board state at the key transitions, so the test fails when any cross-stack seam breaks.

**Contract**: Two `browser.newContext()` instances (admin + member) for clean `localStorage`/refresh-token isolation. Steps, each waited on by state (never time), with unique per-run identifiers:
1. Admin: register → login → create household.
2. Admin: open Invite dialog, read the token from `<code id="invite-link">`.
3. Member (2nd context): navigate to `/join/<token>` → logged-out screen → register (returnUrl preserved) → redirected to login → login → land on `/join/<token>` → **Accept & join**.
4. Admin: create a task via "New task".
5. Member: **Claim** it; assert the admin's board reflects the claim on the next poll (observer converges — `waitForResponse(/\/api\/tasks$/)` / meta "Claimed by …").
6. Member: **Mark done**; assert admin sees "Awaiting confirmation".
7. Admin: **Confirm** (match `/Confirm/`); assert the task drops off **both** boards.

Assertions are on observable board state, not internal calls; no styling/pixel assertions.

### Success Criteria:

#### Automated Verification:

- The journey spec passes against the running app: `cd web && npx playwright test e2e/journey.spec.ts`
- TypeScript build stays green: `cd web && npx tsc -b --noEmit`
- Lint passes: `cd web && npm run lint`

#### Manual Verification:

- Reviewed against the five `/10x-e2e` anti-patterns (hallucinated assertion, brittle selector, shared state, wait-for-time, no cleanup) — none present.
- **Deliberate-break check**: temporarily break the cross-member convergence (e.g., stub out the mutation refetch or the poll) and confirm the spec goes **red**; then revert the break (never committed).
- Both contexts remain visible during cross-member assertions (poll not suppressed).

**Implementation Note**: `/10x-e2e` runs its own phase-end ritual. After Phase 2 is green and break-verified, pause for manual confirmation before Phase 3.

---

## Phase 3: Wire the E2E Gate into CI

> Routing: infrastructure — driven by `/10x-implement`. `/10x-e2e`'s gate will redirect this phase here.

### Overview

Add a blocking `e2e` job to the existing workflow on a Windows/LocalDB runner, and update the test-plan docs to reflect the wired gate.

### Changes Required:

#### 1. New `e2e` CI job

**File**: `.github/workflows/deploy.yml`

**Intent**: Run the Playwright journey suite in CI against a freshly migrated, isolated LocalDB, blocking PRs when the journey regresses.

**Contract**: A new job `e2e` with `needs: build-test`, triggering on **PR + push** (same triggers as `build-test`), runner **`windows-latest`**. Steps: setup .NET 9 + Node 22 → start MSSQLLocalDB → create/target an **isolated run-scoped DB** (e.g. `homdutio_e2e_${{ github.run_id }}`) → `dotnet tool restore` + `dotnet ef database update` on that DB → `npm ci` + `npx playwright install --with-deps chromium` → start the stack (via Playwright `webServer`, `CI=true` so `reuseExistingServer` is false) with the run-scoped connection string + `Jwt__SigningKey` → gate readiness on `/health` → `npx playwright test` → teardown (stop servers, drop the run-scoped DB). Uploads the Playwright report artifact on failure.

#### 2. Documentation: test-plan cookbook + gate status

**File**: `context/foundation/test-plan.md`

**Intent**: Fill in the "Adding an e2e test" cookbook (currently "TBD — see §3 Phase 5") and reflect that the e2e gate is now wired.

**Contract**: §6.5 documents the harness (config, `web/e2e/`, seed + rules, `npm run e2e`), the locator/wait discipline, the two-context pattern, and the migrate-first requirement. §5's "e2e on the critical journey" row reads as wired. §3 Phase 5 status advances toward `complete`. (Foundation-doc edit — path-ignored by CI, so it won't retrigger the gate.)

### Success Criteria:

#### Automated Verification:

- Workflow YAML is valid and the `e2e` job is present: `cd . && gh workflow view deploy.yml` (or a YAML lint).
- On a PR, the `e2e` job runs, boots the stack against an isolated LocalDB, and the journey suite passes green.

#### Manual Verification:

- The `e2e` job is marked a **required** status check (branch protection — a repo setting) so it blocks merges.
- A deliberately broken journey turns the `e2e` PR check red (confirming the gate has teeth).
- `test-plan.md` §6.5 is no longer "TBD" and §5's e2e gate reads as wired.

**Implementation Note**: After Phase 3, run the epilogue (status → implemented) per the driving skill's ritual.

---

## Testing Strategy

### Unit Tests:

- None added here — unit/component coverage stays in the existing Vitest layer. This plan adds only the E2E layer.

### Integration Tests:

- None added — integration coverage (isolation, lifecycle matrix, concurrency, audit) already lives in `tests/Homdutio.Api.Tests` and must **not** be duplicated in the browser.

### E2E Tests:

- `web/e2e/smoke.spec.ts` — harness sanity (login page renders).
- `web/e2e/journey.spec.ts` — the two-member 8-step journey (Risk #4), break-verified.

### Manual Testing Steps:

1. `cd web && npm run e2e` — both servers boot, migrations applied, suite passes.
2. `npm run e2e:ui` — step through the journey visually; confirm both members converge on the same board state after each mutation.
3. Deliberately break the poll/refetch, re-run, confirm red; revert.
4. Open a PR and confirm the `e2e` check runs and blocks on failure.

## Performance Considerations

- E2E is the slowest, flakiest layer — budget is **one journey spec** plus the smoke check. The 4 s poll means cross-member assertions wait up to ~4 s each; keep the journey lean and always state-wait (no fixed sleeps) so it's as fast and deterministic as the poll allows.
- Windows CI runners are slower than ubuntu; the single-spec budget keeps the `e2e` job's wall-clock reasonable.

## Migration Notes

- No schema changes. The only DB concern is applying **existing** migrations out-of-band before the API starts, and (in CI) targeting an isolated run-scoped database so parallel PR runs on the shared Windows runner pool don't collide.

## References

- Research: `context/changes/testing-e2e-journey/research.md`
- Test plan: `context/foundation/test-plan.md` (§2 Risk #4, §2 Risk Response, §3 Phase 5, §5 gates, §6.5 cookbook, §7 exclusions)
- Change identity: `context/changes/testing-e2e-journey/change.md`
- Lessons: `context/foundation/lessons.md` (last-admin TOCTOU — not on the E2E path, noted as a prior)
- Key code anchors: `web/src/app/join/join.component.ts:63-103`, `web/src/app/auth/login/login.component.ts:36-41,78-82`, `web/src/app/auth/auth.service.ts:30,47`, `web/src/app/board/board.component.ts:32,176-199`, `web/src/app/board/task-card/task-card.component.html:72-125`, `src/Homdutio.Api/Program.cs:33-40,180`, `.github/workflows/deploy.yml`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Playwright Harness Scaffolding (local)

#### Automated

- [x] 1.1 @playwright/test present and Chromium installed
- [x] 1.2 Harness boots and smoke passes (`npm run e2e`)
- [x] 1.3 TypeScript build stays green (`tsc -b --noEmit`)
- [x] 1.4 Lint passes (`npm run lint`)

#### Manual

- [x] 1.5 `npm run e2e:ui` opens with both servers running and smoke visible
- [x] 1.6 `/health` readiness gate respected (no startup race)
- [x] 1.7 Migrations applied out-of-band; fresh/empty DB still passes

### Phase 2: Two-Member Journey E2E Test (Risk #4)

#### Automated

- [ ] 2.1 Journey spec passes against the running app
- [ ] 2.2 TypeScript build stays green (`tsc -b --noEmit`)
- [ ] 2.3 Lint passes (`npm run lint`)

#### Manual

- [ ] 2.4 Reviewed against the five anti-patterns — none present
- [ ] 2.5 Deliberate-break check: journey goes red when convergence is broken, then reverted
- [ ] 2.6 Both contexts remain visible during cross-member assertions (poll not suppressed)

### Phase 3: Wire the E2E Gate into CI

#### Automated

- [ ] 3.1 Workflow YAML valid and `e2e` job present
- [ ] 3.2 On a PR, the `e2e` job boots the stack against isolated LocalDB and passes green

#### Manual

- [ ] 3.3 `e2e` job marked a required status check (blocks merges)
- [ ] 3.4 A deliberately broken journey turns the `e2e` PR check red
- [ ] 3.5 `test-plan.md` §6.5 no longer "TBD" and §5 e2e gate reads as wired
