# Join-View Design Fidelity Fixes — Plan Brief

> Full plan: `context/changes/join-view-design-fixes/plan.md`

## What & Why

Two visual defects on the **join view** (shown after clicking an email-invite link) don't match
the design. The "Log in to continue" action renders left-aligned and underlined instead of as a
centered primary button, and the Homdutio house/brand icons are mismatched in size with a stray
shadow. Goal: copy the design exactly.

## Starting Point

Angular 21 app; design source of truth is the CSS token block in `web/src/styles.scss`. The join
view (`web/src/app/join/`) works functionally — these are pure CSS/markup fidelity issues. The
`.btn` primitive is shared by buttons and anchors but lacks anchor-safe rules; three sizes of the
same Homdutio house glyph appear across the auth-logo and join components.

## Desired End State

On the join view, "Log in to continue" is a centered, un-underlined teal primary button identical
to "Accept & join"; the flagged Homdutio icons are equal-sized and render flat with no stray
shadow — matching the design side-by-side.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Login-link fix location | Fix global `.btn` primitive | Root cause is `.btn` missing anchor-safe rules; also fixes "Go to your board" for free | Plan |
| Bug-2 exact values | Confirm against external design at implement time | The design isn't in the repo, so precise px/shadow are read from it during manual verification | Plan |
| Icon interpretation | `.house-chip` (28px) ↔ `.auth-logo-mark` (40px) | Best-supported reading of "join icon & homdutio icon same size"; flagged as an assumption to confirm | Plan |
| Questioning depth | Minimal ("just fix it") | User elected to skip questioning and note assumptions in the plan | Plan |

## Scope

**In scope:** Center + un-underline the `.btn` anchor; equalize the two join-view Homdutio icons; remove the stray shadow.

**Out of scope:** Backend/invite logic, layout/copy redesign, new design tokens, visual-regression tests, the `.info-circle` mark (unless the design says otherwise).

## Architecture / Approach

Phase 1 is a deterministic 3-property addition to the shared `.btn` rule in `styles.scss`. Phase 2
is a design-matching pass: run the join view, compare to the design, and tune icon sizes + remove
the shadow to match. The plan names elements and intent; exact pixel values come from the design.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Center login button | `.btn` anchors render centered, no underline | Global `.btn` change — needs native-button regression check |
| 2. Match Homdutio icons | Equal-sized, flat join-view brand icons | Exact size/shadow are external-design values; interpretation may need retargeting |

**Prerequisites:** Ability to run the Angular app (`cd web && npm start`) and reach a join/invite URL; access to the design to compare against.
**Estimated effort:** ~1 session, 2 small CSS phases.

## Open Risks & Assumptions

- Exact bug-2 values live in an external design (not in the repo) — manual side-by-side comparison is the real gate, not automated checks.
- The two-icons interpretation (`.house-chip` ↔ `.auth-logo-mark`) is an assumption; confirm against the design before editing and retarget if wrong.
- The "minor shadow" isn't declared in static SCSS — locate the actual rule at runtime before removing it.

## Success Criteria (Summary)

- "Log in to continue" is centered, un-underlined, and matches the primary button style.
- The two flagged Homdutio icons are the same size and render flat, matching the design.
- No regression to other primary buttons across the app.
