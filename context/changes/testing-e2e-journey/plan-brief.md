# E2E Journey + CI Gate for the MVP Cross-Stack Flow (Risk #4) — Plan Brief

> Full plan: `context/changes/testing-e2e-journey/plan.md`
> Research: `context/changes/testing-e2e-journey/research.md`

## What & Why

Stand up the project's first Playwright E2E layer and prove **Risk #4**: the full register → create-household → invite → join → claim → done → admin-confirm journey holds across the real running stack, with **both members observing the same board state** at each transition. No cheaper layer crosses the frontend↔backend↔polling seam, so this is the one risk that genuinely needs a browser. This is rollout Phase 5 of `test-plan.md`.

## Starting Point

No E2E layer exists — `@playwright/test` is absent, no config, no `web/e2e/`. All 8 journey steps are already browser-drivable (real screens + endpoints), the UI is fully role/label/text reachable (zero `data-testid`), and the stack runs as SPA `:4200` → proxy → API `:5252` with LocalDB and `/health`. CI is a single `deploy.yml` whose `build-test` job already runs on `windows-latest`.

## Desired End State

`npm run e2e` boots the stack and runs a green two-member journey spec locally; a new **required** `e2e` CI job blocks PRs when that journey regresses; the test-plan's E2E cookbook (§6.5) and gate (§5) are filled in and wired.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Serve strategy / baseURL | `ng serve` :4200 + proxy → API :5252 | Matches the dev inner loop; proxy already configured | Plan |
| Test scope | One two-member happy-path journey | Exactly what Risk #4 names; one test per risk (E2E budget) | Plan |
| CI runner | `windows-latest` + isolated LocalDB | LocalDB is Windows-only; matches `build-test`, no new infra | Plan |
| 15-min refresh seam | Exercise naturally, don't force | Keeps the test fast/deterministic; interceptor has unit coverage | Plan |
| Auth setup | Drive UI login (no token injection) | Access token is in-memory only; login also exercises the refresh seam | Research |
| Both-members mechanism | Two `browser.newContext()` | Clean localStorage/refresh-token isolation per member | Research |
| Wait discipline | `waitForResponse(/\/api\/tasks$/)` / `toBeVisible()` | Observer converges on the 4 s poll — never a fixed sleep | Research |
| Skill routing | Scaffold + CI → /10x-implement; journey test → /10x-e2e | Only the browser-level risk belongs to /10x-e2e | Plan |

## Scope

**In scope:** Playwright install + config + `web/e2e/` + npm scripts + `tsconfig.e2e.json`; a smoke spec; the two-member journey spec (via /10x-e2e); a blocking `e2e` CI job; test-plan §6.5/§5 docs.

**Out of scope:** invite/token abuse cases (Phase 4, deferred); forcing the refresh path; any styling/pixel assertions; re-implementing integration coverage in the browser; `data-testid`; ubuntu/mssql-container CI; serving `wwwroot` same-origin.

## Architecture / Approach

Playwright `baseURL` = `http://localhost:4200`; its `webServer` orchestrates the API (`:5252`) and `ng serve` (`:4200`), gating on `/health`. Migrations are applied out-of-band in `globalSetup` (never on startup). Two browser contexts drive the admin and member through the journey; the observing page stays visible so the 4 s poll isn't suppressed. In CI, an isolated run-scoped LocalDB is migrated first, then the same suite runs on `windows-latest`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Harness scaffolding (/10x-implement) | Playwright config, e2e dir, scripts, tsconfig, smoke spec | webServer orchestration of two processes + migrate-first timing |
| 2. Journey test (/10x-e2e) | Hardened two-member journey spec, break-verified | Cross-member convergence flake if not state-waited |
| 3. CI gate (/10x-implement) | Blocking `e2e` job on windows-latest + docs | Isolated LocalDB naming; required-check branch protection |

**Prerequisites:** Playwright not yet installed (Phase 1 installs it); LocalDB + a `Jwt:SigningKey` available out-of-band for the running API.
**Estimated effort:** ~2–3 sessions across 3 phases (Phase 2 is a focused /10x-e2e run).

## Open Risks & Assumptions

- Parallel PR runs on the shared Windows runner pool must not collide — mitigated by a run-scoped E2E DB name.
- A backgrounded Playwright page stops the 4 s poll (`document.hidden`) — the journey must keep both pages visible during cross-member assertions.
- Long journeys may cross the 15-min access-token boundary and exercise refresh — acceptable (a Risk #4 seam), not asserted.

## Success Criteria (Summary)

- `npm run e2e` passes locally against the real stack.
- The journey spec fails when the cross-stack flow is deliberately broken, then reverts (proven during /10x-e2e).
- The `e2e` CI job runs on PRs and blocks merges on regression.
