# Lifecycle Guard Completeness — Plan Brief

> Full plan: `context/changes/testing-lifecycle-guard-completeness/plan.md`
> Research: `context/changes/testing-lifecycle-guard-completeness/research.md`

## What & Why

Test-plan rollout **Phase 2**, covering **Risk #2**: a wrong-actor / wrong-state
transition corrupts the honest accountability record the product rides on. We add
backend integration tests that prove every illegal transition × role × state is
rejected with the **correct status code**, and that `self-attested` is set **iff**
an admin confirms their own claimed work.

## Starting Point

The task-mutating surface is two files, the state universe is small
(`ToDo/InProgress/Done` × `Admin/Member`, closure via `ClosedAtUtc`), and existing
coverage is already substantial — ~14 illegal-path tests plus `self-attested == true`
and the happy paths. Research mapped the full transition matrix and, critically,
that **guard ordering is non-uniform**: `confirm`/`sendback` check role first (403),
`done`/`unclaim` check state first (409). This plan targets only the genuine gaps.

## Desired End State

The suite proves the cross-member `self-attested == false` case, the guard-ordering
crossed cases (which assert the status the code *actually* returns, not the intuitive
one), foreign-household 404 parity for `done`/`confirm`, logical double-claim 409,
the pinned member-open Delete behavior, and the implicit member-removal unclaim — and
cookbook §6.1 becomes the canonical how-to for adding lifecycle-guard tests.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Coverage breadth | Core gaps 1–6 | Closes every genuine Risk #2 hole; §7 combinatorial nice-to-haves deferred. | Plan |
| Double-claim scope | Logical 409 only | No rowversion on `HouseholdTask`; the true concurrent race is Phase 3 / Risk #3. | Research |
| Member-removal unclaim | Assert here | It's a real lifecycle transition the `/api/tasks` matrix misses; cheap given existing coverage. | Plan |
| Delete role-gating | Pin as intended | Make today's member-open behavior explicit so a future lock is a conscious break. | Plan |
| Oracle discipline | Assert from research matrix | Expected status/flag derived from research, never re-read from the guard at authoring time. | Research |
| Test placement | Per-file by domain | Lifecycle → `TaskEndpointsTests.cs`; member-removal → `HouseholdMemberAdminTests.cs`. | Plan |

## Scope

**In scope:** Cross-member `self-attested=false`; guard-ordering crossed cases;
foreign-404 parity for `done`/`confirm`; logical double-claim 409; pinned Delete;
member-removal unclaim; cookbook §6.1 + §6.6 update.

**Out of scope:** True concurrent double-claim race (Phase 3); re-testing covered
paths; §7 combinatorial cases; fixing Delete's gating; deep audit-durability
(Phase 3 / Risk #5); any production code.

## Architecture / Approach

All backend integration tests over the existing `AuthApiFactory` fixture (real API,
throwaway LocalDB), reusing the established register → login → create-household →
seed-member → bearer helpers. Three phases: oracle-critical correctness first (the
cases where an "obvious" test asserts the wrong thing), then the broader completeness
sweep, then the cookbook fill-in.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Oracle-critical correctness | Cross-member `self-attested=false`; guard-ordering crossed cases | Asserting the intuitive status instead of the code's actual ordering |
| 2. Lifecycle completeness sweep | Foreign-404 parity, logical double-claim, pinned Delete, member-removal unclaim | Overclaiming race safety; asserting board view instead of persisted record |
| 3. Cookbook §6.1 update | Lifecycle transition-matrix how-to + §6.6 note | Documenting the gotcha so newcomers avoid the oracle trap |

**Prerequisites:** Research complete (done); existing test suite green; LocalDB available.
**Estimated effort:** ~1–2 sessions across 3 phases (small, well-scoped test additions).

## Open Risks & Assumptions

- **Assumption: Delete being member-open is intended.** Pinned with a flagging
  comment; if it's actually a bug, the test cements it until consciously changed.
- The shared `IClassFixture` fixture means new tests must build isolated state
  (unique emails/households) to avoid colliding with existing tests in the same file.

## Success Criteria (Summary)

- `dotnet test tests/Homdutio.Api.Tests` green, including all new tests, no regressions.
- Every crossed-axis test asserts the status the research matrix predicts.
- Cookbook §6.1 would steer a newcomer away from the guard-ordering oracle trap.
