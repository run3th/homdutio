# Household and Board (S-02) — Plan Brief

> Full plan: `context/changes/household-and-board/plan.md`

## What & Why

S-02 (FR-004 / FR-017 / NFR-2): a logged-in user with no household creates one — becoming its first
**admin** — and lands on an empty three-column kanban board (To do / In progress / Done), fully usable at
≤ 400px. It introduces the **first real domain schema** (`Household` + `HouseholdMember`), so the data-model
and household-scoping decisions here are inherited by every later slice (S-03–S-09).

## Starting Point

The data layer holds only Identity tables; `ApplicationUser` is empty with a comment anticipating the
household link. The API is minimal-API endpoint groups (`AuthEndpoints`) on a JWT pipeline with
`MapInboundClaims=false` (acting user = `sub` claim). A reusable `WebApplicationFactory` test harness
(`AuthApiFactory`) exists. The frontend (S-01) is the pattern to mirror: root signal-services, functional
guards, lazy standalone components, reactive forms. There is **no `Task` entity yet** — it arrives in S-03 —
so the S-02 board is structurally empty.

## Desired End State

A no-household user is routed straight to a create-household form, names the household, becomes admin, and
sees an empty three-column board headed by the name and a role badge. A user who already has a household
goes straight to the board; creating a second is blocked both in the UI (guard) and at the API (409). No
horizontal scroll at ≤ 400px. A `HouseholdMember` row (with `Role`) links user↔household, ready for S-06
invite/join and S-09 promote.

## Key Decisions Made

| Decision                       | Choice                                              | Why (1 sentence)                                                              | Source |
| ------------------------------ | --------------------------------------------------- | ----------------------------------------------------------------------------- | ------ |
| Membership representation      | Separate `HouseholdMember` entity (UserId, HouseholdId, Role) | PRD FR-007 guardrail: v2 switcher without restructuring; S-06/S-09 are row ops | Plan   |
| One-household enforcement      | Unique index on `HouseholdMember.UserId`            | Makes FR-007 a DB invariant, not an app-level check.                          | Plan   |
| Household name                 | Required field on create                            | Gives the household identity for the board header (and v2 switcher).          | Plan   |
| Endpoint scoping               | Household derived server-side from the `sub` claim  | Sets the S-07 isolation pattern at the first domain endpoint; no IDOR.        | Plan   |
| Board read endpoint            | `GET /api/households/me` only (no task/board GET)   | Tasks don't exist until S-03; ship only what S-02 needs.                      | Plan   |
| Routing                        | Separate `/create-household` + `/board` routes      | `/board` becomes the first-class surface S-03–S-09 build on.                  | Plan   |
| ≤400px column layout           | Vertical stack → side-by-side above a breakpoint    | Guarantees zero horizontal scroll (NFR-2); inherited by later board slices.   | Plan   |
| Board chrome                   | Minimal header: name + role badge                   | Identity + orientation without building member/invite UI (S-06/S-09).         | Plan   |
| Column labels                  | English (To do / In progress / Done)                | Matches live S-01 UI and the PRD; no i18n requirement in v1.                  | Plan   |
| Second-household attempt       | Server 409 + client guard redirect                  | Defense in depth on the FR-007 invariant.                                     | Plan   |
| New-user landing               | Straight to the create-household form               | No dead-ends; fastest path to a board.                                        | Plan   |
| Testing                        | Backend integration + frontend component (vitest)   | Covers the new invariants at the right layers, mirroring S-01/F-02.           | Plan   |

## Scope

**In scope:** `Household`/`HouseholdMember`/`Role` entities + additive migration; `GET /api/households/me`
and `POST /api/households` (409/400, JWT-scoped) + integration tests; `HouseholdService`; membership guard;
`/create-household` + `/board` routing (replacing placeholder home); create-household form; empty responsive
board with header; vitest specs.

**Out of scope:** `Task` entity / board mutations (S-03); invite/join & member list (S-06); promote/remove
(S-09); isolation hardening sweep (S-07); drag-reorder (S-04); i18n; multi-household / rename / delete;
auth/refresh changes.

## Architecture / Approach

Backend-first. New domain tables join the existing `ApplicationDbContext` (Identity store) via an additive
migration; a `HouseholdEndpoints` group exposes create + membership read, deriving the household from the
JWT `sub` claim and never trusting a client id. The Angular SPA gets a root `HouseholdService` (signal
state, mirroring `AuthService`), an async membership guard that loads membership once and routes
create-vs-board, a create-household reactive form, and an empty `BoardComponent` (header + three responsive
columns rendered from static markup).

## Phases at a Glance

| Phase                                   | What it delivers                                                        | Key risk                                                            |
| --------------------------------------- | ----------------------------------------------------------------------- | ------------------------------------------------------------------- |
| 1. Domain schema + backend endpoints    | Entities + additive migration + `/me` & create endpoints + xUnit tests  | Migration must stay additive; correct JWT-`sub` scoping             |
| 2. FE service, routing & create flow    | `HouseholdService`, async membership guard, routes, create form, board stub | Async guard caching + bidirectional redirect (no dead-ends)        |
| 3. Empty board UI                       | Header + three responsive columns, placeholder home removed, vitest      | NFR-2 zero horizontal scroll at ≤400px (inherited downstream)       |

**Prerequisites:** S-01 (done), F-01/F-02 (done); local API runnable; Angular toolchain present.
**Estimated effort:** ~3 sessions, one per phase.

## Open Risks & Assumptions

- Async membership guard is new (the S-01 `authGuard` is synchronous): must load once, cache, and treat
  404 as "no household" rather than an error — or it loops/double-fetches.
- `HouseholdService` state must reset on logout so a re-login as a different user can't inherit the prior
  household.
- The ≤400px layout is load-bearing for every later board slice — reworking it after task cards land is the
  cost of getting it wrong now.

## Success Criteria (Summary)

- A new user registers, is routed to create-household, names it, becomes admin, and sees an empty
  three-column board — end-to-end, with no horizontal scroll at ≤400px.
- A second household creation is blocked both in UI and at the API (409); a fresh user's `GET /me` is 404/204.
- `dotnet test` and `npm test` green across the new endpoints, service, guard, create form, and board.
