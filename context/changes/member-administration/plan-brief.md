# Member Administration — Plan Brief

> Full plan: `context/changes/member-administration/plan.md`

## What & Why

Roadmap slice **S-09 (FR-008, FR-009)**: give a household admin a way to manage who's in the household and who's an admin — **promote** a member to admin, **demote** an admin back, and **remove** a member. It's the last nice-to-have on the multiplayer track (gated on S-06's invite/join), closing the loop on household membership management.

## Starting Point

The membership domain and authorization patterns are already built and waiting for this slice. `HouseholdMember` carries a string `Role` (the entity comment literally says *"S-09 promotes/demotes by updating this value"*), the admin-gate pattern (`Forbid()` + server-derived scope from the JWT `sub`) is canonical across `TaskEndpoints.cs`, and the S-11 shell reserved a "Members" sidebar slot. What's missing: any member-list, promote, or remove endpoint, and any member-management UI.

## Desired End State

An admin opens **Members** in the sidebar, sees the household roster (name, email, role), and can promote/demote members (immediate) or remove them (after a confirmation dialog). Removing a member returns their in-progress tasks to To do and preserves their closed-task audit record. The server refuses to orphan the household (last-admin guard) or let an admin act on their own row. Non-admins see the roster read-only.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Role-change scope | Promote **and** demote (symmetric) | Role-as-string already supports it; makes a mistaken promotion reversible without eviction. | Plan |
| Self & last-admin safety | Block self-actions + reject any action leaving 0 admins (409) | Closes both footguns — no self-eviction, household always has ≥1 admin. | Plan |
| Removed member's in-progress tasks | Unclaim back to To do (reuse S-05 transition) | No task left stranded with an absent claimer; closed-task attribution untouched (NFR-3). | Plan |
| Audit trail for role/remove | None this slice | Keeps the nice-to-have lean; no FR/NFR requires it; matches edit/reorder emitting no events. | Plan |
| UI surface | Dedicated `/members` page via sidebar nav | Matches the S-11-reserved slot; room for list + actions + confirm dialogs. | Plan |
| Destructive-action confirm | Confirm dialog for **remove only**; role flips immediate | Guards the one irreversible action without nagging on reversible flips. | Plan |
| Roster freshness | Fetch on load + refetch after own action (no polling) | Correct for the acting admin; member admin is rare and off the live-board path. | Plan |

## Scope

**In scope:** roster-read endpoint; promote/demote endpoint; remove endpoint with in-progress-task sweep; self-action + last-admin guards; Members page + sidebar nav + `MemberService`; remove confirm dialog; admin-vs-member rendering; tests both tiers.

**Out of scope:** self-removal / "leave household" flow; membership audit log; roster polling; any schema/migration change; invite-flow changes; demote confirmation; a general settings surface.

## Architecture / Approach

Three new routes on the existing `/api/households` group (`GET …/members`, `POST …/members/{userId}/role`, `DELETE …/members/{userId}`), all reusing the caller-resolution + admin-gate + server-derived-scope pattern. The roster DTO carries server-computed `isSelf`/`canManage` flags so the SPA renders controls from flags (matching how `TaskResponse` ships affordance flags) rather than re-deriving authorization. Removal unclaims the target's in-progress tasks and deletes the membership row in **one transaction**. Frontend adds a routed `/members` page under the S-11 shell, a `MemberService`, a sidebar item, and reuses the `delete-confirm` dialog.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend — roster + role/remove endpoints | Three guarded endpoints + integration tests; no migration | Transaction ordering of the remove→unclaim sweep; getting the last-admin count check right |
| 2. Frontend — Members page + sidebar nav | `/members` page, `MemberService`, sidebar item, confirm dialog | Reusing/relocating the `delete-confirm` component cleanly; ≤400px layout (NFR-2) |

**Prerequisites:** S-06 done (need ≥2 members to manage — use the invite flow to seed a second account).
**Estimated effort:** ~1–2 sessions across 2 phases.

## Open Risks & Assumptions

- The remove→unclaim sweep and membership delete must commit atomically — a partial commit could strand a task claimed by a non-member. (Mitigated by single-`SaveChanges` ordering, called out in Critical Implementation Details.)
- The `delete-confirm` component's current placement under `board/` may couple it to board concerns; lifting it to shared may be needed — decided during implementation.
- No polling means two admins editing concurrently won't see each other's changes until reload — accepted for a rare admin screen.

## Success Criteria (Summary)

- An admin can promote/demote members and remove them from a dedicated Members page; non-admins see it read-only.
- The household can never be left with zero admins, and an admin can't act on their own row — both enforced server-side.
- A removed member's in-progress tasks return to To do unassigned while their closed-task audit record stays intact (NFR-3).
