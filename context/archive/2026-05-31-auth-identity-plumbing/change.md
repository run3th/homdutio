---
change_id: auth-identity-plumbing
roadmap_id: F-02
title: Auth + identity plumbing (ASP.NET Identity + JWT bearer)
status: archived
created: 2026-05-31
updated: 2026-06-11
prerequisites: [persistence-baseline]
prd_refs: [Access Control, FR-001, FR-002, FR-003]
archived_at: 2026-06-11T17:18:12Z
---

# Change: auth-identity-plumbing

Mount ASP.NET Core Identity on the existing EF store and stand up a stateless JWT bearer
pipeline that issues and validates signed tokens — the foundation S-01 (account-access) and
the household-scoped authorization S-07 hardens will build on. Plumbing only: token endpoints,
JWT validation middleware, and Identity tables. No user-facing pages (S-01), no SPA wiring, no
prod deploy in this slice.

See `plan.md` for the implementation contract and `plan-brief.md` for the two-page summary.
