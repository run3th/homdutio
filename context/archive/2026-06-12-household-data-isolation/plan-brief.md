# Household Data Isolation — Plan Brief

> Full plan: `context/changes/household-data-isolation/plan.md`

## What & Why

S-07 hardens and *proves* Homdutio's single most important guardrail: no member of one household
may ever view, mutate, or infer the existence of another household's data (US-02, FR-019 — the PRD
calls a failure here "the worst possible bug"). The boundary already exists at every endpoint; this
slice makes it **certain** and **drift-resistant**, not new.

## Starting Point

Every endpoint already derives the caller's household server-side from the JWT (never a client id)
and returns `404` for foreign/missing ids with no existence leak. Cross-household 404s are already
tested for most task and member routes. But the logic is duplicated across two files, `done`/`confirm`
have no foreign-household test, coverage is scattered across four files, and body-shape parity is
unasserted.

## Desired End State

One authoritative isolation test fixture proves the whole boundary from a foreign household; the two
duplicated scoping helpers become one shared path; and a route-coverage guard fails the build if a
future endpoint isn't categorized as scoped-and-covered or explicitly exempt. The boundary becomes
something CI guarantees, not something each author must remember.

## Key Decisions Made

| Decision                         | Choice                                      | Why (1 sentence)                                                                 | Source |
| -------------------------------- | ------------------------------------------- | -------------------------------------------------------------------------------- | ------ |
| Hardening mechanism              | Shared scoping helper + convention test     | Centralizes the rule without DbContext magic; matches the existing endpoint pattern. | Plan   |
| Rejected alternative             | EF Core global query filters                | Cross-cutting DbContext change with real pitfalls on anonymous/pre-membership paths. | Plan   |
| Verification depth               | Full both-directions sweep, every endpoint  | Makes "be certain" literally demonstrable in one auditable place.                | Plan   |
| Explicit leak channel            | Response-body-shape parity                  | A foreign-id 404 must be byte-identical to an unknown-id 404 (no existence oracle). | Plan   |
| Convention enforcement           | Route-coverage guard (EndpointDataSource)   | A new uncategorized route fails the build — real teeth, achievable on minimal APIs. | Plan   |
| Refactor scope                   | Both endpoint files, scope-load only        | Kills the duplication that invites drift; business logic untouched.              | Plan   |
| Frontend / observability         | Out of scope (backend-only)                 | SPA renders only scoped API data; PRD defers observability to v2.                | Plan   |
| Cut line                         | Sweep is must-have; refactor + guard cuttable | Guarantees the certainty outcome ships even if structural work slips.            | Plan   |

## Scope

**In scope:** consolidated isolation test sweep (incl. `done`/`confirm` gaps + body-shape parity);
one shared scoping helper replacing the two duplicates; route-coverage convention guard.

**Out of scope:** global query filters; any frontend change; leak logging/metrics; timing
side-channels; any data-model, migration, or endpoint behavior change.

## Architecture / Approach

Backend, test-heavy. Phase 1 adds `HouseholdIsolationTests` (House A / House B fixture) hitting every
`/api/tasks` + `/api/households` scoped route from a foreign caller — this also becomes the regression
net. Phase 2 extracts `ResolveMember`/`ResolveCaller`/`LoadScopedTask` + the duplicated `CallerContext`
into one shared helper, a pure substitution guarded by Phase 1 + existing tests. Phase 3 enumerates live
routes from `EndpointDataSource` and asserts each is categorized.

## Phases at a Glance

| Phase                              | What it delivers                                   | Key risk                                              |
| ---------------------------------- | -------------------------------------------------- | ---------------------------------------------------- |
| 1. Isolation sweep + gap fill      | One fixture proving the whole boundary; done/confirm + parity | Missing an endpoint in the sweep (mitigated by Phase 3) |
| 2. Shared scoping helper           | One canonical scoping path; duplication removed    | A behavior change sneaks into the "pure" refactor    |
| 3. Route-coverage convention guard | Build fails on an uncategorized new route          | Allowlist drift; guard proves coverage, not query correctness |

**Prerequisites:** S-03 done (it is). No new dependencies, no migration.
**Estimated effort:** ~1 session across 3 phases; mostly test code + a mechanical refactor.

## Open Risks & Assumptions

- The route-coverage guard enforces *categorization*, not that each query's WHERE clause is correct —
  the sweep is what proves correctness; the two are complementary.
- The exact `exempt` route set (invite-preview, create-household, invite-accept) is confirmed against
  the live enumeration during Phase 3, not assumed.
- Phase 2 assumes the two helpers are truly equivalent; any subtle difference surfaces as a Phase 1
  test failure before it ships.

## Success Criteria (Summary)

- `dotnet test` green: every scoped endpoint is proven sealed from a foreign household, including
  `done`/`confirm`, with body-shape parity.
- Only one scoping helper + one `CallerContext` exist; no endpoint behavior changed.
- Adding an uncategorized route fails the build (demonstrated once, then reverted).
