# Invite/token abuse — integration abuse tests (Phase 4) — Plan Brief

> Full plan: `context/changes/testing-invite-token-abuse/plan.md`

## What & Why

Phase 4 of the test rollout closes the remaining **Risk #6 (invite/token abuse)**
gaps with integration tests. The single-use invite design already shipped (roadmap
S-06: a `rowversion` optimistic-concurrency token), so this is verification — proving
reuse / expiry / scoping / **concurrent double-consume** are all rejected while the
happy join still succeeds. The risk map's named anti-pattern is "ignoring the
concurrent double-consume race"; that is exactly the gap this phase closes.

## Starting Point

`HouseholdInviteEndpointsTests.cs` already covers the *serial* abuse angles broadly:
serial double-consume, expiry, cross-household scoping, one-household-per-user,
unknown-token 404, auth, the emailed-link path, and rate limiting. What's missing is
the **concurrency** race (no concurrent-accept test exists) plus a consumed-token
check on the anonymous *preview* surface. Because `HouseholdInvite` has a rowversion
(unlike `HouseholdTask`, whose double-claim race was re-parked), the race is now
observable at the integration layer.

## Desired End State

Three new tests in `HouseholdInviteEndpointsTests.cs` prove — deterministically, every
run — that concurrent double-consume of one token yields exactly one membership, a
free user racing two valid tokens lands in exactly one household, and a consumed token
is sealed (410) on the anonymous preview. Full API suite stays green.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| -------- | ------ | ---------------- | ------ |
| Test layer | Classic integration (xUnit + `WebApplicationFactory`) | Cheapest real signal for Risk #6; project convention | Test plan |
| Scope | Concurrent race + two serial gaps | Closes the named anti-pattern + the cheap holes, skips low-signal edges | Plan |
| File placement | Extend `HouseholdInviteEndpointsTests.cs` | Owns all invite helpers; §6.1 per-file convention | Plan |
| Contingency | Tests-only; a red test escalates to a separate fix change | Matches §2 "verification-and-hardening" and how Phase 3 played out | Plan |
| Folder name | Renamed `invite-token-abuse` → `testing-invite-token-abuse` | Matches the `testing-` convention + test-plan §3 so the orchestrator tracks it | Plan |
| Primary oracle | Persisted post-state invariant from a fresh scope | Status codes can pass over a corrupt post-state (§6.2) | Test plan |

## Scope

**In scope:**
- Concurrent double-consume of one token → exactly one membership
- Same-user concurrent double-join across two tokens → user in exactly one household
- Consumed token → 410 on anonymous preview
- Private `SendConcurrentlyAsync` + `LoadInviteRowAsync` helpers (copied per convention)

**Out of scope:**
- Any production code change (design already shipped; a red test is escalated, not fixed here)
- Revoke/list-invite tests (endpoints don't exist)
- Deleted-household/inviter cascade + malformed/oversized token-format tests (low signal, §7)
- Duplicating the broad existing serial coverage; new test file; e2e

## Architecture / Approach

Extend one test file. Copy the ~6-line `SendConcurrentlyAsync` from
`HouseholdMemberAdminTests.cs` and add a small fresh-scope `LoadInviteRowAsync`. Each
concurrency test fires a single pair of parallel accepts (no loop) and asserts the
persisted invariant as the primary oracle, status codes secondary — the §6.2
discipline. Determinism comes from the accept path's rowversion optimistic concurrency
(+ `UserId` unique index for the double-join), not from a lock or a barrier.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| ----- | ---------------- | -------- |
| 1. Invite abuse — concurrency + serial gap closure | 3 tests + 2 helpers proving the invite single-use/scoping seals hold under a race | A flaky concurrency test (mitigated: rowversion makes the outcome deterministic; repeated-run check) |

**Prerequisites:** LocalDB available (existing test infra); no new dependencies.
**Estimated effort:** ~1 session, one file.

## Open Risks & Assumptions

- **Assumption:** preview returns 410 for a consumed token. If it returns 200 with
  household/inviter fields, the test correctly fails and surfaces an information-leak
  defect to escalate — the assertion is not softened to match.
- **Assumption:** the accept-path guards are correct (verification phase). A red
  concurrent test means a real defect → separate fix change, not in this plan.
- The two concurrency tests must pass repeatedly with no flake; a flake is the signal
  the guard regressed, not a reason to add retries.

## Success Criteria (Summary)

- Concurrent double-consume yields exactly one membership; free user racing two tokens
  lands in exactly one household; consumed token is 410 on anonymous preview.
- `dotnet test tests/Homdutio.Api.Tests` green, including ≥5× repeat of the concurrency tests with no flake.
- No existing invite test modified or duplicated; no production code touched.
