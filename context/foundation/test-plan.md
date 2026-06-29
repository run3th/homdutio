# Test Plan

> Phased test rollout for this project. Strategy is frozen at the top
> (§1–§5); cookbook patterns at the bottom (§6) fill in as phases ship.
> Read before writing any new test.
>
> Refresh: re-run `/10x-test-plan --refresh` when stale (see §8).
>
> Last updated: 2026-06-29 (Phase 1 complete)

## 1. Strategy

Tests follow three non-negotiable principles for this project:

1. **Cost × signal.** The cheapest test that gives a real signal for the
   risk wins. Do not promote to e2e because e2e "feels safer." Do not put a
   vision model on top of a deterministic visual diff that already catches
   the regression.
2. **User concerns are first-class evidence.** Risks anchored in "the team
   is worried about X, and the failure would surface somewhere in <area>"
   carry the same weight as PRD lines or hot-spot data.
3. **Risks are scenarios, not code locations.** This plan documents *what
   could fail* and *why we believe it's likely* — drawn from documents,
   interview, and codebase *signal* (churn, structure, test base). It does
   NOT claim to know which line owns the failure. That knowledge is
   produced by `/10x-research` during each rollout phase. If the plan and
   research disagree about where the failure lives, research is the
   ground truth.

Hot-spot scope used for likelihood weighting: `src/` (.NET API + Data),
`web/src/` (Angular SPA), `tests/` — excluding `obj/`, `bin/`, `wwwroot/`,
`Migrations/`, `node_modules/`.

## 2. Risk Map

The top failure scenarios this project must protect against, ordered by
risk = impact × likelihood. Risks are failure scenarios in user / business
terms, not test names. The Source column cites the *evidence that surfaced
this risk* — never a specific file as "where the failure lives" (that is
research's job, see §1 principle #3).

| # | Risk (failure scenario) | Impact | Likelihood | Source (evidence — not anchor) |
|---|--------------------------|--------|------------|--------------------------------|
| 1 | A member of household A reads, acts on, or infers the existence of household B's tasks, roster, or invites — the worst-possible bug. | High | Medium | PRD US-02 / FR-019 ("failure here is the worst possible bug"); interview Q1; hot-spot dir `src/Homdutio.Api/` (21 commits/30d) — every new household-scoped route reopens it |
| 2 | A non-admin confirms, an admin confirms own work without `self-attested`, a non-claimer marks done, or a double-claim succeeds — a wrong-actor/wrong-state transition corrupts the honest record the product rides on. | High | Medium | PRD FR-013–FR-016; interview Q3 ("feels like roulette"); hot-spot dirs `src/Homdutio.Api/Tasks/` (5), `src/Homdutio.Data/Entities/` (17) |
| 3 | Two concurrent demote/remove requests on different admins both pass the last-admin guard → the household lands at zero admins, no one can confirm tasks, and the accountability loop bricks. | High | Medium | `context/foundation/lessons.md` (TOCTOU on the last-admin guard); interview Q2 ("been burned"); hot-spot dir `src/Homdutio.Api/Households/` (4) |
| 4 | The full register → create household → invite → join → claim → done → admin-confirm journey breaks at a cross-stack seam (auth hop, token storage, polling refresh, board re-render) that each unit/integration test passes alone. | High | Medium | PRD Success Criteria / US-01 (the MVP definition); interview Q4; hot-spot dir `web/src/app/` (240 commits/30d); no e2e layer exists |
| 5 | Closing, unclaiming, or sending-back a task overwrites or drops audit history instead of appending → the durable record (creator/claimer/confirmer/timestamps/`self-attested`) is silently lost. | High | Low-Medium | PRD NFR-3; FR-016 / FR-022 / FR-023; roadmap S-05 (audit-trail extension); hot-spot dir `src/Homdutio.Data/Entities/` (17) |
| 6 | An invite token is reused after consumption, accepted after expiry, or grants access to a household it was not scoped to → a stranger joins or reaches the wrong household. | High | Low-Medium | PRD FR-005 / FR-006 / FR-007, US-02; roadmap S-06 (single-use via rowversion concurrency token); abuse lens (access + resource) |

**Impact × Likelihood rubric.** Score both axes on a coarse High / Medium /
Low scale so two readers agree on the same row. Do not invent finer
gradations — the goal is ordering, not false precision.

| Rating | Impact | Likelihood |
|--------|--------|------------|
| High   | user loses access, data, or money; failure is publicly visible | area changes weekly, or we have already been burned here |
| Medium | feature degrades, a workaround exists, only some users affected | touched occasionally, has been a source of bugs |
| Low    | cosmetic, easily reverted, no data effect | stable code, rarely touched |

Order: protect Risks #1–#4 first (High impact, Medium likelihood). Risks
#5–#6 are High impact but Lower likelihood (the relevant slices shipped with
targeted designs — closure-as-transition in S-03, atomic rowversion consume
in S-06) — they are verification-and-hardening, not greenfield gaps.

**Abuse / security lens.** The product has auth and accepts user input, so
the map carries abuse scenarios that the happy-path interview would not
surface: Risk #1 (authorization / IDOR — ownership check, not just
authentication), Risk #3 (state-integrity race / resource abuse), Risk #6
(token reuse / access scoping). Input-validation parity and secret/PII
leakage were considered and held below the top set (the API returns
`ValidationProblem`; the JWT signing key and ACS email both authenticate via
App Service settings / managed identity, with no keys in the repo) — see §7.

### Risk Response Guidance

| Risk | What would prove protection | Must challenge | Context `/10x-research` must ground | Likely cheapest layer | Anti-pattern to avoid |
|------|-----------------------------|----------------|--------------------------------------|-----------------------|-----------------------|
| #1 | Every household-scoped route returns 404 (not 403) to a foreign caller, byte-identical to an unknown-id 404; a newly added route cannot ship without being swept. | "Logged-in ⇒ authorized." A 404 only avoids a leak if the body shape matches an unknown-id 404. | Which routes are household-scoped; the existing cross-household isolation sweep and the route-coverage guard that fails the build on an un-swept route. | integration | Asserting only own-data / 403; missing the existence-oracle body parity; testing one endpoint and assuming the rest. |
| #2 | Every illegal transition × role × state is rejected with the correct status; `self-attested` is set if and only if an admin confirms their own work. | "Happy-path claim works ⇒ the illegal transitions are blocked." | The full transition matrix (who may move a task from which state to which); where the guard and the `self-attested` flag are decided. | integration | Testing only allowed transitions; lifting the expected value from the guard code (oracle problem). |
| #3 | Two parallel admin mutations cannot drive the admin count below one. | "A serial test passed ⇒ the guard is concurrency-safe." | Whether the guard is a check-then-write or a single serializable transaction / re-check inside the write. | integration + concurrency (parallel requests) | A serial test that can never observe the race; asserting the guard message instead of the post-state invariant. |
| #4 | The 8-step journey completes against the real running stack, with both members observing the same board state at each transition. | "All unit/integration tests green ⇒ the journey works." | The seams: the invite auth hop + `returnUrl`, token storage/refresh, the polling refresh interval, board re-render after a mutation. | e2e (Playwright) — no cheaper layer crosses the frontend↔backend↔polling seam | Re-implementing integration coverage in a slow browser test; asserting styling; brittle DOM selectors. |
| #5 | After confirm / unclaim / send-back, the durable record remains queryable with its full history. | "Gone from the board ⇒ the task closed correctly." | Whether closure is a state transition or a delete; whether the audit/event model appends or overwrites on recovery transitions. | integration (query the persisted record after the action) | Asserting only board/visible state, never the durable record; oracle copied from the production write. |
| #6 | Reuse, expiry, wrong-household scoping, and concurrent double-consume of an invite are all rejected; the happy join still succeeds. | "A successful join ⇒ token handling is safe." | Single-use consume atomicity (the rowversion token); the expiry window and household-scoping rules; one-household-per-user enforcement. | integration | Testing only the successful join path; ignoring the concurrent double-consume race (shares a root with Risk #3). |

## 3. Phased Rollout

Each row is a discrete rollout phase that will open its own change folder
via `/10x-new`. Status moves left-to-right through the values below; the
orchestrator updates Status as artifacts appear on disk.

| # | Phase name | Goal (one line) | Risks covered | Test types | Status | Change folder |
|---|------------|-----------------|---------------|------------|--------|---------------|
| 1 | Cross-household isolation hardening | Prove no foreign-household read/act/infer, and that a new route cannot ship un-swept | #1 | integration | complete | context/changes/testing-cross-household-isolation/ |
| 2 | Lifecycle guard completeness | Prove every illegal transition × role × state is blocked and `self-attested` is correct | #2 | integration | not started | — |
| 3 | Concurrency & audit durability | Prove no zero-admin race and that audit history always appends, never disappears | #3, #5 | integration + concurrency | not started | — |
| 4 | Invite/token abuse | Prove reuse / expiry / scoping / double-consume are all rejected | #6 | integration | not started | — |
| 5 | End-to-end journey + gate wiring | Prove the MVP 8-step flow holds across the stack; wire the e2e gate into CI | #4 | e2e (new Playwright layer) + gates | not started | — |

**Status vocabulary** (fixed — parser literals):

| Value | Meaning |
|-------|---------|
| `not started` | No change folder for this rollout phase yet. |
| `change opened` | `context/changes/<id>/` exists with `change.md`; research not done. |
| `researched` | `research.md` exists in the change folder. |
| `planned` | `plan.md` exists with a `## Progress` section. |
| `implementing` | Progress section has at least one `[x]` and at least one `[ ]`. |
| `complete` | Progress section is fully `[x]`. |

Phases 1–4 stay on the existing classic integration layer (the cheapest
real signal for these risks, and the project's established convention).
Phase 5 introduces the one new layer the risk map justifies — an e2e suite
for the cross-stack journey no cheaper layer can cover — and locks the CI
floor. No AI-native phase is included (see §7).

## 4. Stack

The classic test base for this project. AI-native tools (if any) carry a
`checked:` date so future readers can see which lines need re-verification.

| Layer | Tool | Version | Notes |
|-------|------|---------|-------|
| backend unit + integration | xUnit + `WebApplicationFactory` | net9.0 | ~10 files in `tests/Homdutio.Api.Tests/`; all integration vs LocalDB (`(localdb)\MSSQLLocalDB`); `IClassFixture<AuthApiFactory>`, per-file (no shared base class). No isolated domain/unit layer yet. |
| backend persistence smoke | xUnit | net9.0 | `tests/Homdutio.Data.Tests/PersistenceSmokeTests.cs` — DbContext connectivity only. |
| frontend unit + component | Vitest (`@angular/build:unit-test`, jsdom) | Angular 21 | 28 `*.spec.ts` colocated across `web/src/app/{auth,board,household,join,shell}`. |
| API mocking (frontend) | Angular `HttpTestingController` | Angular 21 | Used in existing service specs; no MSW. |
| e2e | none yet — see §3 Phase 5 | — | No Playwright/Cypress anywhere; the cross-stack journey (Risk #4) is uncovered. |
| accessibility | none | — | Not in scope for v1 (single-household MVP; NFR-2 covers ≤400px responsiveness manually). |
| (optional) AI-native | none — deliberately omitted | n/a | `has_ai: false`; the only plausible use (visual review) is rejected by interview Q5. See §7. |

**Stack grounding tools (current session):**
- Docs: Context7 / framework docs MCP — none available in current session; .NET 9 xUnit + Angular 21 Vitest are conventional and confirmed from local configs; checked: 2026-06-28.
- Search: WebSearch — available, not used (stack is well-known; no version-sensitive recommendation needed at strategy time); checked: 2026-06-28.
- Runtime/browser: Playwright MCP — not available in current session; Phase 5 will install Playwright as a local/CI dependency, not via MCP; checked: 2026-06-28.
- Provider/platform: GitHub (`gh` CLI) + Atlassian Rovo MCP — available; CI already runs on GitHub Actions (`.github/workflows/deploy.yml`), the natural home for the Phase 5 e2e gate; checked: 2026-06-28.

## 5. Quality Gates

The full set of gates that must pass before a change reaches production.
"Required after §3 Phase <N>" means the gate is enforced once that rollout
phase lands; before that, the gate is `planned`.

| Gate | Where | Required? | Catches |
|------|-------|-----------|---------|
| build / typecheck (`dotnet build`, `ng build`/`tsc`) | local + CI | required (wired) | syntactic / type drift across both stacks |
| unit + integration (`dotnet test` + `npm test` Vitest) | local + CI | required (already wired in `deploy.yml` build-test gate) | logic regressions |
| e2e on the critical journey | CI on PR | required after §3 Phase 5 | broken MVP cross-stack user path (Risk #4) |
| migrate-first + `/health` pre-prod smoke | CI between merge + prod | required (wired) | environment-specific failures, App Service ↔ Azure SQL connectivity |
| post-edit hook (run affected tests at edit time) | local (agent loop) | recommended (Module 3 Lesson 3) | regressions at edit time |
| lint (explicit step, e.g. ESLint / analyzers) | local + CI | optional — confirm coverage during §3 Phase 5 | style / lint drift not caught by the compiler |

## 6. Cookbook Patterns

How to add new tests in this project. Each sub-section is filled in once
the relevant rollout phase ships; before that, the sub-section reads
"TBD — see §3 Phase <N>."

### 6.1 Adding a backend integration test (endpoint behavior)

- **Location**: `tests/Homdutio.Api.Tests/`, `IClassFixture<AuthApiFactory>`, integration vs LocalDB (`(localdb)\MSSQLLocalDB`). Per-file convention — no shared base class; each test builds its own state via the register → login → create-household → bearer pattern.
- **Cross-household isolation (Risk #1) is inventory-driven.** The household-scoped route surface lives in one place: `tests/Homdutio.Api.Tests/ScopedRouteInventory.cs`. To add a scoped route, add **one** `ScopedRoute` descriptor (HTTP method, normalized template, id shape, `Behavior`, optional body factory). That single edit makes the coverage guard (`RouteIsolationCoverageTests`) accept it **and** the parity/behavior sweep (`HouseholdIsolationTests`) drive it automatically — there is no second manual step. Omit it and the build fails: the guard reports the route as uncategorized.
- **Never place a household-scoped route under `/api/auth`.** That prefix is *unconditionally* exempt from the guard **and** the sweep (`RouteIsolationCoverageTests.ExemptPrefixes`); a scoped route there would be silently un-isolated and a foreign household could reach it undetected. Put scoped routes under `/api/tasks` or `/api/households` (a brand-new prefix surfaces as uncategorized — the safe failure).
- **Behavior shapes** (the sweep's exhaustive dispatch): `ParityNotFound` — a foreign-id 404 must be byte-identical to an unknown-id 404 (empty body, the existence-oracle seal); `OwnOnlyCollection` — a foreign read returns only the caller's own rows; `MixedBatchRejected` — a foreign id mixed into the caller's batch rejects the whole request without corrupting the caller's order.
- **Reference test**: `tests/Homdutio.Api.Tests/HouseholdIsolationTests.cs` (the sweep) + `ScopedRouteInventory.cs` (the source of truth).
- **Run**: `dotnet test tests/Homdutio.Api.Tests`.
- For the lifecycle transition-matrix pattern (Risk #2), see §3 rollout Phase 2 — not yet shipped.

### 6.2 Adding a concurrency / invariant test

- TBD — see §3 Phase 3 for the parallel-request pattern that exercises the last-admin (and double-consume) races without a serial false-pass.

### 6.3 Adding an audit-durability test

- TBD — see §3 Phase 3 for asserting the durable record survives confirm / unclaim / send-back (query the persisted record after the action, not the board view).

### 6.4 Adding a frontend unit/component test

- **Location**: colocated as `<name>.spec.ts` next to the component/service under `web/src/app/...`.
- **Mocking policy**: mock at the HTTP edge with Angular `HttpTestingController`; do not mock internal services.
- **Reference test**: `web/src/app/board/task.service.spec.ts`.
- **Custom form control (ControlValueAccessor)**: test the component instance's public methods/signals directly — no host harness needed. Set signal inputs via `fixture.componentRef.setInput('<name>', …)`, drive behavior through the public methods, and assert the CVA contract by registering a fake `onChange` (`component.registerOnChange(fn)`) and checking `writeValue`. **Reference**: `web/src/app/board/tag-input/tag-input.component.spec.ts` (chip add/remove/de-dup/caps/filter) + the pure helper `web/src/app/board/tag-color.spec.ts`.
- **Run locally**: `cd web && npm test`.

### 6.5 Adding an e2e test (cross-stack journey)

- TBD — see §3 Phase 5 (introduces the Playwright layer for the MVP journey, Risk #4).

### 6.6 Per-rollout-phase notes

(Optional. After each phase lands, `/10x-implement` appends a 2-3 line note here capturing anything surprising the phase taught.)

- **Phase 1 — Cross-household isolation hardening (2026-06-29).** The route-coverage guard previously proved *coverage* (no route forgotten) but not *behavior* (a route could sit in the scoped set with no assertion exercising it). The shared `ScopedRouteInventory` now makes the two the same fact — categorized ⇔ exercised. Also: parity was already asserted on **7** routes, not 2 — the research "2 of 14" counted parity *facts*, not routes; the genuine gaps were unclaim / sendback / comments POST+GET (now empty-body sealed).
- **Phase 6 — Task tags + per-household suggestions (2026-06-30).** Adding the household-scoped `GET /api/tasks/tags` route was a one-line `ScopedRouteInventory` edit (`OwnOnlyCollection`) — but `HouseholdIsolationTests.AssertOwnOnlyCollectionAsync` dispatches on the route *template*, so a new own-only collection still needs its own `case "<template>":` assertion arm + any fixture data it reads (here a seeded House A tag that must not leak vs a House B tag that must appear). The shared sweep fixture is otherwise pristine because every other entry 404s before writing; a seed for the new assertion is the rare legitimate mutation. Pure tag rules live in `TagNormalizationTests` (no host); the frontend chip control's contract lives in `tag-input.component.spec.ts`. **Migration data logic (the `Category`→`TaskTags` backfill) is intentionally left to the manual "seeded DB" check** — replaying a mid-state migration isn't worth a harness, and §7 flags generated-migration internals as out of scope.

## 7. What We Deliberately Don't Test

Exclusions agreed during the rollout (Phase 2 interview, Q5). Future
contributors should respect these unless the underlying assumption changes.

- **UI visual / snapshot tests on the board styling (S-11 redesign)** — they break on every reskin and catch nothing of value for a single-household MVP. Re-evaluate only if a visual regression actually causes a user-facing defect. (Source: Phase 2 interview Q5.)
- **EF Core internals and generated migrations** — the framework and its generator are the test; assert our behavior, not theirs. Re-evaluate if a custom migration carries hand-written data logic.
- **v2-only surfaces (child roles, multi-household switching, aggregation/reporting)** — not built; zero lines of test budget. Re-evaluate when a v2 slice opens.
- **No dedicated AI-native testing layer** — `has_ai: false` and the only plausible use (visual review) is rejected above; an AI judge over deterministic integration assertions adds cost, not signal. Re-evaluate if an AI feature ships or a DOM-unreachable surface appears.
- **Refresh-token / password-reset deep abuse beyond the shipped tests** — S-10 shipped rotate-on-use + replay detection + short access TTL; S-08 uses managed-identity email. The `localStorage` XSS trade is documented and accepted. Re-evaluate if the token transport or storage model changes.

## 8. Freshness Ledger

- Strategy (§1–§5) last reviewed: 2026-06-28
- Stack versions last verified: 2026-06-28
- AI-native tool references last verified: 2026-06-28

Refresh (`/10x-test-plan --refresh`) when:

- a new top-3 risk surfaces from the roadmap or archive,
- a recommended tool's `checked:` date is older than three months,
- the project's tech stack changes (new framework, new test runner),
- §7 negative-space no longer matches what the team believes.
