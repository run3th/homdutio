---
change_id: invite-and-multiplayer-board
title: Invite and multiplayer board
status: implementing
created: 2026-06-02
updated: 2026-06-02
archived_at: null
---

## Notes

<!-- Free-form notes for this change: links, ad-hoc context, decisions that don't belong in research/frame/plan. -->

S-06 — single-use invite link, second-adult join (account created in-flow if needed, one household per user),
and a live shared board (polling, F-03) so both members see updates within 5s. Roadmap:
`context/foundation/roadmap.md` (S-06, lines 192-202). PRD refs: US-02, FR-005 (single-use invite),
FR-006 (join + in-flow account), FR-007 (one household per user), NFR-1 (5s freshness).
Prerequisite S-02 (household-and-board) is delivered; F-03 (polling) was decided but never built — folded
into this slice's Phase 3.

Key planning decisions (2026-06-02): DB-backed invite token, single-use + time expiry (no explicit revoke
UI, no pending-invites roster); admin OR adult member can invite; many concurrent invites allowed; dedicated
public `/join/:token` page that branches on auth/household state and threads the token through the existing
register/login screens; already-in-a-household recipient blocked (409, FR-007); joiner lands straight on the
live board; polling pauses on hidden tab and is suppressed during an active drag / open dialog; full invite
lifecycle + cross-household isolation locked by integration tests.
