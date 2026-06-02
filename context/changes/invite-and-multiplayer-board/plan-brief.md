# Invite and Multiplayer Board (S-06) — Plan Brief

> Full plan: `context/changes/invite-and-multiplayer-board/plan.md`

## What & Why

S-06 makes the board genuinely multiplayer. A member generates a **single-use, time-expiring invite link**; a
second adult opens it, registers/logs in if needed, and **joins the one household** (FR-006/FR-007); from then
on **both members see the shared board refresh within 5 seconds** (NFR-1). This is the slice that makes the
PRD's primary success flow — and FR-015's cross-member confirm — verifiable with two real people instead of a
single self-attesting admin.

## Starting Point

The membership model is already invite-ready: `HouseholdMember` has a unique index on `UserId` enforcing
one-household-per-user (FR-007), and `HouseholdEndpoints` already derives household server-side with an
`alreadyMember` 409 guard to reuse. There is **no invite entity** yet, and **F-03 (polling) was decided but
never built** — `TaskService` only refetches after the acting client's own mutations, so a second member sees
nothing until they navigate.

## Desired End State

A member clicks **Invite a member**, gets a `/join/<token>` link copied to share out-of-band. A second adult
opens it, sees the household name, registers/logs in (token preserved), and lands on the **live shared board**
as a `Member`. The inviter sees the joiner's actions appear within 5s without refreshing. A consumed/expired
link is rejected; a recipient already in a household is blocked; a household-A token can never join household B.

## Key Decisions Made

| Decision                         | Choice                                              | Why (1 sentence)                                                              | Source |
| -------------------------------- | --------------------------------------------------- | ---------------------------------------------------------------------------- | ------ |
| Invite lifetime                  | Single-use + time expiry                            | Caps a leaked-but-unused link without a revoke UI (FR-005).                   | Plan   |
| Who can invite                   | Admin or adult member                               | Exactly the PRD role table / FR-005.                                          | Plan   |
| Pending-invites roster           | None (fire-and-forget)                              | Keeps the slice on generate → share → join; management is S-09.               | Plan   |
| Concurrent invites               | Many allowed                                        | Natural for inviting several people; each still single-use + expiring.       | Plan   |
| Join entry                       | Dedicated public `/join/:token` page                | One clear entry; token survives the auth hop; bypasses the household guards.  | Plan   |
| New-account creation             | Reuse existing register, then auto-join             | Leans on proven S-01 register/login; no duplicated registration logic.       | Plan   |
| Already-in-household recipient   | Block (409)                                          | Enforces FR-007; switching households is a v2 Non-Goal.                       | Plan   |
| Post-join landing                | Straight to the shared board                        | Delivers the multiplayer payoff immediately.                                 | Plan   |
| Live transport (F-03)            | Poll board on interval, pause on hidden tab         | Meets NFR-1's 5s contract, bounds server load on B1.                          | Plan   |
| Poll vs in-progress UI           | Suppress refetch during drag / open dialog          | Prevents a poll yanking the board out from under a reorder/edit.             | Plan   |
| Single-use correctness           | `rowversion` optimistic concurrency on the invite   | Consume is one atomic `SaveChanges`; the concurrent loser gets 410.          | Plan   |
| Test coverage                    | Full lifecycle + cross-household isolation           | Locks FR-005 single-use, FR-007 one-household, US-02 scoping — the named risk. | Plan   |

## Scope

**In scope:** `HouseholdInvite` entity (single-use + expiry + concurrency token); generate / public-preview /
accept endpoints with all guards; `InviteService`; board invite affordance; public `/join/:token` page with
register/login threading; one-household block; polling transport (F-03) with visibility + interaction pause;
integration + vitest tests.

**Out of scope:** invite emails; explicit revoke UI / pending-invites roster; member roster panel (S-09);
leave-and-switch household (v2); combined join-and-register endpoint; SignalR push; role management on join;
password reset / refresh-token changes (S-08/S-10).

## Architecture / Approach

Backend-first (mirrors S-02/03/04). A new DB-backed token table is the backbone: **generate** mints a CSPRNG
token scoped to the caller's household; **preview** is public and returns only the household name; **accept**
consumes the token and inserts a membership row in one `SaveChanges`, guarded by a `rowversion` (single-use)
and the `UserId` unique index (one-household). The SPA adds a public `/join/:token` page that orchestrates the
preview → register/login (token in the route, `returnUrl` through auth) → accept → board path. Polling is an
interval over the existing `TaskService.load()`, paused on a hidden tab and during drag/dialog.

## Phases at a Glance

| Phase                              | What it delivers                                              | Key risk                                              |
| ---------------------------------- | ------------------------------------------------------------ | ----------------------------------------------------- |
| 1. Backend — invite model + API    | Token entity + migration + generate/preview/accept + tests   | Single-use concurrency correctness; cross-household scoping |
| 2. Frontend — invite + join flow   | InviteService, board affordance, public join page, routing   | Threading the token through register→login cleanly    |
| 3. Live board — polling (F-03)     | Interval polling with visibility + interaction pause         | A poll disrupting an in-progress drag/edit; interval leaks |

**Prerequisites:** S-02 (delivered); S-04 board (drag + dialog) in place — its state is reused by the poll
pause. F-03 is folded in as Phase 3 (it was never built).
**Estimated effort:** ~3 sessions across 3 phases.

## Open Risks & Assumptions

- **Single-use under concurrency** is the load-bearing correctness path — the `rowversion` guard must make
  consume atomic; a bare read-then-write would race. Locked by an integration test.
- **Token survival across register→login** is the fiddly UX path; the token lives in the route param and
  `returnUrl`, never server-side session state — a dropped token just means re-opening the original link.
- Polling assumes a live connection (no offline mode, per Non-Goal) and is paused on hidden tabs to bound load.

## Success Criteria (Summary)

- A second adult joins via a shared link (registering in-flow if needed) and both members see the same board.
- A change by one member appears for the other within 5 seconds with no manual refresh.
- Consumed/expired tokens are rejected, already-in-household recipients are blocked, and no token crosses households.
