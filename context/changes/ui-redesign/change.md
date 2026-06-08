---
change_id: ui-redesign
roadmap_id: S-11
title: UI redesign (board experience overhaul)
status: planned
created: 2026-06-08
updated: 2026-06-08
prerequisites: [household-and-board, accountability-loop, task-management-and-priority, invite-and-multiplayer-board]
prd_refs: [NFR-2]
---

# Change: ui-redesign

Reskin the whole board experience to a minimalist, pastel style inspired by Claude.ai and Scanye —
replacing the bare v1 shell. Introduces a design-token layer (CSS custom properties) plus a really
loaded Inter font, a persistent app shell (light/translucent sidebar + topbar with an avatar menu
that finally surfaces logout), and a recomposed board: the monolithic component is split into
`task-column` / `task-card`, the create-task form moves into a dialog (consistent with edit), and per-task management actions
(Edit, Delete, and reserved S-05 Unclaim/Send back) move into a kebab (⋯) menu on the card — so the
edit dialog becomes a pure form and destruction never sits beside Save. The layout is designed up front to
host the affordances of S-04 (edit), S-05 (loop recovery), S-06 (invite) and S-09 (member admin)
without a re-layout. Light-only, with tokens structured so dark mode is a later add. NFR-2 (≤400px,
no horizontal scroll) is preserved — the sidebar collapses to a bottom icon bar on phones.

See `plan.md` for the implementation contract and `plan-brief.md` for the two-page summary.
