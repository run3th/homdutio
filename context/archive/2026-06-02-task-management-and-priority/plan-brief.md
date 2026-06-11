# Task Management and Priority (S-04) — Plan Brief

> Full plan: `context/changes/task-management-and-priority/plan.md`

## What & Why

On top of the S-03 lifecycle, let a household member **edit** a task's title/description/category and
**delete** it — both only while it's in **"To do"** — and **drag to reorder** tasks within a column. Per the
PRD, column order (FR-021) is the *only* priority surface in v1: the top of "To do" is the priority. The order
is shared, so every member sees the same backlog.

## Starting Point

The S-03 board renders live tasks with server-computed affordance flags and refetch-on-action, but tasks have
**no manual order** (`GET /api/tasks` sorts by `Status` then `CreatedAtUtc`) and there is **no** edit/delete/
reorder endpoint, no drag-drop library, and no modal primitive. S-03 deferred all of this to S-04.

## Desired End State

Each card has an affordance to open a **task-detail dialog**: for a "To do" task it edits the fields (Save
re-renders the board) and offers an inline-confirm **Delete**; for a claimed/done task it's read-only. Within
any column, dragging a card to a new slot persists a shared order that holds on reload. Illegal moves are
rejected server-side (edit/delete off "To do" → 409; foreign id → 404; blank title → 400).

## Key Decisions Made

| Decision                         | Choice                                   | Why (1 sentence)                                                                 | Source |
| -------------------------------- | ---------------------------------------- | -------------------------------------------------------------------------------- | ------ |
| Order representation             | Integer `SortOrder`, reindex on move     | Always a dense consistent order; trivial cost at single-household scale.          | Plan   |
| Reorderable columns              | All three                                | One uniform drag interaction; `SortOrder` only needs correctness within a column. | Plan   |
| Drag-and-drop                    | Angular CDK drag-drop                    | First-class touch support for NFR-2; same `@angular/cdk` also gives the dialog.   | Plan   |
| Edit surface                     | CDK Dialog task-detail panel             | Scales to a growing per-task surface (assignee later) without reworking the card. | Plan   |
| Delete placement + guard         | In the detail panel, inline two-step confirm | Consolidates per-task actions, keeps cards uncluttered, reuses CDK a11y.       | Plan   |
| Delete semantics                 | Hard delete, no events                   | A never-claimed "To do" task has no honest record to preserve (NFR-3 guards closed tasks). | Plan |
| Edit/delete authorization        | Server `CanEdit`/`CanDelete` flags       | Keeps the SPA authorization-dumb, consistent with S-03's affordance pattern.      | Plan   |
| Reorder concurrency              | Persist on drop + refetch (last-write-wins) | Mirrors S-03 self-heal; live cross-member sync is S-06 (polling).              | Plan   |

## Scope

**In scope:** `SortOrder` + additive migration (backfill existing tasks); maintain order on create + claim/done;
edit / delete / reorder endpoints with guards + scoping; `CanEdit`/`CanDelete` flags; CDK Dialog task-detail/edit
panel with inline-confirm delete; per-column CDK drag-reorder; tests at both layers.

**Out of scope:** edit/delete outside "To do"; cross-column drag; a priority field/algorithm; **assignment**
(panel is *designed* to take an assignee later, but ships none); delete/edit/reorder audit events; soft-delete;
cross-member live reorder; undo machinery; reorder concurrency guards.

## Architecture / Approach

A household-wide integer `SortOrder` orders tasks *within* their current status column (the board query
partitions by `Status` first, so cross-column value collisions are harmless). Create and the claim/done
transitions append to the bottom of the destination column; a `PUT /api/tasks/order` reindexes a column from a
client-supplied ordered id list (validating household + status cohesion). Edit (`PUT /api/tasks/{id}`) and
delete (`DELETE /api/tasks/{id}`) are guarded to "To do". The SPA gets `@angular/cdk`: `cdk/dialog` for the
task-detail panel, `cdk/drag-drop` for per-column (independent, non-connected) drop lists; every mutation
refetches via the existing `TaskService.load()`.

## Phases at a Glance

| Phase                                              | What it delivers                                              | Key risk                                                        |
| -------------------------------------------------- | ------------------------------------------------------------- | -------------------------------------------------------------- |
| 1. Backend — ordering + edit/delete/reorder        | `SortOrder` + migration; 3 guarded endpoints; `CanEdit`/`CanDelete` | Backfill correctness; reorder validation (no partial reindex)  |
| 2. Frontend — edit/delete via task-detail dialog   | `@angular/cdk`; CDK Dialog edit panel + inline-confirm delete | First CDK dialog in the app; read-only gating; ≤400px sizing   |
| 3. Frontend — drag-and-drop reorder                | Per-column CDK drop lists; persist-on-drop + refetch          | Touch drag at ≤400px (NFR-2); drag vs open-dialog not conflicting |

**Prerequisites:** S-03 delivered (done). Adds the `@angular/cdk` dependency in Phase 2.
**Estimated effort:** ~3 sessions across 3 phases (mirrors S-03's size).

## Open Risks & Assumptions

- Mobile touch drag at ≤400px is the fiddliest part; CDK is chosen specifically to de-risk it, but the drag
  state styling + drag-vs-tap separation still need manual verification on a phone-sized viewport.
- Last-write-wins reorder means concurrent reorders by two members can clobber each other until reload; this
  is accepted pre-S-06 (no live push yet).
- The task-detail dialog is built to accept a future assignee field; assignment itself is **not** v1 (the model
  is self-claim) and is out of scope here.

## Success Criteria (Summary)

- A member can reorder "To do" by drag and the order persists and is shared across members.
- A member can edit and delete a "To do" task via its dialog; claimed/done tasks are read-only and undeletable.
- Server rejects every illegal move (edit/delete off "To do" → 409; foreign id → 404; blank title → 400) with
  no cross-household leak and no partial reorder write.
