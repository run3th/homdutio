---
change-id: member-administration
roadmap-id: S-09
title: Member administration — admin promote/demote and remove
status: impl_reviewed
created: 2026-06-12
updated: 2026-06-12
prd-refs: [FR-008, FR-009]
prerequisites: [S-06]
---

# Member administration

An admin can view the household roster and **promote a member to admin**, **demote an admin back to member**, and **remove a member** from the household. Promotion is a role-string flip (no migration); removal deletes the `HouseholdMember` row while preserving durable task attribution (NFR-3) and sweeps the removed member's in-progress tasks back to To do.

See `plan-brief.md` (start here) and `plan.md` (full contract).
