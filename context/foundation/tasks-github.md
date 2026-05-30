---
project: Homdutio
doc: tasks-github
status: active
created: 2026-05-29
source: context/foundation/roadmap.md (v1)
repo: run3th/homdutio
---

# GitHub Backlog: Homdutio

> Record of the roadmap → GitHub Issues migration performed on 2026-05-29.
> `context/foundation/roadmap.md` remains the source-of-truth narrative; this file documents
> how each roadmap item maps onto a tracked GitHub issue and how the backlog is structured.

## What was done

All 13 roadmap items (4 foundations `F-01`–`F-04`, 9 slices `S-01`–`S-09`) plus the 2 cross-cutting
Open Roadmap Questions were migrated into **GitHub Issues** on `run3th/homdutio` (private repo).
The migration created a label taxonomy, one milestone, and 15 issues with dependency cross-links
baked into each body.

**Task management system chosen:** GitHub Issues. The repo's only remote is
`https://github.com/run3th/homdutio.git`, `gh` CLI v2.93.0 is installed, and the roadmap's own
Backlog Handoff table was written for any MCP-backed backlog — GitHub Issues is the in-repo
equivalent already wired up.

**Decisions taken during the session:**
- Scope: Issues + labels + a single milestone (no GitHub Projects v2 board).
- Titles: `[F-01] <suggested title>` prefix so the stable roadmap ID stays searchable.
- Open Questions: 2 separate `type:decision` issues, cross-linked from the items they block.
- `roadmap.md`: left unchanged (no issue-number back-link column added).

## Updates since migration

**2026-05-30 — open decisions resolved + auth method changed (roadmap edit), live GitHub issues synced.**
Reflected in the **ID → issue map** and **Dependency wiring** below, and applied to the live GitHub
issues the same day. The "Verification performed" section is a point-in-time record of the original
migration and is left unchanged.

- **DEC-1 (#1)** RESOLVED → **polling** (not SignalR). Unblocked F-03 (#5) and S-06 (#12). Issue #1 **closed** with a resolution comment.
- **DEC-2 (#2)** RESOLVED → **SendGrid** (ACS needed a verified domain; SendGrid single-sender verification is lighter). Informs S-08 (#14). Issue #2 **closed** with a resolution comment.
- **F-02 (#4)** auth changed from **cookie sessions → JWT bearer tokens** (Identity still the user store). Title updated.

GitHub sync applied (2026-05-30): retitled #4 (JWT) and #5 (polling); #5 `status:blocked`→`status:ready`; #12 `status:blocked`→`status:proposed`; closed #1 and #2 with resolution comments. (Closed decision issues #1/#2 keep their old `status:*` labels — harmless once closed.)

## ID → issue map

| Roadmap ID | Issue | Title | Labels | Milestone |
| ---------- | ----- | ----- | ------ | --------- |
| DEC-1 | [#1](https://github.com/run3th/homdutio/issues/1)  | [DEC-1] Live-update transport: polling vs SignalR | `type:decision` — RESOLVED: polling (close #1) | — |
| DEC-2 | [#2](https://github.com/run3th/homdutio/issues/2)  | [DEC-2] Transactional email provider for password reset | `type:decision` — RESOLVED: SendGrid (close #2) | — |
| F-01  | [#3](https://github.com/run3th/homdutio/issues/3)  | [F-01] Wire EF Core + provisioned Azure SQL persistence baseline | `type:foundation` `status:ready` `stream:a` | v1 MVP |
| F-02  | [#4](https://github.com/run3th/homdutio/issues/4)  | [F-02] Mount ASP.NET Identity + JWT bearer pipeline | `type:foundation` `status:proposed` `stream:a` | v1 MVP |
| F-03  | [#5](https://github.com/run3th/homdutio/issues/5)  | [F-03] Establish 5s live-update transport via polling | `type:foundation` `status:ready` `stream:b` | v1 MVP |
| F-04  | [#6](https://github.com/run3th/homdutio/issues/6)  | [F-04] GitHub Actions build+smoke gate + auto-deploy on merge | `type:foundation` `status:ready` `stream:d` | v1 MVP |
| S-01  | [#7](https://github.com/run3th/homdutio/issues/7)  | [S-01] Register, log in, log out | `type:slice` `status:proposed` `stream:a` | v1 MVP |
| S-02  | [#8](https://github.com/run3th/homdutio/issues/8)  | [S-02] Create household + empty mobile-first kanban board | `type:slice` `status:proposed` `stream:a` | v1 MVP |
| S-03  | [#9](https://github.com/run3th/homdutio/issues/9)  | [S-03] Task lifecycle: create → claim → done → admin-confirm | `type:slice` `status:proposed` `stream:a` `north-star` | v1 MVP |
| S-04  | [#10](https://github.com/run3th/homdutio/issues/10) | [S-04] Edit/delete tasks + drag-reorder priority | `type:slice` `status:proposed` `stream:a` | v1 MVP |
| S-05  | [#11](https://github.com/run3th/homdutio/issues/11) | [S-05] Unclaim + admin send-back with comment | `type:slice` `status:proposed` `stream:a` | v1 MVP |
| S-06  | [#12](https://github.com/run3th/homdutio/issues/12) | [S-06] Single-use invite, join, live shared board | `type:slice` `status:proposed` `stream:b` | v1 MVP |
| S-07  | [#13](https://github.com/run3th/homdutio/issues/13) | [S-07] Enforce + verify no cross-household leakage | `type:slice` `status:proposed` `stream:c` | v1 MVP |
| S-08  | [#14](https://github.com/run3th/homdutio/issues/14) | [S-08] Password reset via emailed link | `type:slice` `status:proposed` `stream:c` | v1 MVP |
| S-09  | [#15](https://github.com/run3th/homdutio/issues/15) | [S-09] Admin promote / remove member | `type:slice` `status:proposed` `stream:b` | v1 MVP |

> Issue numbers were deterministic: the repo had an empty number space (no prior issues or PRs),
> so creation order mapped cleanly to `#1`–`#15`, letting the real `#N` cross-links be written
> directly into each body in a single pass.

## Label taxonomy

| Label | Color | Meaning |
| ----- | ----- | ------- |
| `type:foundation` | `1d76db` | Foundation / enabler item from the roadmap |
| `type:slice` | `0e8a16` | Vertical, user-visible slice from the roadmap |
| `type:decision` | `d4c5f9` | Cross-cutting open decision to resolve |
| `status:ready` | `2cbe4e` | Ready to pick up |
| `status:proposed` | `fbca04` | Proposed; prerequisites pending |
| `status:blocked` | `b60205` | Blocked on an open decision |
| `stream:a` | `c5def5` | Stream A — Accountability spine |
| `stream:b` | `c5def5` | Stream B — Multiplayer & freshness |
| `stream:c` | `c5def5` | Stream C — Hardening & recovery |
| `stream:d` | `c5def5` | Stream D — Delivery automation |
| `north-star` | `5319e7` | North-star validation milestone (S-03 only) |

The default GitHub labels (`bug`, `enhancement`, etc.) were left in place, untouched.

**Milestone:** `v1 MVP` — assigned to all 13 F/S issues. The 2 decision issues are intentionally
**not** on the milestone (they are decisions to resolve, not deliverables).

## Dependency wiring

Prerequisites and blockers are expressed as `#N` references inside each issue body's
`## Dependencies` / `## Unknowns` sections (text links, not GitHub's native blocked-by relations):

- F-02 → #3 (F-01)
- F-03 → DEC-1 resolved (polling); no longer blocked
- S-01 → #3 (F-01), #4 (F-02)
- S-02 → #7 (S-01)
- S-03 → #8 (S-02)
- S-04 → #9 (S-03)
- S-05 → #9 (S-03)
- S-06 → #8 (S-02), #5 (F-03); DEC-1 resolved (polling), no longer blocked
- S-07 → #9 (S-03)
- S-08 → #7 (S-01); see #2 (DEC-2)
- S-09 → #12 (S-06)
- DEC-1 (RESOLVED: polling) previously blocked #5 (F-03), #12 (S-06) — now unblocked
- DEC-2 (RESOLVED: SendGrid) informs #14 (S-08), non-blocking

## Issue body structure

Each F/S issue follows one template, populated verbatim from the roadmap fields:
`Roadmap ID · Change ID · Stream` header, then `## Outcome`, `## PRD refs`, `## Dependencies`
(Prerequisites / Parallel with / Unlocks), `## Unknowns`, `## Risk`, and a footer pointing back
to `context/foundation/roadmap.md` with the status at migration time.

Decision issues use a slimmer body: `## Question`, `## Context`, and which items they block/inform.

## How it was created

`gh` CLI, authenticated as `run3th` (token scopes: `repo`, `workflow`, `read:org`, `gist`).
Direct `gh` commands — no helper script:
- Labels: `gh label create <name> --color <hex> --description <text> --force` (idempotent).
- Milestone: `gh api repos/run3th/homdutio/milestones -f title="v1 MVP" -f description=...`
  (gh has no native milestone-create command).
- Issues: `gh issue create --title ... --label ... --milestone "v1 MVP" --body-file -`, with the
  body piped via a **quoted heredoc** (`<<'EOF'`) so backticks and `$` in the markdown stayed
  literal and were not shell-expanded.

## Verification performed

- `gh issue list` — 15 issues, titles correctly prefixed `[F-0x]/[S-0x]/[DEC-x]`.
- Counts: 4 `type:foundation`, 9 `type:slice`, 2 `type:decision`.
- `status:ready` → #3, #6 · `status:blocked` → #1, #5, #12 · `north-star` → #9 only.
- All 13 F/S issues on `v1 MVP`; decision issues excluded.
- Spot-checked S-06 (#12): body shows resolved `#8 (S-02)`, `#5 (F-03)`, `#1 (DEC-1)`.

## Maintenance notes

- **Re-running the creation commands would duplicate issues** — there is no natural dedupe. To
  resume a partial migration, list existing issues and create only the missing IDs.
- **Numbering caveat:** the `#N` links are correct only because the repo started empty. If issues
  are deleted and recreated, numbers will not be reusable and links may drift.
- When a change is implemented and archived via `/10x-archive`, flip the corresponding issue's
  `status:*` label and close it; the roadmap's own `## Done` section is updated separately.

## Open follow-ups (not done)

- Add a "GitHub" column to the roadmap's Backlog Handoff table linking each row to its issue.
- Create a GitHub Projects v2 board (kanban view) over these issues.
- Convert the text `#N` prerequisites into GitHub's native sub-issue / blocked-by relationships.
