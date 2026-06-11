# UI Redesign (Board Experience Overhaul) — Plan Brief

> Full plan: `context/changes/ui-redesign/plan.md`

## What & Why

The v1 board is functional but visually bare — an empty shell wrapping a monolithic board with
ad-hoc, hard-coded styles. This slice (S-11) reskins the whole authenticated experience to a
minimalist, pastel style inspired by Claude.ai and Scanye: a shared design-token system, a persistent
sidebar + topbar shell, and a recomposed board of clean Claude-style cards. The new layout is designed
up front to host the affordances of surrounding slices (edit, loop recovery, invite, member admin) so
they slot in later without a re-layout.

## Starting Point

`app.html` is just `<router-outlet />` (no shell); the board component is a monolith owning header,
inline create-form, inline columns/cards, drag, polling, and dialogs. Styles are hard-coded hex
repeated per component with no tokens; `'Inter'` is named but never loaded (system fallback).
`AuthService.logout()` exists but is unreachable from the UI.

## Desired End State

A logged-in member lands on `/board` inside a polished shell — light/translucent sidebar (bottom icon
bar at ≤ 400px) and a topbar with household identity, **+ Add task** and **Invite** CTAs, and an
avatar menu that finally surfaces logout. The board is a Claude-style kanban of pastel cards; creating
a task opens a dialog (consistent with editing); per-task management (Edit, Delete, and reserved S-05
Unclaim / Send back) lives in a card **⋯ menu**, so the edit dialog is a pure form. Auth/join pages
adopt the tokens + font. Everything holds at ≤ 400px (NFR-2).

## Key Decisions Made

| Decision                         | Choice                                              | Why (1 sentence)                                                              | Source |
| -------------------------------- | --------------------------------------------------- | ----------------------------------------------------------------------------- | ------ |
| Shell architecture               | Parent layout route + child routes                  | Idiomatic Angular; shell persists across navigations, auth pages stay flat.   | Plan   |
| Shell scope                      | Only authenticated household routes                 | Shell appears where nav + identity make sense; auth/join/create stay full-page.| Plan   |
| Mobile sidebar (≤400px)          | Collapses to a bottom icon bar                      | Thumb-friendly, no horizontal scroll (NFR-2).                                  | Plan   |
| Sidebar style                    | Light / translucent                                 | Matches the airy pastel direction (vs a dark anchor).                          | Plan   |
| Nav items now                    | Home + Tasks only (→ board); no Members/Settings    | No dead icons; structure leaves room for S-09.                                 | Plan   |
| Logout home                      | Avatar menu (email + Logout)                        | Fixes the orphaned logout; future home for account/settings.                  | Plan   |
| Design tokens                    | CSS custom properties (`:root`)                     | Pierce component encapsulation; dark-mode-ready.                              | Plan   |
| Font                             | Self-host Inter (variable)                          | Same-origin, offline, no third-party; renders the named font for real.        | Plan   |
| Palette                          | Concrete hex tokens locked in the plan              | Unambiguous implementation + review.                                          | Plan   |
| Create task                      | Moved into a dialog + topbar CTA                    | Consistent with edit; declutters the board (user request).                    | Plan   |
| Per-task actions + delete        | Card **⋯ menu** (Edit / Delete / S-05 slots); edit dialog is delete-free | Destruction off the edit dialog entirely; one home for management actions (user request). | Plan   |
| Other screens                    | Token/font alignment only (no redesign)             | Consistency cheaply, without ballooning scope.                                | Plan   |
| Dark mode                        | Light only, tokens dark-ready                       | Holds scope without closing the door.                                         | Plan   |
| Tests                            | Keep green + light specs for new/extracted parts    | Protect drag/polling/dialog/lifecycle through a big refactor.                 | Plan   |

## Scope

**In scope:** design-token layer + real Inter; app shell (sidebar, topbar, avatar-menu/logout);
routing restructure; relocate Invite to topbar; recompose board into `task-column` / `task-card`;
restyle kanban + cards; create-task-as-dialog + **+ Add task** CTA; per-task **⋯ menu**
(Edit / Delete / S-05 slots) + delete-free edit dialog; token/font alignment of auth/join/create-household.

**Out of scope:** Members/Settings pages and their nav icons; member-admin/loop-recovery/invite
behaviour (slots only); dark mode; layout redesign of auth pages; any API/data change; new e2e harness.

## Architecture / Approach

A `ShellComponent` parent route (carrying the board's `authGuard` + `requireHousehold`) wraps a
`<router-outlet>`; the board nests as a child. The shell renders `SidebarComponent` (icon rail ↔ bottom
bar via a CSS breakpoint) and `TopbarComponent` (identity + Invite + `AvatarMenuComponent` with
logout). Tokens live as CSS custom properties on `:root` in `styles.scss`; every component consumes
`var(--…)`. The board is reduced to a composition of `task-column`s of `task-card`s, with create + edit
both expressed as CDK dialogs.

## Phases at a Glance

| Phase                              | What it delivers                                                        | Key risk                                               |
| ---------------------------------- | ----------------------------------------------------------------------- | ------------------------------------------------------ |
| 1. Design-system foundation        | Tokens + real Inter; all screens tokenised (no structural change)       | Visual churn across every screen; catch regressions    |
| 2. App shell                       | Sidebar + topbar + routing restructure; logout + Invite relocation      | Guard/routing restructure; mobile bottom-bar (NFR-2)   |
| 3. Board recomposition & dialogs   | `task-column`/`task-card`, restyle, create dialog, delete rework        | Preserving drag/polling/lifecycle through extraction   |

**Prerequisites:** S-02/S-03/S-04/S-06 shipped (board, lifecycle, edit/reorder, invite) — all done.
**Estimated effort:** ~3 sessions, one per phase.

## Open Risks & Assumptions

- Big refactor surface: extraction + routing change must keep drag-reorder, polling, the dialog, and
  lifecycle behaviour (and their specs) green — mitigated by the foundation-first ordering and specs.
- “Home” and “Tasks” both point to the board today (documented interim) until a distinct
  home/members destination exists.
- Loop-recovery (S-05) and member-admin (S-09) are designed as *slots* only; their real behaviour is
  out of scope and unverified here.

## Success Criteria (Summary)

- The board renders inside a polished, pastel shell with a working sidebar, topbar, and avatar-menu
  logout; Inter actually loads.
- Create is a dialog; edit's delete is a quiet header trash-icon with Save as the sole primary action;
  drag/polling/lifecycle all still work.
- Every screen holds at ≤ 400px with no horizontal scroll (NFR-2), and the full vitest suite is green.
