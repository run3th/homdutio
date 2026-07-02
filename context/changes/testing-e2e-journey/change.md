---
change_id: testing-e2e-journey
title: E2E journey + CI gate for the MVP cross-stack flow (Risk #4)
status: implemented
created: 2026-07-02
updated: 2026-07-02
archived_at: null
---

## Notes

Rollout Phase 5 of `context/foundation/test-plan.md`: "End-to-end journey + gate wiring".

Risk covered: **#4** — the full register → create household → invite → join → claim → done → admin-confirm journey breaks at a cross-stack seam (auth hop, token storage, polling refresh, board re-render) that each unit/integration test passes alone.

Test types planned: e2e (new Playwright layer) + CI gate wiring.

Risk response intent: prove the 8-step MVP journey completes against the real running stack, with BOTH members observing the same board state at each transition. Seams to cover: the invite auth hop + `returnUrl`, token storage/refresh, the polling refresh interval, and board re-render after a mutation. Cheapest layer is e2e — no cheaper layer crosses the frontend↔backend↔polling seam. Avoid: re-implementing integration coverage in a slow browser test, asserting styling, brittle DOM selectors.

This phase introduces the project's first e2e layer (no Playwright/Cypress exists today — see §4 Stack) and wires the e2e gate into CI (`.github/workflows/deploy.yml`). The `/10x-e2e` skill (Module 3 Lesson 4) will drive the browser-level phases during implementation.
