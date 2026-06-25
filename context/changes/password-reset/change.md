---
change-id: password-reset
roadmap-id: S-08
title: Password reset — request an emailed link and set a new password
status: implemented
created: 2026-06-25
updated: 2026-06-25
prd-refs: [FR-020]
prerequisites: [S-01]
---

# Password reset

A registered user who has forgotten their password can request a reset email and set a new
password from a time-limited, same-origin link (FR-020). This is the single permitted v1
transactional-email use — delivered via Azure Communication Services Email (switched from SendGrid
on 2026-06-25; see roadmap Open Q #2), reset-only, so it never balloons into a general email surface.

See `plan-brief.md` (start here) and `plan.md` (full contract).
