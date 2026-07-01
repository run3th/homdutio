# Join-View Design Fidelity Fixes Implementation Plan

## Overview

Two design-fidelity bugs on the **join view** (the screen shown after a user clicks an
email-invite link, `web/src/app/join/`):

1. The **"Log in to continue"** action renders left-aligned and underlined instead of as a
   centered primary button.
2. The join view's **Homdutio brand/house icons** don't match the design: the two house
   marks should be the same size, they read smaller than the inviter avatar, and one shows a
   stray shadow the design doesn't have.

Both are isolated CSS/markup fixes in known files. No logic, data-model, or API changes.

## Current State Analysis

**Framework:** Angular 21 + scoped SCSS. Design source of truth = the CSS custom-property
token block in `web/src/styles.scss` (`:root`, lines 13–88) plus global primitives. There is
**no Figma/mockup file in the repo** — "the design" the user is matching against is external,
so exact target pixel values for bug 2 are supplied/confirmed by the human, not derivable
from code.

**Bug 1 — root cause (confirmed):**
- `web/src/app/join/join.component.html:40-42` renders the action as an anchor:
  `<a class="btn" [routerLink]="['/login']" role="button">Log in to continue</a>`.
- The `.btn` rule (`web/src/styles.scss:307-323`) sets `width: 100%` but has **no**
  `display`, **no** `text-align`, and **no** `text-decoration`. On a native `<button>` (the
  sibling "Accept & join", `join.component.html:76`) this is invisible — buttons center their
  label and have no underline. On an `<a>`, the element stays inline (label hugs the left) and
  keeps the browser-default underline.
- The same `<a class="btn">` pattern is also used for **"Go to your board"**
  (`join.component.html:105`), so a fix at the `.btn` level corrects both anchors at once.

**Bug 2 — the elements involved:**
Three instances of the same house SVG glyph appear in the join flow, at different sizes:
- `.auth-logo-mark` — 40px (2.5rem) rounded-square teal mark in the Homdutio lockup at the top
  of every auth/join card (`web/src/app/auth/auth-logo.component.ts:41-50`). No shadow.
- `.house-chip` — 28px (1.75rem) circular teal badge with a 2px white ring, overlaid on the
  inviter avatar's bottom-right corner in the join hero (`web/src/app/join/join.component.scss:37-48`).
  No `box-shadow` declared; the white `border` ring can read as a subtle halo.
- Inviter avatar — 64px (`size="lg"`, `join.component.html:19` & `:52`); avatar size tokens are
  `sm:21 / md:36 / lg:64` (`web/src/app/shared/user-avatar/user-avatar.component.ts:6-10`).
- No `box-shadow`/`filter` is declared on the avatar, house-chip, or auth-logo mark in any
  scoped SCSS — so the reported "minor shadow" is either the house-chip's white ring or a
  runtime-only style to be identified with devtools during implementation.

## Desired End State

Opening an invite link and landing on the join view:
- "Log in to continue" is a full-width, **centered** teal primary button with **no underline**,
  visually identical to the "Accept & join" button below/above it.
- The join view's Homdutio house marks match the design exactly: the two flagged icons are the
  **same size** as each other, sized per the design, and render **flat with no stray shadow**.

Verified by loading the running join view and comparing it side-by-side with the design.

### Key Discoveries:

- `.btn` is shared by anchors and buttons; it lacks anchor-safe rules (`web/src/styles.scss:307-323`).
- Fixing `.btn` globally also fixes the "Go to your board" anchor (`join.component.html:105`) — a bonus, low-risk.
- The house glyph exists at three sizes (40 / 28 / 56px) across `auth-logo.component.ts` and `join.component.scss`.
- No box-shadow is declared on the flagged icons in static SCSS — the shadow source must be confirmed at runtime.

## What We're NOT Doing

- Not redesigning the join flow, copy, or layout — only the two reported defects.
- Not touching the backend, invite-token logic, or routing.
- Not changing the `.info-circle` "you're already in" mark (a different screen/role) unless the design comparison shows it's one of the flagged icons.
- Not introducing new design tokens or a Figma export; we reuse existing tokens and confirm sizes against the external design.
- Not adding automated visual-regression tests (out of scope for this fix; belongs to the test-plan rollout).

## Implementation Approach

Phase 1 is a deterministic global CSS fix. Phase 2 is a design-matching pass whose exact pixel
values are confirmed by running the join view and comparing to the design — the plan names the
elements and the intent; the implementer reads the precise target size/shadow from the design
during manual verification and tunes the values to match.

## Phase 1: Center the "Log in to continue" primary button (remove left-align + underline)

### Overview

Make `.btn` render correctly when applied to an `<a>`, so the login link (and the "Go to your
board" link) look identical to the native primary buttons.

### Changes Required:

#### 1. Global `.btn` primitive

**File**: `web/src/styles.scss` (the `button[type='submit'], .btn` rule, ~lines 307-323)

**Intent**: Add anchor-safe declarations so an `<a class="btn">` centers its label and drops
the browser-default underline, matching native `<button class="btn">`. This is a systemic fix,
not a join-view patch, so every `.btn` anchor in the app renders consistently.

**Contract**: Add `display: block;`, `text-align: center;`, and `text-decoration: none;` to the
`.btn` rule. Native `<button>` appearance is unchanged (already centered, no underline; already
effectively full-width via `width: 100%`). The `.btn--ghost` / `.btn--danger` variants inherit
these and are unaffected. If any `:hover`/`:focus`/`:visited` state re-introduces an underline
on anchors, reset `text-decoration: none` there too.

### Success Criteria:

#### Automated Verification:

- Lint passes: `cd web && npm run lint`
- Production build succeeds: `cd web && npm run build`

#### Manual Verification:

- On the join view (`joinLoggedOut` screen), "Log in to continue" is horizontally centered and has no underline.
- It is visually identical to the "Accept & join" primary button (same width, color, weight, centering).
- The "Go to your board" link on the `joinTaken` screen is also centered and un-underlined.
- No regression: existing native primary buttons (login/register submit, dialog actions) look unchanged.

**Implementation Note**: After automated verification passes, pause for human confirmation that
the manual checks passed before starting Phase 2.

---

## Phase 2: Match join-view Homdutio icons to the design (equal size + no stray shadow)

### Overview

Bring the join view's Homdutio house marks to the design: equalize the two flagged icons' size
and remove the stray shadow so they render flat. Exact target values are read from the design
during manual verification.

### Changes Required:

#### 1. Homdutio house marks — size parity

**File**: `web/src/app/auth/auth-logo.component.ts` (`.auth-logo-mark`, ~lines 41-50) and
`web/src/app/join/join.component.scss` (`.house-chip`, ~lines 37-48)

**Intent**: Make the two Homdutio brand/house icons the user flagged the same size as each
other, sized to match the design (currently 40px auth-logo mark vs 28px house-chip badge). The
implementer confirms which two icons and the exact target dimension by comparing the running
join view to the design before editing.

**Contract**: Adjust `width`/`height` (and the inner `<svg>` `width`/`height` proportionally,
`join.component.html:21` & `:54` for the chip, `auth-logo.component.ts:14` for the mark) so both
marks share one size. Keep sizes expressed in `rem` and, where a matching design token exists,
reuse it rather than hard-coding a new value. Preserve each element's shape and role
(`.auth-logo-mark` = rounded square via `--radius-sm`; `.house-chip` = circular overlay badge
positioned bottom-right of the avatar).

#### 2. Remove the stray shadow

**File**: whichever of `web/src/app/join/join.component.scss` (`.house-chip`),
`web/src/app/auth/auth-logo.component.ts` (`.auth-logo-mark`), or
`web/src/app/shared/user-avatar/user-avatar.component.scss` actually renders the shadow

**Intent**: The design shows these marks flat. Locate the source of the "minor shadow" at
runtime (browser devtools on the running join view) and remove it so the icons render without a
drop-shadow/halo.

**Contract**: No `box-shadow`/`filter` is declared on these elements in static SCSS, so the
source is either the `.house-chip` white `border: 2px solid var(--color-surface)` ring reading
as a halo, an inherited/computed style, or a `box-shadow` added elsewhere. Identify the actual
rule in devtools, then remove or neutralize it (`box-shadow: none` / drop the border) — matching
the design's flat rendering. Do not remove the border if the design keeps the white separator
ring; only remove what the design does not show.

### Success Criteria:

#### Automated Verification:

- Lint passes: `cd web && npm run lint`
- Production build succeeds: `cd web && npm run build`

#### Manual Verification:

- On the running join view, the two flagged Homdutio icons are the **same size** and match the design.
- Neither flagged icon shows a stray shadow/halo — they render flat as in the design.
- The inviter avatar and overall join-hero layout are unchanged apart from the icon adjustments.
- Side-by-side with the design, the join view matches ("copy it as it is").

**Implementation Note**: This phase starts by running the app (`cd web && npm start`), opening
an invite/join URL, and screenshotting the join view to compare against the design. Confirm the
interpretation in "Open Risks & Assumptions" against the design *before* editing; if the design
disagrees, adjust the target elements/values accordingly and note the correction.

---

## Testing Strategy

### Manual Testing Steps:

1. `cd web && npm start`, open a valid invite link → land on the join view (`joinLoggedOut` and `join` screens).
2. Confirm "Log in to continue" is centered, un-underlined, and matches "Accept & join".
3. Confirm the two Homdutio house icons are equal-sized and flat, matching the design.
4. Visit `joinTaken` (already-a-member invite) and confirm "Go to your board" is also centered/un-underlined.
5. Spot-check login/register submit buttons and a dialog for no button-styling regression.

## References

- Change identity: `context/changes/join-view-design-fixes/change.md`
- **Design source of truth (found during impl): `templates/Homdutio Auth Pro.html`** — the Auth Pro
  mockup. Its `renderVals()` defines the exact join-view specs used here:
  - `inviteAvatarsStyle: { display:flex, alignItems:center, marginBottom:18px }`
  - `inviterAvatarStyle: 52px circle, border:3px #fff, boxShadow:0 2px 8px rgba(28,35,48,.12)`
  - `houseChipStyle: 52px circle, accent bg, marginLeft:-14px, border:3px #fff, same shadow`
  - `primaryHover: { filter:brightness(1.08), transform:translateY(-1px) }`
- Prior join-flow work: `context/archive/2026-06-30-join-flow-and-user-settings/plan.md`
- Design tokens: `web/src/styles.scss` (`:root`, lines 13–88)
- Login link: `web/src/app/join/join.component.html:40`; `.btn`: `web/src/styles.scss:307-323`
- Avatar pair: `web/src/app/join/join.component.scss` (`.invite-avatars`, `.house-chip`)

## Open Risks & Assumptions

- **RESOLVED — design found:** The design source is `templates/Homdutio Auth Pro.html` (the Auth
  Pro mockup). All bug-2 values are read directly from its `renderVals()`, so the earlier
  "external design / unknown values" risks no longer apply.
  - The two icons are the **inviter avatar** and the **`.house-chip`** (not the auth-logo mark);
    both are **52px**, laid out as a **flex pair** with the chip pulled `-14px` onto the avatar.
  - The "shadow" is design-intended: `0 2px 8px rgba(28,35,48,.12)` on both discs (plus a 3px
    white ring). Copied as-is per "copy design as it is."
  - The button hover uses **`filter: brightness(1.08)` only**. The design's 1px lift
    (`transform: translateY(-1px)`) was **dropped per user preference** — distracting on the live view.
- **Global `.btn` blast radius:** Phase 1 (anchor centering) and the hover change (darken →
  `brightness(1.08)`) both edit the shared `.btn` primitive app-wide. Intended and consistent
  with the design; ghost/danger variants keep `transform: none` and ghost now pins `filter: none`.
  Regression spot-check on native buttons still required.
- **Env note (not part of this change):** Local DB needed the pending `AddUserAvatar` migration
  applied (`dotnet ef database update`) — all requests 500'd until then. Unrelated to the CSS.

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Center the "Log in to continue" primary button

#### Automated

- [x] 1.1 Lint passes: `cd web && npm run lint`
- [x] 1.2 Production build succeeds: `cd web && npm run build`

#### Manual

- [x] 1.3 "Log in to continue" is centered with no underline on the join view
- [x] 1.4 It matches the "Accept & join" primary button visually
- [x] 1.5 "Go to your board" (joinTaken) is also centered and un-underlined
- [x] 1.6 No regression on existing native primary buttons

### Phase 2: Match join-view Homdutio icons to the design

#### Automated

- [x] 2.1 Lint passes: `cd web && npm run lint`
- [x] 2.2 Production build succeeds: `cd web && npm run build`

#### Manual

- [x] 2.3 Inviter avatar + house chip are both 52px, matching the design
- [x] 2.4 Both discs show the design's white ring + soft shadow (0 2px 8px), no stray extra shadow
- [x] 2.5 Avatars sit as a flex pair overlapping -14px (not a corner-badge overlay)
- [x] 2.6 Join view matches `Homdutio Auth Pro.html` ("copy it as it is")
