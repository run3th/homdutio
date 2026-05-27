---
project: "Homdutio"
context_type: greenfield
created: 2026-05-27
updated: 2026-05-27
product_type: web-app
target_scale:
  users: small
  qps: low
  data_volume: small
timeline_budget:
  mvp_weeks: 3
  hard_deadline: null
  after_hours_only: true
checkpoint:
  current_phase: 8
  phases_completed: [1, 2, 3, 4, 5, 6, 7]
  gray_areas_resolved:
    - topic: "primary persona scope"
      decision: "specific named user — builder's own household; broader audience deferred"
    - topic: "load-bearing insight"
      decision: "undecided — three candidates kept open (priority visibility, accountability via record, shared-household context); routed to Open Questions"
    - topic: "auth strategy"
      decision: "email + password"
    - topic: "v1 role model"
      decision: "two roles per household — admin + adult member; child role deferred to v2"
    - topic: "admin confirmation scope"
      decision: "every completed task requires admin confirmation before closing"
    - topic: "v1 household scope"
      decision: "each user belongs to exactly one household in v1; multi-household membership deferred to v2"
    - topic: "v1 invite delivery"
      decision: "invite link shared out-of-band by the inviter; no email pipeline in v1"
    - topic: "closed task visibility"
      decision: "closed tasks disappear from the board; audit trail preserved server-side"
    - topic: "secondary success criterion"
      decision: "none — primary flow is the whole MVP"
    - topic: "guardrails"
      decision: "household privacy (no cross-household leakage); honest record (admin cannot confirm own claimed task)"
    - topic: "mvp_weeks target"
      decision: "3 weeks of after-hours work"
    - topic: "v2 child onboarding (post-finalize resolution of OQ #2)"
      decision: "hybrid — parent creates the account; child can later claim ownership by adding their own email"
    - topic: "self-attest abuse mechanism (post-finalize resolution of OQ #5)"
      decision: "leave as-is permanently in v1 and v2; flag preserved, never surfaced in UI"
  frs_drafted: 23
  quality_check_status: accepted
---

# Homdutio — Shape Notes

Seed idea (verbatim from `idea-notes.md`): a shared family task-management app where each user has one account but can belong to multiple families. Families collaborate on a kanban board of household chores; tasks are claimed, completed, then admin-confirmed before close.

## Vision & Problem Statement

A parent in a shared household walks through the door at the end of the day and faces invisible work: chores exist, but nothing in the environment tells them what matters most right now. The default move is the visible chore (washing dishes), not the priority one (taking out the bins before pickup tomorrow). Today's coordination relies on memory, nagging, and verbal handoffs — there is no shared record of what's already been done or who did it, so unfair load goes unnoticed and the same person keeps doing the visible work.

The insight Homdutio is built around is still being narrowed (see Open Questions), but three candidate angles are kept open: (1) **priority visibility** — the app surfaces what matters most so household members don't default to the visible chore; (2) **accountability via record** — every action carries a name, timestamp, and admin confirmation, making contributions and gaps socially visible; (3) **shared-household context** — one account can belong to multiple households at once (e.g., split-parenting, flatshares, parents' house), with seamless switching.

## User & Persona

**Primary persona — the household coordinator (the builder's own household first).**
Role: an adult in a shared household with ≥ 2 adults and (in the builder's case) children. The persona is responsible for keeping the household running but does not want to be the sole holder of "what needs doing". The MVP is built for the builder's household first; generalizing to other parents is deliberately deferred.

### Secondary persona — household members (adults and children)
Every household member, including children/teenagers, is a first-class user with their own account. They claim tasks ("Biorę to"), mark them done, and earn confirmation from an admin. The MVP assumes children are old enough to use a phone/web app independently.

## Access Control

Authentication: email + password. Account confirmation and password reset via email. One account per person.

**v1 scope**: each user belongs to exactly one household. Multi-household membership (one account in several households with context switching) is part of the long-term model but is deferred to v2.

**v1 roles** — two roles per household. The same architectural model accommodates a third "child" role in v2:

| Capability | Admin | Adult member |
| --- | --- | --- |
| Create, edit, delete tasks | ✓ | ✓ |
| Claim a task ("Biorę to") | ✓ | ✓ |
| Mark a claimed task done | ✓ | ✓ |
| Confirm someone else's completed task | ✓ | — |
| Generate invite links to new members | ✓ | ✓ |
| Promote/demote roles within the household | ✓ | — |
| Remove a member from the household | ✓ | — |

Invite flow (v1): a member with invite capability generates a shareable invite link inside the app. The link is shared out-of-band (SMS, WhatsApp, in person) — Homdutio v1 does NOT send invitation emails. The recipient clicks the link; if they have no account, the click-through creates the account and joins the household in one flow. Multiple admins per household are allowed (the household creator is the first admin and may promote others).

**Deferred to v2**: child role with reduced capabilities (cannot invite; simplified UI); multi-household membership and switching; parent-managed child accounts.

## Success Criteria

### Primary
A new household coordinator, starting from a fresh signup, can complete the full task lifecycle end-to-end in one session:
1. Register and create a household (becomes admin).
2. Generate an invite link and share it out-of-band with a second adult.
3. The second adult opens the link, creates an account, and joins the household.
4. Both members see the shared kanban board (To do / In progress / Done).
5. The admin adds a task ("take out bins").
6. The other member claims it — it moves to In progress with the claimer's name.
7. The other member marks it done — it moves to Done.
8. The admin confirms it — the task closes and disappears from the board.

The MVP is considered to work when this flow completes end-to-end without manual intervention outside the app (no email pipeline, no admin DB fixups) and both users see the same state at every step.

### Secondary
None for v1. The primary flow IS the whole MVP. Anything that would otherwise be a "nice to have" (today's-done view, category tags, notifications) is explicitly out of scope and lives in `## Non-Goals` or `## Forward: v2`.

### Guardrails
- **Household privacy**: only members of a household can see that household's tasks. No cross-household leakage under any circumstance — failure here is the worst possible bug.
- **Honest record**: an admin cannot confirm a task they themselves claimed. Self-confirmation defeats the accountability rule that justifies the whole product. (Implication: a single-admin household where the admin claims a task creates a deadlock — see Open Questions.)

## Functional Requirements

### Authentication & Account
- FR-001: A person can register a new account using email + password. Priority: must-have
  > Socratic: Counter considered: "without email pipeline, forgotten passwords brick the account." Resolution: permit one transactional email service for password reset only (FR-020). The 'no email pipeline' cut from Phase 2 narrows to 'no invite emails', which was the expensive part.
- FR-002: A registered user can log in using their email + password. Priority: must-have
  > Socratic: bundled with FR-001 resolution.
- FR-003: A logged-in user can log out. Priority: must-have
  > Socratic: trivial; no counter-argument.
- FR-020: A registered user can request a password-reset email; clicking the link in the email lets them set a new password. Priority: must-have
  > Socratic: new FR from FR-001's resolution. The single permitted email use in v1.

### Household & Membership
- FR-004: A logged-in user with no household can create a new household and becomes its first admin. Priority: must-have
  > Socratic: trivial; no counter-argument.
- FR-005: An admin or adult member can generate a single-use shareable invite link to their household; once consumed, the link is invalidated. Priority: must-have
  > Socratic: Counter considered: "leaked link grants strangers access." Resolution: single-use links. If unused, admin can revoke; if used by the wrong person, only one mistake is possible.
- FR-006: A person opening a valid, unconsumed invite link can join the household; if they have no account, the same flow creates one and joins them. Priority: must-have
  > Socratic: bundled with FR-005 resolution.
- FR-007: A user belongs to at most one household at a time. Priority: must-have
  > Socratic: Counter considered: "contradicts the original multi-household vision." Resolution: v1 UX is single-household, but the data model supports multi-membership from day 1 (user_households join). v2 adds the switcher UI without a schema migration.
- FR-008: An admin can promote an adult member to admin. Priority: nice-to-have
  > Socratic: no counter-argument; remains nice-to-have for v1.
- FR-009: An admin can remove an adult member from the household. Priority: nice-to-have
  > Socratic: no counter-argument; remains nice-to-have for v1.

### Task Lifecycle
- FR-010: An admin or adult member can create a task with a title, optional description, optional category, and automatic creation timestamp; new tasks land in "To do" unassigned. Priority: must-have
  > Socratic: Counter considered: "no priority field — contradicts the pain of 'I default to dishes instead of priorities'." Resolution: priority emerges from column ordering (FR-021), not a discrete field. Top of "To do" is the priority.
- FR-011: A member can edit a task's title / description / category while it is in "To do". Priority: must-have
  > Socratic: Counter considered: "what about typos / mistakes once a task is in 'In progress'?" Resolution: edit remains "To do"-only; the abandon case is handled by FR-022 (unclaim).
- FR-012: A member can delete a task while it is in "To do". Priority: must-have
  > Socratic: bundled with FR-011 resolution.
- FR-013: An admin or adult member can claim an unassigned task; claiming moves it to "In progress" and records the claimer. Priority: must-have
  > Socratic: Counter considered: "stale claim — task sits in 'In progress' forever." Resolution: FR-022 (unclaim) covers it; stale claims are a social problem, not a software problem beyond unclaim.
- FR-014: The claimer can mark their claimed task as done, moving it to "Done". Priority: must-have
  > Socratic: bundled with FR-013 resolution.
- FR-015: An admin can confirm a "Done" task that they did NOT claim; confirmation closes the task and removes it from the board. Priority: must-have
  > Socratic: Counter considered: "what if the work is sloppy and admin disagrees?" Resolution: add FR-023 — admin can "send back" a "Done" task to "In progress" with a comment.
- FR-016: An admin can self-confirm a "Done" task they themselves claimed; the closure is recorded as self-attested. Priority: must-have
  > Socratic: Counter considered: "lazy admin self-attests forever, defeating accountability." Resolution: v1 records the flag but does not enforce a cap; social pressure of visibility (in a future archive view) is the only deterrent. Cap-or-no-cap revisited in v2.
- FR-021: A member can reorder tasks within a column by drag-and-drop; the order is shared across all household members. Priority: must-have
  > Socratic: new FR from FR-010's resolution. Order in "To do" IS priority.
- FR-022: The claimer of a task in "In progress" can unclaim it, returning the task to "To do" unassigned. Priority: must-have
  > Socratic: new FR from FR-011/013 resolution. Closes the "stuck in progress" failure mode.
- FR-023: An admin reviewing a task in "Done" can send it back to "In progress" with a short comment; the original claimer remains attached. Priority: must-have
  > Socratic: new FR from FR-015's resolution. Closes the "sloppy work" failure mode; preserves the audit trail.

### Board View
- FR-017: A household member can view the household's kanban board with three columns (To do / In progress / Done). Priority: must-have
  > Socratic: convention from idea-notes; no counter-argument.
- FR-018: Each task on the board displays its title, claimer (if any), and creation timestamp. Priority: must-have
  > Socratic: trivial display contract; no counter-argument.

### Privacy Boundary
- FR-019: A user cannot view, create, claim, or confirm tasks belonging to a household they are not a member of. Priority: must-have
  > Socratic: guardrail FR; no counter-argument worth recording — failure here is the worst possible bug.

## User Stories

### US-01: Household admin completes the full task lifecycle with another member

- **Given** an admin who has just registered and created a household, and an invited adult member who has just joined
- **When** the admin creates a task ("take out bins"), the other member claims it, marks it done, and the admin confirms it
- **Then** the task closes and disappears from the board; both members observe the same state at every transition

#### Acceptance Criteria
- The admin observes the task move from "To do" → "In progress" (with the claimer's name) → "Done", then disappear after confirmation
- The other member observes the same transitions
- After closure, the audit trail (who created, who claimed, who confirmed, timestamps) is preserved server-side even though the task is no longer visible on the board
- If the admin claims and completes the task themselves, the admin can confirm it; the closure is recorded with a `self-attested` flag (per FR-016)

### US-02: Privacy across households

- **Given** two separate households (A and B) exist, each with members and tasks
- **When** a user who is a member of household A interacts with the app
- **Then** they see only household A's tasks; no UI path, API response, or direct identifier exposes household B's data

#### Acceptance Criteria
- Task listing endpoints scope by the requesting user's household membership
- A request for a task ID from a different household returns a not-found result (does not leak existence)
- Invite link tokens are scoped to one household; consuming one cannot grant access to another

## Business Logic

**Homdutio attributes every chore action — creation, claim, completion, confirmation — to a named household member with a timestamp and (for closure) an admin verification, so each task carries an honest record of who did what.**

The rule consumes one input per user-initiated state transition: the acting member's identity, the action type, the affected task, and the time of the action. It produces a durable per-task record carrying creator, claimer, confirmer, every transition timestamp, and a `self-attested` flag when the admin confirms their own work. The record outlives the visible task: when an admin confirms a task and it disappears from the board, the record persists for the lifetime of the household (see NFR-3).

In v1 the user encounters the rule at task-card granularity: every card on the board carries the claimer's name and a creation timestamp; the moment of confirmation is the felt accountability event (the admin acknowledges the claimer's work, the task closes). v1 does NOT surface aggregations ("Anna did 5 this week") — aggregation views are explicitly v2. The rule is realized at the unit-of-work level, not the report level; the durability of the record is what makes v2 aggregation possible without re-architecture.

## Non-Functional Requirements

- **NFR-1 — Cross-device freshness.** A state change one member makes is observable to other household members within 5 seconds without manual refresh. The mechanism (polling, websockets, server-sent events) is not specified here.
- **NFR-2 — Mobile-first responsive.** Every primary flow (claim, mark done, confirm, create task) is fully usable on a phone-sized screen no wider than 400 CSS pixels, without horizontal scrolling and without obstructing controls. Native, PWA, or responsive web are all acceptable realizations.
- **NFR-3 — Honest record durability.** Once a task is closed, its audit data — creator identity, claimer identity, confirmer identity, every transition timestamp, and the `self-attested` flag — remains queryable for the lifetime of the household. The board UI may hide it, but the record cannot be deleted by ordinary user action.

## Non-Goals

### Functional non-goals (capabilities v1 explicitly will NOT provide)

- **No built-in priority or urgency algorithm.** v1 does not compute task priority from due dates, claim history, AI, or any signal. Column order (FR-021) is the only priority surface. Rationale: keeps the Phase 5 domain rule frozen on accountability; prevents drift toward becoming a generic task ranker.
- **No aggregation or reporting views ("who did what this week").** v1 realizes the accountability rule at task-card granularity only. The audit trail is durable (NFR-3) but no UI aggregates it. Rationale: aggregation is the v2 surface; shipping v1 without it keeps the 3-week scope honest.
- **No invite emails / no email pipeline beyond password reset.** Invite links are generated in-app and shared out-of-band (SMS, WhatsApp, in person). Password reset (FR-020) is the single permitted transactional email use. Rationale: cuts SES/SendGrid setup, templates, deliverability, bounce handling.
- **No multi-household membership UI.** Each user belongs to exactly one household in v1 (FR-007). The data model supports multi-membership for v2, but no household switcher ships in v1. Rationale: cuts an entire navigation surface; matches builder's stated v1 scope.
- **No child role and no parent-managed accounts.** v1 has two roles only: admin and adult member. Children's accounts and the simplified-UI variant are v2. Rationale: cuts a third role + a parent-onboarding flow.
- **No notifications of any kind.** No push, in-app, or email notifications for task assignment, completion, or admin-review-pending. Listed as out-of-scope in idea-notes; reaffirmed here. Rationale: notification infrastructure is a v2 conversation.
- **No comments, multimedia, or chat on tasks.** Listed as out-of-scope in idea-notes; reaffirmed. Rationale: turns tasks into messages; expands data model and storage; off-charter for a chore tracker.
- **No recurring or scheduled tasks.** Listed as out-of-scope in idea-notes; reaffirmed. Rationale: introduces a cron-like scheduler and template-task model that doubles MVP surface.
- **No AI-generated tasks or AI ranking.** Listed as out-of-scope in idea-notes; reaffirmed.
- **No gamification, points, or leaderboard.** Listed as out-of-scope in idea-notes; reaffirmed. Rationale: tempting feature that doesn't realize the Phase 5 rule; if revisited, must explicitly serve accountability, not just engagement.

### Non-functional non-goals (quality dimensions v1 explicitly will NOT aim for)

- **No native mobile app.** v1 is web only; NFR-2 ensures mobile-responsive use at ≤ 400px. Native iOS/Android is v2+. Rationale: app-store distribution + native build pipelines double the MVP scope.
- **No offline mode.** Connectivity is required for every operation. No local-first sync, no offline queue, no conflict resolution. Rationale: offline-first is a major architectural commitment; v1's freshness NFR (5s) already assumes a live connection.
- **No multi-region SLA or high-availability commitment.** v1 is for the builder's single household. Single-region, single-instance is acceptable.
- **No compliance certifications beyond baseline GDPR hygiene.** v1 does not pursue SOC 2, ISO 27001, HIPAA, or any explicit attestation.

## Forward: v2 candidate scope

Captured here so the next chain step (tech-stack selection) knows what the data model must accommodate. **Not** part of the PRD schema.

- Multi-household membership + switcher UI (data model already supports it via user-household join table — FR-007 Socratic resolution).
- Third role: child (cannot invite, simplified UI). Onboarding follows a hybrid model: parent creates the child's account using a parent-set password; the child can later claim ownership by adding their own email, after which the child becomes the account's identity owner (per resolved OQ #2). v2 must build the "transfer ownership" flow.
- Aggregation views ("recent activity" / "this week per member") realizing the accountability rule at report level. These views do NOT surface the `self-attested` flag (per resolved OQ #5).
- Invite emails (proper template pipeline) as a quality-of-life upgrade over out-of-band sharing.

## Open Questions (running)

1. **Load-bearing insight** — RESOLVED in Phase 5: accountability via record. Priority visibility and shared-household context retired as candidate v1 rules (priority partially realized by FR-021 column ordering, but not the load-bearing rule; shared-household belongs to v2).
2. **Parent-managed child accounts** — RESOLVED: hybrid model for v2. The parent creates the child's account; the child can later claim ownership by adding their own email. Covers pre-email-age kids without permanently coupling the account to the parent. v2 must build a "transfer ownership" flow (child adds email → child receives confirmation → child becomes the account's identity owner).
3. **Single-admin household deadlock** — RESOLVED: admin self-confirms with `self-attested` flag (FR-016).
4. **Password reset without an email pipeline** — RESOLVED: one transactional email service permitted solely for password reset (FR-020). Phase 2 "no email pipeline" cut narrows to "no invite emails".
5. **Self-attest abuse cap** — RESOLVED: leave as-is permanently. v1 AND v2 keep self-attests unconstrained; the `self-attested` flag exists in the audit record but no v1/v2 UI surfaces it (not even within v2 aggregation views). Accepts the limitation as a deliberate product choice; aligns with builder's own-household scope where abuse is unlikely.
