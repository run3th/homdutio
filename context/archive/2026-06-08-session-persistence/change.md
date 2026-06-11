---
change_id: session-persistence
roadmap_id: S-10
title: Session persistence (refresh-token flow)
status: archived
created: 2026-06-08
updated: 2026-06-11
prerequisites: [account-access]
prd_refs: [Access Control]
archived_at: 2026-06-11T17:18:12Z
---

# Change: session-persistence

A logged-in user stays authenticated across a full page reload instead of being bounced to
`/login`. The short-lived access token stays in memory; on app startup the SPA silently re-mints it
from a refresh token persisted in `localStorage` (sent in the request body, not a cookie) — so a
refresh, a reopened tab, or a returning session resumes without re-entering the password. The XSS
exposure of `localStorage` is accepted and mitigated by a short access-token lifetime plus
refresh-token rotation with replay detection, not eliminated. This un-defers the refresh/revocation
work F-02 explicitly postponed and adds the server-side token store that turns logout into real
server-side revocation rather than a client-only token discard.

See `plan.md` for the implementation contract and `plan-brief.md` for the two-page summary.
