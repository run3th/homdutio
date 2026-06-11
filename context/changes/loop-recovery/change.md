---
change_id: loop-recovery
title: Loop recovery + task comments — unclaim, admin send-back, free-form comments (S-05)
status: implementing
created: 2026-06-11
updated: 2026-06-11
---

## Notes

Roadmap S-05 (`accountability-loop` recovery). Originally scoped to the two FR-022/FR-023
transitions — **unclaim** (claimer frees a stuck in-progress task) and **send-back** (admin
returns a Done task to In progress with a short comment). During `/10x-plan` the scope was
**expanded by explicit product decision (2026-06-11)** to a **full free-form task-comments
feature** (any member, anytime; immutable thread; 💬 count badge + author/timestamp history in
the detail dialog) plus **admin-anytime task-field editing**.

**This overrides two settled boundaries** and must be recorded back into the foundation docs
(Phase 5): the PRD Non-Goal *"No comments, multimedia, or chat on tasks"* and FR-011's
*"any member edits while in To-do"* model (now admin-only, any column).

See `plan.md` / `plan-brief.md`. Prereq S-03 is done; builds directly on `TaskEndpoints.cs`,
`TaskEvent`, and the S-11 board UI (which already reserves Unclaim / Send-back menu slots).
