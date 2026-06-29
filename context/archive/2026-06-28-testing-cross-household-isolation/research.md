---
date: 2026-06-28T21:27:04+02:00
researcher: Rafal Michalak
git_commit: 15aef301544b3cf35806318108fb6adaf0230424
branch: main
repository: run3th/homdutio
topic: "Cross-household isolation hardening (Test Plan Rollout Phase 1 — Risk #1)"
tags: [research, codebase, isolation, authorization, idor, route-coverage, integration-tests]
status: complete
last_updated: 2026-06-28
last_updated_by: Rafal Michalak
---

# Research: Cross-household isolation hardening

**Date**: 2026-06-28T21:27:04+02:00
**Researcher**: Rafal Michalak
**Git Commit**: 15aef301544b3cf35806318108fb6adaf0230424
**Branch**: main
**Repository**: run3th/homdutio

## Research Question

Phase 1 of `context/foundation/test-plan.md` defends **Risk #1** — a member of
household A reads, acts on, or infers the existence of household B's tasks,
roster, or invites. Ground the following before planning the test work
(verify, don't blindly accept):

1. Which routes are household-scoped?
2. How do the existing cross-household isolation sweep
   (`HouseholdIsolationTests.cs`) and the route-coverage build guard
   (`RouteIsolationCoverageTests.cs`) actually work?
3. Did any route added since S-07 escape the sweep?
4. Is the contract real — 404 (not 403), and a foreign-household 404 that is
   byte-identical to an unknown-id 404 (the existence-oracle seal)?

## Summary

**The isolation contract is genuinely implemented and the test scaffolding is
strong — but the test plan's job for Phase 1 is to close three specific gaps,
not to re-prove what already holds.**

What holds today:

- **The seal is real at the code level.** Every household-scoped handler
  derives the caller's household from the JWT `sub` (never from the request),
  AND-s the resource id with the caller's `HouseholdId` in one WHERE clause, and
  returns an **argument-less `Results.NotFound()`** when the row doesn't resolve.
  No `AddProblemDetails()` and no exception/status-code middleware is registered,
  so that 404 is a bare, empty-body response — byte-identical whether the id is
  unknown or belongs to household B. `Results.Forbid()` (403) is only ever
  reached *after* a resource has resolved inside the caller's own household, so a
  foreign caller can never see a 403. **404-not-403 and body parity both hold.**
- **No route has escaped the sweep.** All 14 foreign-attackable routes are in
  the guard's `Scoped` set and are exercised by the sweep. Member-admin routes
  were authored *before* the sweep (not after, contrary to the change.md
  suspicion). The only post-guard additions (password-reset) are genuinely
  household-agnostic.
- **The build guard is sound.** `RouteIsolationCoverageTests` reflects over the
  live `EndpointDataSource`, and via an *inverted filter* (every `/api/*` route
  except `/api/auth` must be categorized `Scoped` or `Exempt`) it fails the build
  on any un-categorized route — including under a brand-new prefix.

The three gaps Phase 1 should harden (these are the "challenge the assumption"
findings):

1. **Body-parity is asserted for only 2 of 14 scoped routes.** The
   existence-oracle parity assertion (`AssertNotFoundParityAsync`) runs only for
   a foreign **task-claim** 404 and a foreign **member-role** 404. The other 12
   scoped routes assert *status code 404 only*, not byte-empty body. The seal
   holds for them today only because every handler happens to use argument-less
   `NotFound()` — nothing in the test locks that in. A future dev adding a
   `NotFound(new { message })` to, say, the comments route would leak an oracle
   and only the 2 parity'd routes would catch a regression.
2. **The guard proves coverage, not behavior.** There is *no programmatic link*
   between the guard's `Scoped` set and the sweep's hand-written facts. A route
   can sit in `Scoped` (guard green) while no fact actually exercises it. The
   guard guarantees "categorized," not "tested."
3. **`/api/auth` is an unconditional exempt prefix.** The one structural blind
   spot: a future household-scoped route added under `/api/auth` would be
   silently exempted. Empty today, but it rests on a naming convention.

## Detailed Findings

### Area 1 — Household-scoped route inventory

There are **20 endpoints that touch household data**, which the route-coverage
guard correctly splits into two classes by *attack surface*:

**`Scoped` (14) — present a foreign-id surface; a leak is possible; MUST be swept:**

| Method | Route | Handler location |
|--------|-------|------------------|
| GET | /api/tasks | [TaskEndpoints.cs:25](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Tasks/TaskEndpoints.cs#L25) |
| POST | /api/tasks/{id}/claim | [TaskEndpoints.cs:89](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Tasks/TaskEndpoints.cs#L89) |
| POST | /api/tasks/{id}/done | [TaskEndpoints.cs:123](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Tasks/TaskEndpoints.cs#L123) |
| POST | /api/tasks/{id}/confirm | [TaskEndpoints.cs:161](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Tasks/TaskEndpoints.cs#L161) |
| POST | /api/tasks/{id}/unclaim | [TaskEndpoints.cs:203](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Tasks/TaskEndpoints.cs#L203) |
| POST | /api/tasks/{id}/sendback | [TaskEndpoints.cs:244](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Tasks/TaskEndpoints.cs#L244) |
| PUT | /api/tasks/{id} | [TaskEndpoints.cs:302](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Tasks/TaskEndpoints.cs#L302) |
| DELETE | /api/tasks/{id} | [TaskEndpoints.cs:341](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Tasks/TaskEndpoints.cs#L341) |
| PUT | /api/tasks/order | [TaskEndpoints.cs:369](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Tasks/TaskEndpoints.cs#L369) |
| POST | /api/tasks/{id}/comments | [TaskEndpoints.cs:410](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Tasks/TaskEndpoints.cs#L410) |
| GET | /api/tasks/{id}/comments | [TaskEndpoints.cs:451](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Tasks/TaskEndpoints.cs#L451) |
| GET | /api/households/members | [HouseholdEndpoints.cs:192](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Households/HouseholdEndpoints.cs#L192) |
| POST | /api/households/members/{userId}/role | [HouseholdEndpoints.cs:229](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Households/HouseholdEndpoints.cs#L229) |
| DELETE | /api/households/members/{userId} | [HouseholdEndpoints.cs:308](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Households/HouseholdEndpoints.cs#L308) |

**`Exempt` (6) — operate only in the caller's own context; no foreign-id surface:**

| Method | Route | Why exempt |
|--------|-------|------------|
| POST | /api/tasks | Creates in the caller's own household |
| GET | /api/households/me | Returns the caller's own household (204 if none) |
| POST | /api/households | Create; caller has no membership yet |
| POST | /api/households/invites | Issues for the caller's own household |
| GET | /api/households/invites/{token} | Anonymous token-scoped preview (S-06) |
| POST | /api/households/invites/{token}/accept | Token-scoped join (S-06) |

Household-agnostic routes (`/api/auth/*`, `/health`) carry no household
dimension and are outside the contract entirely.

### Area 2 — How household scope is enforced (the contract is real)

Scope is derived in one canonical helper, **`HouseholdScope`**
([HouseholdScope.cs](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/HouseholdScope.cs)),
created specifically to remove per-endpoint drift:

- `ResolveCallerAsync` ([:25-38](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/HouseholdScope.cs#L25)):
  reads `principal.FindFirstValue("sub")`, looks up the `HouseholdMembers` row,
  returns `CallerContext(HouseholdId, Role, UserId)` or null. **The household id
  is never client-supplied.**
- `LoadScopedTaskAsync` ([:41-42](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/HouseholdScope.cs#L41)):
  `t.Id == id && t.HouseholdId == householdId` — a foreign id yields no row,
  exactly as an unknown id does.

**404 vs 403 — cleanly separated.** Every "no household / not in my household"
guard returns `Results.NotFound()`. Every `Results.Forbid()` (403) sits *below*
the scoped lookup and gates only on role/actor for a resource the caller can
already see:

- 403 role/actor gates: done ([TaskEndpoints.cs:144](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Tasks/TaskEndpoints.cs#L144)),
  confirm (:177), unclaim (:225), sendback (:261), edit (:319), member
  role/remove ([HouseholdEndpoints.cs:239](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Households/HouseholdEndpoints.cs#L239), :318).
- A foreign caller never reaches a 403 — their lookup already returned null →
  404. Confirmed across every handler.

**Body parity — the seal holds in code.** All scope 404s are the identical
statement `return Results.NotFound();` (no argument). Program.cs registers **no**
`AddProblemDetails()` and **no** exception/status-code-pages middleware
([Program.cs:65-83](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Program.cs#L65)),
so the 404 is a bare, empty-body, no-content-type response. Foreign-household and
unknown-id 404s are byte-identical. JWT validation sets `MapInboundClaims = false`
([Program.cs:72](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Program.cs#L72))
so the raw `sub` survives; route groups use `.RequireAuthorization()`
([TaskEndpoints.cs:22](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Tasks/TaskEndpoints.cs#L22)).

**Collection scoping** filters on `caller.HouseholdId` directly: board
([TaskEndpoints.cs:35](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Tasks/TaskEndpoints.cs#L35)),
reorder count-check ([TaskEndpoints.cs:387-395](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Tasks/TaskEndpoints.cs#L387)
— a foreign id in the batch fails `tasks.Count != orderedIds.Length` and rejects
the whole request with 404), roster ([HouseholdEndpoints.cs:203](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Households/HouseholdEndpoints.cs#L203)),
member targets (:258, :327).

### Area 3 — The two existing tests

**`HouseholdIsolationTests.cs` (the sweep)** — `IClassFixture<AuthApiFactory>`,
LocalDB per run. Builds House A (admin + seeded member + 3 tasks in to-do /
in-progress / done states) and House B (separate admin/household), then drives
House B's bearer token against House A's ids:

- Lifecycle routes → 404 ([:191-206](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/tests/Homdutio.Api.Tests/HouseholdIsolationTests.cs#L191)):
  claim, done, confirm, unclaim, sendback.
- Management routes → 404 ([:209-231](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/tests/Homdutio.Api.Tests/HouseholdIsolationTests.cs#L209)):
  edit, delete, reorder (mixed foreign+own ids; also asserts state not corrupted).
- Comment routes → 404 ([:234-245](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/tests/Homdutio.Api.Tests/HouseholdIsolationTests.cs#L234)).
- Member-admin routes → 404 ([:250-269](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/tests/Homdutio.Api.Tests/HouseholdIsolationTests.cs#L250));
  roster read returns only B's own member (:259).
- Read isolation: B's board never shows A's three task ids ([:173-186](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/tests/Homdutio.Api.Tests/HouseholdIsolationTests.cs#L173)).
- **Parity (the seal):** `AssertNotFoundParityAsync` ([:156-168](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/tests/Homdutio.Api.Tests/HouseholdIsolationTests.cs#L156))
  asserts `foreignBody == unknownBody` AND `foreignBody == string.Empty`. It is
  invoked for **only two** routes: foreign task-id ([:274-286](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/tests/Homdutio.Api.Tests/HouseholdIsolationTests.cs#L274))
  and foreign member-id ([:288-299](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/tests/Homdutio.Api.Tests/HouseholdIsolationTests.cs#L288)).

Routes are expressed as **hardcoded inline calls per fact** — no shared
constant, no reflection.

**`RouteIsolationCoverageTests.cs` (the build guard)** — reflects over the live
`EndpointDataSource` ([:94-125](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/tests/Homdutio.Api.Tests/RouteIsolationCoverageTests.cs#L94)),
normalizes patterns, and via an **inverted filter** includes every `/api/*`
route except those under `ExemptPrefixes = { "/api/auth" }` ([:72](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/tests/Homdutio.Api.Tests/RouteIsolationCoverageTests.cs#L72)).
It then asserts set-equality against `Scoped` ([:34-50](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/tests/Homdutio.Api.Tests/RouteIsolationCoverageTests.cs#L34))
∪ `Exempt` ([:57-65](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/tests/Homdutio.Api.Tests/RouteIsolationCoverageTests.cs#L57)):
any **uncategorized** route (discovered but not listed) or **stale** route
(listed but no longer registered) fails the build ([:74-86](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/tests/Homdutio.Api.Tests/RouteIsolationCoverageTests.cs#L74))
with a message naming the offending routes. A second fact asserts `Scoped` and
`Exempt` don't overlap ([:88-92](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/tests/Homdutio.Api.Tests/RouteIsolationCoverageTests.cs#L88)).

The guard comment is explicit about its boundary ([:17](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/tests/Homdutio.Api.Tests/RouteIsolationCoverageTests.cs#L17)):
"This guard proves COVERAGE (no route was forgotten), not query CORRECTNESS."

### Area 4 — Did any route escape the sweep since S-07? No.

Git timeline (all 2026-06-12 unless noted):

| Commit | Event |
|--------|-------|
| `93ef01c` | S-09 member-administration endpoints added |
| `f0722ce` | `HouseholdIsolationTests.cs` first added (S-07 Phase 1) |
| `cde628f` | S-07 Phase 2 — shared `HouseholdScope` helper refactor |
| `3608790` | `RouteIsolationCoverageTests.cs` first added (S-07 Phase 3) |
| `01fd3f6` | Guard widened to inverted filter ("all `/api` except auth") |
| `6a7324a` (2026-06-25) | password-reset endpoints added — **only** post-guard route additions |

Member-admin **precedes** the sweep, so S-07 was authored with it already
present (correcting the change.md note that member-admin came "after"). The only
post-guard additions — `POST /api/auth/forgot-password` and
`/api/auth/reset-password` ([AuthEndpoints.cs:103](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Auth/AuthEndpoints.cs#L103), :137)
— are pre-auth, email/token-scoped, with no household dimension. Current routes
match the guard's allowlists exactly: no uncategorized, no stale.

## Code References

- `src/Homdutio.Api/HouseholdScope.cs:25-42` — canonical caller-resolution + scoped-load helper (the single source of the WHERE clause)
- `src/Homdutio.Api/Tasks/TaskEndpoints.cs:22,35,387-395` — group auth, board scoping, reorder count-check
- `src/Homdutio.Api/Households/HouseholdEndpoints.cs:192,203,229,258,308,327` — roster + member-admin scoping
- `src/Homdutio.Api/Program.cs:65-83` — JWT validation, `MapInboundClaims=false`, no `AddProblemDetails` (why the empty 404 is bare)
- `tests/Homdutio.Api.Tests/HouseholdIsolationTests.cs:156-168,173-299` — the sweep + the 2 parity assertions
- `tests/Homdutio.Api.Tests/RouteIsolationCoverageTests.cs:34-125` — `Scoped`/`Exempt` sets, inverted-filter discovery, set-equality guard
- `tests/Homdutio.Api.Tests/AuthApiFactory.cs:18-82` — `WebApplicationFactory` fixture, per-run LocalDB, deterministic JWT config

## Architecture Insights

- **Server-derived scope, never client-supplied.** No endpoint accepts a
  `householdId` route/body parameter; scope is always looked up from the JWT
  `sub`. This is the structural reason a single shared helper can seal every
  route — there is no per-handler trust boundary to get wrong.
- **404-collapse is the isolation primitive.** Foreign-scope and
  genuinely-unknown are deliberately funneled to the same null→`NotFound()` path.
  Isolation correctness reduces to "is the resource load AND-ed with the caller's
  household id, and is the 404 body empty?"
- **Two-layer test design:** behavioral sweep (does each route enforce it?) +
  reflective coverage guard (did we forget a route?). The guard's value is
  catching the *next* route; its limit is that it can't see whether a `Scoped`
  route is actually exercised, nor whether a route's 404 body stays empty.
- **`GET /api/households/me` returns 204** (not 404) when the caller has no
  household ([HouseholdEndpoints.cs:31-33](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Households/HouseholdEndpoints.cs#L31))
  — intentional "no household yet," not an isolation hole, but note it so a test
  doesn't wrongly assert 404.
- **The invite preview is an intentional oracle.** `GET /api/households/invites/{token}`
  is `.AllowAnonymous()` ([HouseholdEndpoints.cs:132](https://github.com/run3th/homdutio/blob/15aef301544b3cf35806318108fb6adaf0230424/src/Homdutio.Api/Households/HouseholdEndpoints.cs#L132))
  and by design leaks the household *name* to an invite recipient and
  distinguishes unknown (404) from consumed/expired (410). Out of scope for the
  404-parity rule; in scope for Phase 4 (invite/token abuse, Risk #6).

## Implications for the Phase 1 plan (cost × signal)

The cheapest real signal lives at the **integration layer** (xUnit +
`WebApplicationFactory`), the established convention — no e2e needed for Risk #1.
The plan should target the three gaps, not re-prove the sealed-by-construction
parts:

1. **Extend body-parity to all 14 scoped routes.** Promote
   `AssertNotFoundParityAsync` from the 2 routes it covers to every scoped 404,
   so the empty-body seal is locked in per-route and a future
   `NotFound(new {...})` regression fails a test rather than silently leaking.
   This is the highest-signal, lowest-cost addition.
2. **Bridge coverage→behavior.** Close the "in `Scoped` but never exercised" gap
   — either drive the sweep from the same list the guard reflects (so a
   categorized route that lacks a behavioral assertion is impossible), or add a
   guard assertion that each `Scoped` entry has a corresponding exercised call.
3. **Document/seal the `/api/auth` blind spot.** At minimum a §6 cookbook note
   ("never put a household-scoped route under `/api/auth`"); optionally a guard
   tweak so an `/api/auth` route touching `HouseholdId` is flagged.

Anti-patterns to avoid (from §2 Risk Response): asserting only own-data/403;
testing one endpoint and assuming the rest; copying the expected 404 body from
the production code rather than from an independent unknown-id baseline.

## Historical Context (from prior changes)

- `context/archive/2026-06-12-household-data-isolation/` (S-07) — introduced the
  isolation contract, the shared `HouseholdScope` helper (Phase 2), and both the
  sweep and the route-coverage guard (Phases 1 & 3).
- `context/archive/2026-06-12-member-administration/` (S-09) — added the
  member roster/role/remove routes; authored *before* S-07, hence already in the
  sweep.
- `context/archive/2026-06-25-password-reset/` — the only post-guard route
  additions; verified household-agnostic.
- `context/foundation/lessons.md` — "Guard min-count invariants with an atomic
  check-and-mutate" (the last-admin TOCTOU). Not Risk #1, but the member-remove
  route it concerns is in this phase's scoped set; the concurrency angle belongs
  to Phase 3 (Risk #3), not here.

## Related Research

- `context/foundation/test-plan.md` §2 (Risk #1 row + Risk Response Guidance) and
  §3 (Phase 1) — the brief this research grounds.

## Open Questions

- Should the coverage→behavior bridge (gap #2) be a reflection-driven sweep, or
  is a lightweight assertion ("every `Scoped` entry appears in a test call list")
  enough? A decision for `/10x-plan` weighing maintainability vs. over-engineering.
- Is parity for all 14 routes worth the extra fixtures for routes whose 404 is
  trivially the same code path, or is a single shared helper applied uniformly
  enough signal? (Recommendation: uniform helper — cheap, and it locks the
  invariant rather than trusting it.)
