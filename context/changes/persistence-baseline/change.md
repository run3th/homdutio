---
change_id: persistence-baseline
title: Persistence baseline — EF Core + provisioned Azure SQL wiring (F-01)
status: implementing
created: 2026-05-30
updated: 2026-05-30
archived_at: null
---

## Notes

Roadmap F-01 (bounded foundation enabler). Wire EF Core 9 + an `ApplicationDbContext` to a
provisioned Azure SQL Basic database with a runnable migration workflow, so data persists
(NFR-3 durability precondition). Unlocks F-02 (auth), S-01, and every data-bearing slice.

Scope is **DbContext + DB plumbing only** — real domain entities (Households, Tasks, Identity)
arrive with the slices that need them. See `plan.md` / `plan-brief.md`.
