---
change_id: testing-lifecycle-guard-completeness
title: "Lifecycle guard completeness: block every illegal transition × role × state"
status: implemented
created: 2026-06-30
updated: 2026-06-30
archived_at: null
---

## Notes

Test-plan rollout **Phase 2** (see `context/foundation/test-plan.md` §3). Covers **Risk #2**: a non-admin confirms, an admin confirms own work without `self-attested`, a non-claimer marks done, or a double-claim succeeds — any wrong-actor/wrong-state transition that corrupts the honest record.

Goal: prove every illegal transition × role × state is rejected with the correct status, and that `self-attested` is set if and only if an admin confirms their own work.

Test type: backend integration (classic layer, per §3). Cheapest real signal — no e2e promotion. Reference convention: `tests/Homdutio.Api.Tests/` with `IClassFixture<AuthApiFactory>` (§6.1).

What research must ground (per §2 Risk Response): the full transition matrix (who may move a task from which state to which) and where the guard + the `self-attested` flag are decided. Anti-pattern to avoid: testing only allowed transitions, or lifting the expected value from the guard code (oracle problem).
