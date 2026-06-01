---
change_id: account-access
roadmap_id: S-01
title: Account access (register, log in, log out)
status: implemented
created: 2026-06-01
updated: 2026-06-01
prerequisites: [persistence-baseline, auth-identity-plumbing]
prd_refs: [FR-001, FR-002, FR-003]
---

# Change: account-access

Deliver the first user-facing slice: a person can register an account with email + password,
log in, and log out. The F-02 auth backend (register/login/`me` endpoints, stateless JWT bearer)
is already implemented; this slice is almost entirely the Angular SPA layer that consumes it —
HTTP client, in-memory token state, bearer + 401 interceptors, an auth guard, register/login
forms, a client-side logout, and a placeholder authenticated home (S-02 replaces it). Establishes
the auth-state, token-storage, interceptor, guard, routing, form, and error-handling conventions
the rest of the SPA inherits, mobile-first at ≤ 400px (NFR-2).

See `plan.md` for the implementation contract and `plan-brief.md` for the two-page summary.
