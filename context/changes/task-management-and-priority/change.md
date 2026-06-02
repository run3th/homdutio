---
change_id: task-management-and-priority
title: Task management and priority
status: implemented
created: 2026-06-02
updated: 2026-06-02
archived_at: null
---

## Notes

<!-- Free-form notes for this change: links, ad-hoc context, decisions that don't belong in research/frame/plan. -->

S-04 — edit/delete (To-do-only) + drag-reorder within a column. Roadmap: `context/foundation/roadmap.md` (S-04, lines 168-178).
PRD refs: FR-011 (edit), FR-012 (delete), FR-021 (reorder = the only priority surface in v1).
Prerequisite S-03 (accountability-loop) is delivered (commits `d0b3b71`→`07843f8`).

Key planning decisions (2026-06-02): integer `SortOrder` (reindex on move); all three columns reorderable;
Angular CDK drag-drop; CDK Dialog task-detail/edit panel (extensible toward a future assignee field);
delete lives in the panel with inline confirm; hard delete (no events); server-computed `canEdit`/`canDelete`
affordance flags; reorder persists on drop + refetch (last-write-wins, consistent with S-03; live sync is S-06).
