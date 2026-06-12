---
change_id: household-data-isolation
title: Household data isolation — harden + verify no cross-household leakage (S-07)
status: implementing
created: 2026-06-12
updated: 2026-06-12
---

## Notes

Roadmap S-07. The PRD's worst-possible-bug guardrail: no member of one household may ever
see, mutate, or infer the existence of another household's data (US-02, FR-019). The boundary
already exists per-endpoint (server-derived household scope, foreign-id → 404); this slice makes
it **certain** (an exhaustive both-directions isolation sweep) and **drift-resistant** (one shared
scoping path + a route-coverage convention guard).

Backend-only. No new data model, no user-facing feature. See `plan.md` / `plan-brief.md`.
