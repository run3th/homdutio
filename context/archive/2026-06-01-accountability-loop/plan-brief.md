# Accountability Loop (S-03) — Plan Brief

> Full plan: `context/changes/accountability-loop/plan.md`

## What & Why

S-03 is the product's **north star**: a household member creates a task, claims it, marks it done, and an
admin confirms it — closing the task off the board while a durable record of who did what (and when)
persists. The confirm step is the felt accountability event the whole product hypothesis rides on, so this
slice is sequenced as early as the auth + household chain allows.

## Starting Point

S-02 delivered the household domain and a **structurally empty** board: `BoardComponent` renders three
static columns from `HouseholdService.current` with no task data and no API call. The household-scoping
pattern (derive the acting user from the JWT `sub`, scope server-side, never trust a client id) and the
minimal-API + signal-service conventions are established and reused here. There is no `Task` entity yet,
no live updates (polling is F-03/S-06), and the SPA knows the user's email but not their id.

## Desired End State

A member adds a task; it appears in **To do** with the creator's name + timestamp. Anyone claims it → **In
progress** with the claimer's name. The claimer marks it done → **Done**. An admin confirms it → the card
disappears. Behind it, a `HouseholdTask` row (closed via `ClosedAtUtc`) and an immutable `TaskEvent` chain
(Created → Claimed → MarkedDone → Confirmed, the last flagged `self-attested` if the admin confirmed their
own claim) persist for the household's lifetime. The whole loop is verifiable on **one device**.

## Key Decisions Made

| Decision                       | Choice                                          | Why (1 sentence)                                                                 | Source |
| ------------------------------ | ----------------------------------------------- | -------------------------------------------------------------------------------- | ------ |
| Audit representation           | Append-only `TaskEvent` log + state projection  | An immutable event chain is the honest NFR-3 record and extends cleanly for S-05. | Plan   |
| Closure model                  | Status enum `{ToDo,InProgress,Done}` + `ClosedAtUtc` | Closure is a transition, not a delete; the board filters on the timestamp.   | Plan   |
| Member identity on cards       | Add `DisplayName` to `ApplicationUser` now      | Cards read as names, not raw emails; captured at register, backfilled from email. | Plan   |
| API shape                      | Action sub-routes per transition                | Each transition is separately authorizable and maps 1:1 to a `TaskEvent`.        | Plan   |
| Invalid/stale transitions      | Guarded → 409 (403 for role)                    | Keeps the state machine honest and correct once S-06 makes it multiplayer.        | Plan   |
| Button visibility              | Server-computed affordance flags in the DTO     | One source of truth for the rules; the SPA stays dumb and needs no user id.        | Plan   |
| Cross-member freshness         | Acting-client refetch only (no polling)         | Roadmap sequences 5s freshness into F-03/S-06; S-03 is one-device by design.       | Plan   |
| Task fields & order            | Title + optional description/category; order by created time | Covers FR-010/FR-018; defers FR-021 drag-reorder to S-04.            | Plan   |

## Scope

**In scope:** `HouseholdTask` + `TaskEvent` entities; `DisplayName` on accounts; create / claim / mark-done /
admin-confirm endpoints with guards, scoping, and affordance flags; the interactive board (live cards,
create form, action buttons); self-attested closure; integration + vitest tests.

**Out of scope:** edit / delete / reorder (S-04); unclaim / admin send-back (S-05); invite + second member
(S-06); cross-household isolation *sweep* (S-07); live polling (F-03/S-06); any `self-attested` UI surface
beyond a confirm hint; aggregation views (v2).

## Architecture / Approach

Backend-first, three phases. The DbContext gains two tables and a column via one additive migration. Four
guarded minimal-API endpoints under `/api/tasks` each resolve the caller's household from the JWT `sub`,
assert current state + actor eligibility, mutate the `HouseholdTask` projection **and** append one
`TaskEvent` in a single `SaveChanges`, and return a DTO carrying `canClaim` / `canMarkDone` / `canConfirm`.
The Angular `TaskService` (signals, refetch-after-action) feeds `BoardComponent`, which renders cards into
the three S-02 columns and shows only the buttons the DTO permits. Closed tasks stop returning from the
board read, so they vanish with no client bookkeeping.

## Phases at a Glance

| Phase                                          | What it delivers                                                        | Key risk                                                            |
| ---------------------------------------------- | ----------------------------------------------------------------------- | ------------------------------------------------------------------- |
| 1. Backend domain + lifecycle API              | Entities, migration, four guarded endpoints, affordance flags, tests    | Getting the state machine + audit/closure shape right (4 slices inherit it) |
| 2. Task service, display-name & render/create  | `TaskService`, register display-name, live board, create-task form      | Turning the static board into live data without breaking S-02 specs |
| 3. Lifecycle actions in the UI & closure        | Affordance-driven buttons, refetch, closure removal, ≤400px cards, specs | Action UX + button affordances at 400px (NFR-2)                     |

**Prerequisites:** S-02 (household-and-board) — delivered. No second member needed (self-attested path
covers single-device verification).
**Estimated effort:** ~3 sessions, one per phase.

## Open Risks & Assumptions

- **`Task` naming collision** with `System.Threading.Tasks.Task` — entities/enums use the `HouseholdTask`
  prefix to stay unambiguous.
- **Board filter on `ClosedAtUtc`, not status** — forgetting it leaves confirmed cards on the board.
- **Projection + event must commit atomically** in one `SaveChanges` (no manual transaction — the
  `EnableRetryOnFailure` execution strategy forbids it spanning the retry boundary).
- **FR-015 cross-member confirm** is only fully exercised once S-06 adds a second member; here the
  self-attested path stands in for single-device verification.

## Success Criteria (Summary)

- A single user can drive create → claim → mark done → confirm and watch the card move To do → In progress
  → Done → gone, on one device.
- After closure, the task's row + its four events remain queryable though the card is gone (NFR-3).
- Illegal moves are rejected (double-claim 409, non-claimer done 409/403, member confirm 403) and a
  foreign-household task id returns 404.
