# New Design Adoption + Task Tags + Emailed Invites — Plan Brief

> Full plan: `context/changes/redesign-tags-and-email-invites/plan.md`
> Research: `context/changes/redesign-tags-and-email-invites/research.md`

## What & Why

Adopt the Claude-design mockups (`templates/Homdutio Pro.html`, `templates/Homdutio Auth Pro.html`) as a **structural redesign** of the Angular SPA — new tokens, IBM Plex fonts, and rebuilt shell/board/dialogs/members/auth — then layer the **task-tags** and **emailed-invite** features onto the redesigned surfaces. (Supersedes the earlier token-only repalette, which delivered color-only and didn't match the mockup's layout.)

## Starting Point

A clean, componentized Angular 21 SPA reskinned once before: a single CSS-custom-property token block drives the look, components are scoped-SCSS + inline-SVG, the shell is a 4.75rem icon rail + simple topbar, cards show a single `Category`, and invites are copy-link only. The mockups are full structural redesigns (decoded from their bundle template scripts) using IBM Plex, a top header + 176px text sidebar, multi-tag colored chip cards, and modals that already include the tag chip-input and an invite email field.

## Desired End State

The SPA looks like the mockups end-to-end (IBM Plex, teal, top-header shell, restructured cards, restyled dialogs/members/auth) with drag-drop and ≤400px behavior intact; tasks carry multiple color-coded tags with per-household autocomplete; and invites can optionally be emailed (server-built `/join/<token>` link) with the reset email sharing the same templating.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Scope of "new design" | Full structural adoption, not repalette | The user confirmed the mockup HTML/layout must be adopted, not just colors | Plan |
| Phase ordering | Design foundation first, features layer on | Build each control (tag chip-input, invite email) once, in its final design, against a working backend | Plan |
| Fonts | Self-host IBM Plex Sans + Mono | Faithful to the mockup; keeps the no-CDN, offline `@fontsource` pattern | Plan |
| Tag colors | Deterministic per-tag colors (known map + hash) | Matches the mockup; purely client-side, no schema impact | Plan |
| Auth extras | Show/hide toggle + live password-rule checklist | Full mockup parity; checklist mirrors the existing server policy | Plan |
| Members page | Included in the design phase | Reachable one click from the new sidebar; keeps the shell consistent | Plan |
| Component budget | Stay within 8 kB (lean on tokens/globals) | Keeps the prior redesign's performance guardrail honest | Plan |
| Tags persistence | `TaskTag` child table, `Category` backfilled then retired | Matches the house one-to-many pattern; additive migration | Research |
| Invite emailing | Optional email added to copy-link flow | Keeps the working copy-link path; widens `IEmailSender` + server-side link | Research |

## Scope

**In scope:** IBM Plex fonts + token overhaul; rebuilt shell (top header, workspace pill, text sidebar, mobile FAB nav); restructured board/cards; restyled dialogs + members; redesigned auth (show/hide + live checklist + check-inbox); `TaskTag` table + migration/backfill + normalization + DTOs + suggestions endpoint + chip input; emailed invites (templating, `IEmailSender` widen, server-side link, email field).

**Out of scope:** dark theme; normalized tag registry / tag management; pending-invite roster / revoke / resend; background email queue; new email provider; changes to CDK dialog/drag-drop mechanisms; dropping the `Category` column this deploy; raising the 8 kB budget.

## Architecture / Approach

Five design phases (foundation → shell → board/cards → dialogs/members → auth) translate the mockup's inline styles into `styles.scss` tokens + scoped SCSS, each shippable and visually verifiable against today's functionality. Two feature phases then build tags (EF child table + household-scoped suggestions endpoint + chip component into the redesigned modal) and emailed invites (embedded-HTML renderer + widened `IEmailSender` + server-side link + email field) onto those surfaces, following existing minimal-API/EF/`HouseholdScope` and reactive-forms/signals patterns.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Foundation | Tokens + IBM Plex + global primitives | Font payload; budget creep |
| 2. Shell | Top header + text sidebar + mobile FAB nav | Preserving ≤400px (NFR-2) through the rebuild |
| 3. Board & cards | Restructured columns + cards | Keeping CDK drag-drop intact |
| 4. Dialogs & members | All five dialogs + members restyled | Behavior regressions in CDK dialogs |
| 5. Auth | Card auth + show/hide + live checklist | Anti-enumeration preserved on forgot |
| 6. Task tags | `TaskTag` backend + chip input + multi-tag cards | Additive migration + isolation coverage |
| 7. Emailed invites | Templating + invite email field | Reset-email regression |

**Prerequisites:** EF local tool restored (`dotnet tool restore`); migrations applied out-of-band per `MIGRATIONS.md`.
**Estimated effort:** ~7 sessions (one per phase), `/clear` between handoffs.

## Open Risks & Assumptions

- The 8 kB per-component style budget may be tight for the rebuilt shell/card/dialog SCSS — mitigated by pushing shared rules into global primitives; if a component genuinely can't fit, revisit with the user (decision was to stay within budget).
- The mockup's `<1000px` mobile threshold must be reconciled with the existing NFR-2 ≤400px contract (plan keeps the ≤400px guarantees).
- A partial teal token swap from the earlier implement attempt sits uncommitted in `web/src/styles.scss`; Phase 1 absorbs it.

## Success Criteria (Summary)

- Every screen matches the mockups at desktop and ≤400px; drag-drop and lifecycle actions unregressed.
- Tasks carry multiple autocompleted, color-coded tags; migrated `Category` values appear as initial tags.
- Invites can be emailed with a working `/join/<token>` link; reset email and copy-link paths unregressed.
