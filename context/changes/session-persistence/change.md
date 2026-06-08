---
change_id: session-persistence
roadmap_id: S-10
title: Session persistence (refresh-token flow)
status: implemented
created: 2026-06-08
updated: 2026-06-08
prerequisites: [account-access]
prd_refs: [Access Control]
---

# Change: session-persistence

A logged-in user stays authenticated across a full page reload instead of being bounced to
`/login`. The access token stays in memory (no XSS-exposed storage); on app startup the SPA
silently re-mints a short-lived access token from a persisted, httpOnly refresh credential — so a
refresh, a reopened tab, or a returning session resumes without re-entering the password. This
un-defers the refresh/revocation work F-02 explicitly postponed and adds the server-side token
store that turns logout into real server-side revocation rather than a client-only token discard.

See `plan.md` for the implementation contract and `plan-brief.md` for the two-page summary.
