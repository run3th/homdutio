# Cross-household isolation hardening — Plan Brief

> Full plan: `context/changes/testing-cross-household-isolation/plan.md`
> Research: `context/changes/testing-cross-household-isolation/research.md`

## What & Why

Rollout Phase 1 of the test plan, defending **Risk #1** — a member of household A
reading, acting on, or inferring the existence of household B's data (the PRD's
worst-possible bug, US-02 / FR-019). Research found the isolation contract is
already *sealed by construction*; this phase locks that seal against silent
regression by closing three test-side gaps, not by changing any product code.

## Starting Point

Two integration tests exist: a behavioral sweep
(`HouseholdIsolationTests.cs`) that drives a foreign household against every
scoped route, and a reflection-based coverage guard
(`RouteIsolationCoverageTests.cs`) that fails the build on an un-categorized
route. The seal holds, but three gaps let it regress quietly: parity (empty-body
404) is asserted for only some routes, the guard proves "categorized" not
"exercised," and `/api/auth` is an unconditional exempt prefix.

## Desired End State

The set of household-scoped routes lives in one shared inventory that drives both
the coverage guard and the parity sweep — so a route can't be categorized without
being exercised, and every foreign-id 404 is body-parity-sealed. Adding a new
scoped route to the inventory automatically sweeps and parity-checks it; the
guard fails the build if it's forgotten.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Close coverage→behavior gap (#2) | Shared route inventory as single source of truth | Both guard and sweep iterate it, so "categorized" and "exercised" become the same fact | Plan |
| Extend parity (#1) | One shared `AssertNotFoundParityAsync` over every 404-producing scoped route | Cheap, locks the empty-body invariant per route instead of trusting it | Research |
| `/api/auth` blind spot (#3) | Cookbook rule + pointed guard comment | Makes the convention explicit where the risk lives; reflection over handler bodies is fragile | Plan |
| Prove tests aren't vacuous | Manual tripwire in Success Criteria | Directly answers research's "test one, assume the rest" anti-pattern | Plan |
| Parity baseline | 7 routes already parity'd (not 2 — research counted facts) | Accurate gap = unclaim, sendback, comments POST/GET, reorder | Plan |
| Test layer | Integration (xUnit + `WebApplicationFactory`) | Cheapest real signal for Risk #1; project convention; no e2e needed | Research |

## Scope

**In scope:** A shared scoped-route inventory; guard refactored to derive `Scoped`
from it; parity extended to all foreign-id-404 routes; `/api/auth` guard comment +
cookbook rule; §6 cookbook fill-in; tripwire verification.

**Out of scope:** Any production code change; new `ProblemDetails`/middleware;
guard reflection over handler bodies; e2e/Playwright (Phase 5); invite-token
routes (Phase 4); concurrency/last-admin TOCTOU (Phase 3).

## Architecture / Approach

A `ScopedRouteInventory` static list in the test project holds one descriptor per
scoped route (method, normalized template, id shape, `Behavior`, optional body
factory). The coverage guard projects it to its `Scoped` key set; the behavioral
sweep iterates it and dispatches on an exhaustive `Behavior` switch
(`ParityNotFound` / `OwnOnlyCollection` / `MixedBatchRejected`). A new `Behavior`
without a case fails to compile; a new entry is auto-swept — Gap #2 closed by
construction.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Inventory + guard refactor | Single source of truth; guard derives `Scoped` from it; `/api/auth` comment | Inventory keys must match `NormalizePattern` output or set-equality spuriously fails |
| 2. Inventory-driven parity sweep | Parity on every 404 route; sweep drives all 14 entries; tripwire verification | Reused House A/B must not mutate state (safe — foreign calls 404 before any write) |
| 3. Cookbook §6 update | §6.1 pattern + `/api/auth` rule; §6.6 note; status → complete | Doc only — low risk |

**Prerequisites:** None — research complete; tests run against LocalDB
(`(localdb)\MSSQLLocalDB`).
**Estimated effort:** ~1 session across 3 phases (test-and-docs only).

## Open Risks & Assumptions

- Assumes the inventory descriptor can carry enough to build every foreign request
  (method, template, id shape, body) — verified against the 14 routes' shapes.
- Assumes reused House A/House B is mutation-safe across the sweep (holds because
  every foreign/unknown call 404s before any write).

## Success Criteria (Summary)

- Every foreign-id-404 scoped route is body-parity-sealed; a `NotFound(new {...})`
  regression fails a test.
- A new scoped route can't ship without being in the inventory (guard build-fails),
  and once in, it's swept automatically.
- Both tripwire checks confirm the new assertions go red on a real regression.
