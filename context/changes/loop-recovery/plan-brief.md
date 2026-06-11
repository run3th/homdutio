# Loop Recovery + Task Comments — Plan Brief

> Full plan: `context/changes/loop-recovery/plan.md`

## What & Why

Roadmap **S-05** closes the two failure modes the S-03 lifecycle creates: a task stuck *In
progress* (no one frees it) and a *Done* task an admin judges sloppy. It adds **unclaim** (FR-022)
and **admin send-back with a reason** (FR-023). Per an explicit product decision (2026-06-11), the
slice also grows a **full free-form task-comments feature** and **admin-anytime field editing** —
the send-back reason becomes just the first kind of comment.

## Starting Point

The accountability loop (create → claim → done → confirm) ships and is well-prepared for this slice:
`TaskEndpoints.cs` establishes the transition pattern (household-scoped, state-guarded, one
`TaskEvent` per change), `TaskEventType` already names `Unclaimed`/`SentBack` as future work, and
the S-11 board **already renders disabled "Unclaim"/"Send back" menu slots** plus a detail dialog
"structured for S-05's send-back comment." No comments feature exists yet.

## Desired End State

On the live board, a claimer or admin can unclaim a stuck in-progress task instantly; an admin can
send a Done task back to In progress with a required reason (claimer preserved). Any member can post
immutable comments, see a 💬 count on the card, and read the author/timestamp thread in the detail
dialog; admins edit task fields in any column while members are read-only on fields. Every
transition and comment persists durably (NFR-3).

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Unclaim actor | Claimer **or** admin | Closes the absent-claimer deadlock the roadmap's "stuck task" framing implies | Plan |
| Unclaim UX | Direct action, no confirm | Cheap + reversible (just re-claim); a dialog is ceremony | Plan |
| Send-back comment | Required, ≤280 chars | Honors FR-023 "short comment"; mirrors the blank-title-400 guard | Plan |
| Comments scope | **Full free-form**, any member, anytime | Explicit product choice — overrides PRD Non-Goal | Plan |
| Comment mutability | Immutable / post-only | Matches the append-only audit grain (NFR-3); smaller surface | Plan |
| Field editing | **Admin anytime**, member read-only | Product choice — diverges from FR-011 To-do-only | Plan |
| Shared model | Send-back reason = a `SendBack`-kind `TaskComment` | One store for reasons + comments; no `TaskEvent` schema change | Plan |
| Comment delivery | `commentCount` on board DTO; thread lazy-loaded per task | Keeps the 4s-poll payload lean | Plan |

## Scope

**In scope:** unclaim + send-back transitions; `Unclaimed`/`SentBack` events; a `TaskComment` entity
+ post/list endpoints; comment count badge + thread UI + add-comment; admin-anytime editing;
foreign-household 404 + auth on all new routes; doc reconciliation.

**Out of scope:** comment edit/delete, replies/mentions/attachments/reactions; notifications; any
change to `DELETE` (stays To-do-only); an events-history UI beyond comments.

## Architecture / Approach

One additive migration adds a `TaskComments` table (`Body ≤280`, `Kind ∈ {Member, SendBack}`,
author + timestamp, cascade FK to the task). New `TaskEventType` values need no migration
(string-converted column). `POST /unclaim` and `POST /sendback` mirror the existing `/claim`
`/done` `/confirm` shape; send-back writes its `SentBack` event **and** a `SendBack` comment in one
`SaveChanges`. The board DTO gains `canUnclaim`/`canSendBack`/`commentCount` and a redefined
`canEdit` (admin); the thread loads lazily via `GET /api/tasks/{id}/comments`. The SPA wires the two
reserved card menu slots, a send-back comment dialog (CDK, like `delete-confirm`), and a thread +
input in the detail dialog.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Comments backend | `TaskComment` + migration + post/list endpoints + board count | Payload bloat if bodies leak into the board GET (mitigated: count-only) |
| 2. Transitions backend | unclaim + send-back + admin-edit + affordance flags | `canEdit` change ripples into existing edit tests |
| 3. Frontend recovery | wire Unclaim (direct) + Send-back dialog + board orchestration | Polling refetch mid-action (mitigated: pause pattern) |
| 4. Frontend comments | 💬 badge + thread + add-comment + admin-vs-member fields | ≤400px layout for the thread (NFR-2) |
| 5. Docs reconciliation | PRD/roadmap/contract-surfaces updates | Stale Non-Goal/FR-011 wording left behind |

**Prerequisites:** S-03 done (it is); builds on `TaskEndpoints.cs`, `TaskEvent`, and the S-11 board.
**Estimated effort:** ~3–4 sessions across 5 phases (2 backend, 2 frontend, 1 docs).

## Open Risks & Assumptions

- The comments feature and admin-edit **override settled boundaries** (PRD Non-Goal; FR-011). Phase 5
  records this; a follow-up `/10x-roadmap` / PRD pass may be warranted.
- Editing now admin-only means a non-admin who could previously fix a To-do typo must ask an admin or
  delete+recreate — accepted per the decision.
- `DELETE` stays To-do-only/any-member, leaving an intentional edit-vs-delete asymmetry (flagged, not
  resolved here).

## Success Criteria (Summary)

- A claimer or admin frees a stuck task; an admin sends a Done task back with a reason that lands in
  the thread, claimer preserved.
- Any member comments (immutably) and sees the count badge + attributed thread; admins edit fields,
  members don't.
- All new xUnit + vitest tests pass; `dotnet build` and `ng build` are green; the foundation docs no
  longer misstate the comments/edit boundaries.
