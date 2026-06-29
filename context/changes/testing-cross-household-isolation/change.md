---
change_id: testing-cross-household-isolation
title: Cross-household isolation hardening
status: implemented
created: 2026-06-28
updated: 2026-06-29
archived_at: null
---

## Notes

Rollout Phase 1 of `context/foundation/test-plan.md`. Defends **Risk #1** — a member of household A reads, acts on, or infers the existence of household B's tasks, roster, or invites (the worst-possible bug per PRD US-02 / FR-019).

Test type: integration (xUnit + `WebApplicationFactory`, the project's existing convention).

Risk response intent (verify, don't blindly accept):
- Prove every household-scoped route returns **404 (not 403)** to a foreign caller, byte-identical to an unknown-id 404 (the existence-oracle seal).
- Prove a newly added household-scoped route **cannot ship without being included in the isolation sweep** (the existing route-coverage build guard).
- Challenge the assumption that "logged-in implies authorized," and that a 404 alone avoids a leak — body-shape parity is what seals it.

Grounding to confirm during `/10x-research`: which routes are household-scoped; how the existing cross-household isolation sweep and the route-coverage guard work (today's reference: `tests/Homdutio.Api.Tests/HouseholdIsolationTests.cs`, `RouteIsolationCoverageTests.cs`); whether any route added since S-07 escaped the sweep.
