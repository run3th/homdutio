# New Design Adoption + Task Tags + Emailed Invite Links — Implementation Plan

## Overview

Adopt the Claude-design mockups (`templates/Homdutio Pro.html`, `templates/Homdutio Auth Pro.html`) as a **structural redesign** of the Angular SPA — not a recolor. We overhaul the design-token layer and fonts, then rebuild the shell, board, cards, dialogs, members, and auth screens to match the mockup's markup and layout. Once the redesigned surfaces exist, we layer the two functional features — **task tags** and **emailed invite links** — directly onto them, so each control (the tag chip-input, the invite email field) is built once, in its final design.

This supersedes the prior token-only repalette plan: a partial teal token swap is already in the working tree (`web/src/styles.scss`) from the earlier attempt; Phase 1 absorbs and completes it.

## Current State Analysis

Grounded in `context/changes/redesign-tags-and-email-invites/research.md` (commit `194ace7`) and a full decode of the two mockup bundles' `__bundler/template` scripts (readable rendered markup + the `renderVals()` style logic).

- **Styling is a single token source.** `web/src/styles.scss` `:root` (lines 7–64) drives the whole app via CSS custom properties; every component references `var(--…)` with **no hard-coded hex** (verified). Global primitives `.page/.card/.field/.notice/.btn/.alt-link` at `styles.scss:87-191` back the auth/create/join pages. Font is self-hosted **Inter** via `@import '@fontsource-variable/inter'` (line 5). Plain SCSS + Angular CDK only — no Material/Tailwind. Per-component scoped SCSS; inline-SVG icons; **8 kB/component style budget** (`angular.json:48-50`); mobile NFR-2 (≤400px sidebar→bottom bar).
- **The mockups are a full structural redesign**, decoded from the bundle template scripts:
  - **Fonts**: IBM Plex Sans (body, weights 400/500/600/700) + IBM Plex Mono (dates, meta, counts, the invite link; weights 500/600).
  - **Shell**: a sticky **top header** (`headerStyle`) with a house-logo lockup ("Homdutio" + "Shared chores"), a **workspace pill** (household name + `ADMIN` badge), `Invite` (outline) + `＋ New task` (accent) buttons, and an avatar dropdown — over a body of a **176px text-label sidebar** (`sidebarStyle`, nav items with a colored dot, active state) + scrolling `main`. ≤1000px (`isMobile`) hides the sidebar and shows a fixed **bottom nav** with a center **FAB**. This replaces today's 4.75rem icon-rail sidebar.
  - **Board/cards**: `Task board` heading + subtitle; 3 columns (To do / In progress / Done) each with a colored dot, label, and count pill, plus an empty-state line; mobile shows a **column-tab switcher**. Cards (`cardStyle`) carry **multiple tag chips** (colored dot + uppercase label, max 3 shown then `+N`), a grip handle (`⣿`) + kebab menu, title, optional description, **creator/claimer avatars** with names, a mono timestamp, and a footer with a comment button + a status-driven primary action (Claim / Mark done / Confirm / Awaiting confirmation) + a "Confirmed by" chip.
  - **Dialogs** (mockup renders custom overlays; we keep the existing **CDK dialogs** and restyle their panels): add/edit task (Title, Description, **Tags chip field + suggestions dropdown**), comments, **invite (email field + Send, *or* share-a-link copy box)**, send-back (reason), delete-confirm.
  - **Members**: member cards with avatar, name (+ "you"), email, role badge, and admin-only Make-admin / Remove actions.
  - **Auth**: a centered card (logo lockup, left-aligned title + subtitle); login/register/forgot/reset with a password **show/hide** toggle, a **live password-rule checklist** (checkmarks update as you type), and a forgot **"Check your inbox"** success state.
- **Current Angular surfaces to rebuild** (from the file inventory): shell `shell/shell.component.*`, `shell/topbar/topbar.component.*` (+ invite generate/copy at `topbar.component.ts:58-83`), `shell/topbar/avatar-menu.component.*`, `shell/sidebar/sidebar.component.*`; board `board/board.component.*`, `board/task-column/*`, `board/task-card/*`, dialogs `board/{create-task,task-detail,comments,delete-confirm,send-back}/*`; members `household/members/{members,remove-member-confirm}.component.*`; auth `auth/{login,register,forgot-password,reset-password}/*` (+ `auth/password-policy.validator.ts` for the live checklist); plus `household/create-household/*` and `join/join.component.*` (ride on global primitives).
- **Tasks (for the tags feature)**: `HouseholdTask` has a nullable `Category` (`HouseholdTask.cs:24`) + `HouseholdId` FK (line 16); config `ApplicationDbContext.cs:70-89` (`Category` `.HasMaxLength(100)`, `HasIndex(HouseholdId)`). One-to-many house pattern is `TaskComment` (`ApplicationDbContext.cs:103-119`) with migration template `Migrations/20260611175249_AddTaskComments.cs`. Endpoints `src/Homdutio.Api/Tasks/TaskEndpoints.cs` (create 48-86, edit 302-338, board 25-45, DTOs 601-635). Scoping is JWT-derived in `HouseholdScope.ResolveCallerAsync` (`HouseholdScope.cs:25-41`); new scoped routes are enforced by `RouteIsolationCoverageTests`. Migration rules in `src/Homdutio.Data/MIGRATIONS.md` (additive-only, out-of-band, `--project`/`--startup-project`).
- **Email/invites (for the emailed-invite feature)**: `IEmailSender` has one method (`SendPasswordResetAsync`), config-gated ACS vs `NoOpEmailSender` (`Program.cs:100-109`); bodies are **inline strings** in `AcsEmailSender.BuildResetMessage` (55-80) — no template engine. Reset link built server-side from `AppBaseUrl` (`AuthEndpoints.cs:103-132`). Invite mints a token-only response (`HouseholdEndpoints.cs:82-109`); link built client-side `/join/<token>` (`invite.service.ts:23-25`). Capturing-fake `IEmailSender` test harness via `tests/Homdutio.Api.Tests/AuthApiFactory.cs`. The hardened HTML email bodies already exist in `templates/Homdutio Email Preview.html` (reset `{{reset_link}}`; invite `{{household_name}}`, `{{inviter_name}}`, `{{invite_link}}`).

## Desired End State

- The SPA visually matches the mockups: IBM Plex typography, teal palette, the top-header + text-sidebar shell, restructured board/cards, restyled dialogs, redesigned members and auth screens — with CDK drag-drop and ≤400px responsive behavior intact.
- A task carries **multiple free-text tags** with deterministic per-tag chip colors; create/edit offer a chip input with type-to-filter household suggestions (incl. a "Create '<x>'" row and per-tag task counts); the old single `Category` is gone (values preserved as initial tags).
- A household member can **optionally email an invite** from the redesigned invite dialog; the copy-link path still works; the emailed link is server-built `/join/<token>`; the password-reset email renders from the same templating mechanism.

### Key Discoveries

- The mockup is fully decoded — exact tokens, fonts, radii, shadows, and component metrics are known (see **Critical Implementation Details → Design spec**) — so the rebuild is a faithful translation of mockup inline-styles into scoped SCSS + tokens, not a guess.
- The design **bakes in** the tag chip-input (in the add/edit modal) and the invite email field (in the invite modal); building those as features against today's components would be thrown away — hence design-first, features-layered.
- Tags is a **generalization of `Category`** — reuse its plumbing (`HouseholdTask.cs:24` → `ApplicationDbContext.cs:73` → `TaskEndpoints.cs` DTOs → `task.service.ts:14` → modal field); the `TaskComment` one-to-many + `AddTaskComments` migration are the verbatim template.
- `IEmailSender` is a deliberately narrow one-method seam; widening it + adding `{{token}}` templating is the bulk of the invites phase.
- Any new household-scoped route must use `HouseholdScope.ResolveCallerAsync` and be registered in the isolation sweep or `RouteIsolationCoverageTests` fails.

## What We're NOT Doing

- No backend/data-model changes in the design phases (1–5) — they are pure Angular presentation; `TaskResponse` affordance flags, lifecycle actions, comments, and 4s polling are wired as-is.
- No change to the **CDK dialog / CDK drag-drop** mechanisms — we restyle panels and restructure card markup, not replace the interaction primitives. (The mockup's native HTML5 drag and custom overlays are visual references only.)
- No dark theme (tokens stay light-only; the custom-property structure keeps a future `data-theme` open).
- No normalized `Tag` registry / tag rename / management screen — tags are free-text child rows.
- No pending-invite roster, invite revoke, or resend UI; the invite email is added inline to the existing invite affordance.
- No background email queue — sends stay synchronous in-request.
- No new email provider or DI swap — keep ACS + the `NoOpEmailSender` config gate.
- No raising of the 8 kB component-style budget — shared rules go into global primitives/tokens to stay within it.

## Implementation Approach

Five design-adoption phases (foundation → shell → board/cards → dialogs/members → auth) land the new look against today's functionality, each independently shippable and visually verifiable. Then two feature phases build tags and emailed invites onto the already-redesigned surfaces, following the existing minimal-API + EF + `HouseholdScope` backend patterns and the standalone-component + reactive-forms + signals Angular patterns. Each phase ends by updating the test-plan §6 cookbook where it adds a new test category.

## Critical Implementation Details

### Design spec (canonical values, extracted from the decoded mockup bundles)

These are load-bearing — the implementer translates them into `styles.scss` tokens + scoped SCSS. They are not derivable from the file paths.

- **Palette**: accent `#2C6E63` (hover via `brightness(1.08)`; darker `#235049` for solid hover); ink `#1C2330`; muted `#7A828F`; faint `#A2A9B5`; secondary text `#3C4554` / `#5A6273`; subtle `#8A92A0` / `#9AA1AD`. Surfaces: app bg `#F4F6F8`, white `#FFFFFF`, inset/ghost `#F1F3F6`, input bg `#FBFBFC`, link/quote bg `#F7F8FA`, outline-hover `#F6F7F9`. Borders: primary `#E7EAEF`, input/strong `#E2E5EB`, hairlines `#EAECF0`/`#EDEFF3`/`#EEF0F4`. Status: todo `#B5852F`, in-progress `#2F6B8F`, done `#3A7D52`; confirmed chip bg `#E4EFDC` / text `#3A7D52`; danger `#C1462F` with soft `#FBEEEB` (hover `#F6DED8`); admin badge bg `#E4E7EC` / text `#5A6273`; count-pill text `#6B7280`.
- **Radius**: inputs/buttons 10px, cards 12px, columns 14px, modals 16px, auth card 18px, chips 6–7px, menus/small 8–11px, count pills 20px, avatars 50%.
- **Shadows** (all `rgba(28,35,48,a)`): card hover `0 4px 14px /.07`; dropdown menu `0 12px 32px /.16`; modal `0 20px 56px /.24`; accent button `0 1px 2px /.08`; auth card `0 12px 40px /.08`. FAB `0 6px 16px rgba(44,110,99,.4)`.
- **Fonts**: `'IBM Plex Sans'` body; `'IBM Plex Mono'` for timestamps, card meta, count metas, and the invite link box. Self-host both via `@fontsource` (mirror the current Inter setup).
- **Shell metrics**: header height ≈ 61px (sidebar sticky `top:61px`, `height:calc(100vh-61px)`); sidebar width 176px; `isMobile` breakpoint `< 1000px` for the header/sidebar→bottom-nav switch — **but the existing NFR-2 ≤400px contract must still hold** (reconcile: treat the mockup's 1000px as the desktop-sidebar threshold while keeping the ≤400px guarantees).
- **Tag colors** (deterministic, client-side; Phase 6, also used for the single-category chip in Phase 3): `key = name.trim().toLowerCase()`; known map `{kitchen:#C2703D, cleaning:#2F6B8F, garden:#3A7D52, pets:#7A5AA6, shopping:#B5852F, laundry:#5B5FA6}`; else hash `h=0; for c in key: h=(h*31+code)>>>0` and pick from palette `[#2F6B8F,#3A7D52,#C2703D,#7A5AA6,#B5852F,#5B5FA6,#2C6E63,#B5524A,#4F6B8F,#3E7C8A]` at `h % 10`. Cards show up to 3 chips then `+N`.

### Cross-cutting preservation constraints

- **Drag-drop**: keep the board's CDK drag-drop (within-column reorder; cross-column = lifecycle transition). Card restructure must keep the drag initiator working and not regress touch behavior.
- **Responsive**: the ≤400px sidebar→bottom-bar guarantee (NFR-2) and no-horizontal-scroll must survive the shell rebuild.
- **Additive migration only** (Phase 6): create `TaskTags` + backfill from `Category` in one migration; **do not drop `Category`** this deploy (backward-compatible, no-rollback Azure SQL B1). Apply out-of-band per `MIGRATIONS.md`.
- **Cross-household isolation** (Phase 6): the tag-suggestions route resolves the household via `ResolveCallerAsync` (never client-supplied) and is registered in the isolation sweep, or `RouteIsolationCoverageTests` fails. Suggestions must include **closed** tasks — do not reuse the board's `ClosedAtUtc == null` filter.
- **Reset-email regression** (Phase 7): migrating the reset body onto the renderer must keep the existing reset-email tests green and the link HTML-encoded.

---

## Phase 1: Design Foundation — tokens, fonts, global primitives

### Overview

Establish the new design language in one place: overhaul the `:root` token block, self-host IBM Plex, and restyle the shared global primitives so every component (and the globals-based create-household / join / auth pages) inherits the new look.

### Changes Required:

#### 1. Self-host IBM Plex fonts

**File**: `web/src/styles.scss`, `web/package.json`

**Intent**: Replace Inter with IBM Plex Sans (body) and add IBM Plex Mono (meta/dates/link), keeping the offline, same-origin `@fontsource` approach.

**Contract**: Add `@fontsource-variable/ibm-plex-sans` (or the weighted `@fontsource/ibm-plex-sans` 400/500/600/700) and `@fontsource/ibm-plex-mono` (500/600) as deps; swap the `@import` at `styles.scss:5`; set `--font-sans` to IBM Plex Sans and add a `--font-mono` token. Remove the Inter import/dep.

#### 2. Token overhaul

**File**: `web/src/styles.scss`

**Intent**: Replace the violet-era token values with the mockup's palette, radii, and shadows (see Design spec), keeping variable **names** stable so components inherit without edits.

**Contract**: Update the `:root` block (7–64): primary family → teal; surfaces/neutrals → mockup blue-greys; border tokens; status/semantic pairs to the mockup's; radius scale (add column/modal/auth radii or reuse `--radius-*`); shadows to `rgba(28,35,48,…)`; add `--font-mono`. Absorb the partial teal swap already in the working tree. Keep names referenced by components (`--color-primary`, `--color-primary-soft`, `--focus-ring`, `--shell-*`, etc.).

#### 3. Global primitives restyle

**File**: `web/src/styles.scss`

**Intent**: Restyle the shared scaffolding (`.page/.card/.field/.notice/.btn/button[type=submit]/.alt-link/.rules/.form-error`) to the mockup's card/input/button treatment so auth/create/join pick it up centrally.

**Contract**: Update `87-191` for radius (inputs/buttons 10px, card 18px on auth), input bg `#FBFBFC` + 1.5px border + focus to accent, accent button with `brightness` hover, spacing per mockup. No markup/structural changes.

### Success Criteria:

#### Automated Verification:

- Angular build succeeds: `cd web && npm run build`
- Lint passes: `cd web && npm run lint`
- Unit tests pass: `cd web && npm test`
- No component style exceeds the 8 kB budget (no build budget warning/error)

#### Manual Verification:

- App renders in IBM Plex with the teal palette; no leftover violet anywhere
- Buttons, inputs, and cards on auth/create-household/join match the mockup's treatment
- Focus rings and hover states are visible and on-brand

**Implementation Note**: After automated verification passes, pause for human confirmation of the visual review before Phase 2.

---

## Phase 2: Shell — top header, text-label sidebar, mobile nav

### Overview

Rebuild the shell chrome to the mockup: a sticky top header with the logo lockup, workspace pill + ADMIN badge, action buttons, and avatar dropdown; a 176px text-label sidebar with dots; and a mobile bottom nav with a center FAB. Preserve the ≤400px contract.

### Changes Required:

#### 1. Top header (topbar)

**File**: `web/src/app/shell/topbar/topbar.component.{ts,html,scss}`

**Intent**: Replace the current topbar with the mockup header: house-logo SVG + "Homdutio"/"Shared chores" lockup, a workspace pill showing the household name + `ADMIN` role badge, and right-side `Invite` (outline) + `＋ New task` (accent) buttons plus the avatar menu.

**Contract**: Sticky header (`position:sticky; top:0`), translucent white + blur, `1px` bottom border, height ≈ 61px. Keep existing data sources (household name, current role, the actions that open the create-task dialog and the invite affordance at `topbar.component.ts:58-83`). Workspace pill + subtitle hidden on mobile per `isMobile`.

#### 2. Avatar dropdown

**File**: `web/src/app/shell/topbar/avatar-menu.component.{ts,html,scss}`

**Intent**: Restyle the avatar menu to the mockup dropdown (avatar trigger → menu with name/email header, divider, Log out).

**Contract**: Token-styled dropdown (`userMenuStyle`), avatar circle in accent; preserve existing open/close + logout behavior.

#### 3. Sidebar → text-label rail + mobile bottom nav

**File**: `web/src/app/shell/sidebar/sidebar.component.{ts,html,scss}`, `web/src/app/shell/shell.component.{ts,html,scss}`

**Intent**: Replace the 4.75rem icon rail with the 176px text-label sidebar (nav item = colored dot + label, active background) on desktop, and a fixed bottom nav with a center FAB on mobile.

**Contract**: Desktop sidebar `width:176px`, sticky under the header. Mobile (≤400px per NFR-2, reconciled with the mockup's `<1000px`): sidebar hidden, fixed bottom nav (Board / FAB / Members) with matching body bottom padding so the last card isn't occluded. Preserve existing routerLink targets and active state.

### Success Criteria:

#### Automated Verification:

- Build, lint, unit tests pass: `cd web && npm run build && npm run lint && npm test`
- No component style exceeds the 8 kB budget

#### Manual Verification:

- Header shows logo lockup, workspace pill + ADMIN badge, Invite / ＋New task, and avatar menu; matches the mockup
- Sidebar shows text labels with dots and an active state; navigates Board/Members
- At ≤400px the chrome collapses to the bottom nav with FAB; no horizontal scroll
- Avatar dropdown opens/closes and logs out

**Implementation Note**: Pause for human visual confirmation before Phase 3.

---

## Phase 3: Board & Cards

### Overview

Restructure the board header, columns, and task cards to the mockup, preserving CDK drag-drop and the lifecycle actions. Tags render as a single colored chip from today's `Category` (multi-tag arrives in Phase 6).

### Changes Required:

#### 1. Board header + columns

**File**: `web/src/app/board/board.component.{ts,html,scss}`, `web/src/app/board/task-column/task-column.component.{ts,html,scss}`

**Intent**: Add the "Task board" heading + subtitle; restyle each column with a colored status dot, uppercase label, count pill, and empty-state line; add the mobile column-tab switcher.

**Contract**: 3-column grid on desktop (`repeat(3, minmax(0,1fr))`, gap 16px), single column + tab switcher on mobile. Column accent dots: todo `#B5852F`, progress `#2F6B8F`, done `#3A7D52`. Preserve the CDK drop-list wiring and 4s polling.

#### 2. Task card restructure

**File**: `web/src/app/board/task-card/task-card.component.{ts,html,scss}`

**Intent**: Rebuild the card to the mockup: tag chip row (colored dot + uppercase label) at top, grip handle + kebab cluster top-right, title, optional description, creator/claimer avatar rows, mono timestamp, and a footer with the comment button + status-driven primary action + "Confirmed by" chip.

**Contract**: Render the existing single `category` as one colored chip (deterministic color helper introduced here, reused by Phase 6). Map the existing `TaskResponse` affordance flags to the primary action (Claim / Mark done / Confirm / Awaiting confirmation) and the Confirmed chip; keep kebab items (Edit / Unclaim / Send back / Delete) gated as today. **Preserve the CDK drag initiator** and touch behavior. Avatars are initial-in-circle using a per-member color.

### Success Criteria:

#### Automated Verification:

- Build, lint, unit tests pass: `cd web && npm run build && npm run lint && npm test`
- No component style exceeds the 8 kB budget

#### Manual Verification:

- Board + columns match the mockup (dots, count pills, empty states); mobile tab switcher works
- Cards show chip, avatars, mono date, and the correct status-driven primary action
- Drag-reorder still works (and cross-column lifecycle moves still work)
- Kebab actions (edit/unclaim/send-back/delete) behave as before

**Implementation Note**: Pause for human visual confirmation before Phase 4.

---

## Phase 4: Dialogs & Members

### Overview

Restyle all five CDK dialogs and the members page to the mockup, keeping current behavior. The invite dialog keeps copy-link only (email field arrives in Phase 7); the create/edit dialog keeps a single styled Category field (chip input arrives in Phase 6).

### Changes Required:

#### 1. Task dialogs (create/edit, comments, send-back, delete-confirm)

**File**: `web/src/app/board/create-task/*`, `web/src/app/board/task-detail/*`, `web/src/app/board/comments/*`, `web/src/app/board/send-back/*`, `web/src/app/board/delete-confirm/*`

**Intent**: Restyle the CDK dialog panels to the mockup modal treatment (16px radius, header with title/subtitle + close button, token inputs/textareas, accent/ghost footer buttons), keeping each dialog's current fields and behavior.

**Contract**: Panel = `modalStyle`; inputs/labels/buttons per Design spec. Create/edit keeps the single Category field (styled). Comments keeps the list + composer; send-back keeps the reason textarea; delete-confirm keeps the confirm/cancel. No behavior change.

#### 2. Invite dialog (copy-link)

**File**: `web/src/app/shell/topbar/topbar.component.*` (and/or a dedicated invite dialog), `web/src/app/household/invite.service.ts`

**Intent**: Present the invite affordance as the mockup's invite modal with the "Or share a link" copy box (the email field is deferred to Phase 7).

**Contract**: Modal header + a mono link box + Copy button; reuse the existing client-side `/join/<token>` generate+copy. Leave room (markup/layout) for the Phase 7 email field above the link box.

#### 3. Members page

**File**: `web/src/app/household/members/members.component.*`, `web/src/app/household/members/remove-member-confirm.component.*`

**Intent**: Restyle the members list to the mockup (member cards with avatar, name + "you", email, role badge, admin-only Make-admin / Remove).

**Contract**: Member card = `memberCardStyle`; role badge accent-tinted for Admin; preserve existing make-admin/remove actions and the last-admin guard behavior. Restyle the remove-confirm dialog to the modal treatment.

### Success Criteria:

#### Automated Verification:

- Build, lint, unit tests pass: `cd web && npm run build && npm run lint && npm test`
- No component style exceeds the 8 kB budget

#### Manual Verification:

- All five dialogs match the mockup; their existing actions still work
- Invite dialog shows the link box + Copy and copies the working `/join/<token>` link
- Members page matches the mockup; make-admin/remove still work (last-admin guard intact)

**Implementation Note**: Pause for human visual confirmation before Phase 5.

---

## Phase 5: Auth Screens

### Overview

Adopt the auth card design across login/register/forgot/reset, adding the password show/hide toggle, the live password-rule checklist, and the forgot "Check your inbox" success state.

### Changes Required:

#### 1. Auth layout + logo lockup

**File**: `web/src/app/auth/login/*`, `web/src/app/auth/register/*`, `web/src/app/auth/forgot-password/*`, `web/src/app/auth/reset-password/*`

**Intent**: Apply the centered card with logo lockup, left-aligned title + subtitle, and the mockup's field/button styling (largely inherited from Phase 1 globals, with per-screen copy).

**Contract**: Each screen uses the redesigned `.page/.card/.field/.btn` plus a shared logo-lockup header; titles/subtitles per mockup ("Welcome back", "Create your account", "Reset password").

#### 2. Password show/hide toggle

**File**: `web/src/app/auth/login/*`, `web/src/app/auth/register/*`, `web/src/app/auth/reset-password/*`

**Intent**: Add an inline Show/Hide button toggling the password input type.

**Contract**: Right-aligned text toggle inside the password field wrapper; toggles `type` between `password`/`text`; accent-colored. No change to form submission.

#### 3. Live password-rule checklist

**File**: `web/src/app/auth/register/*` (and reset-password), `web/src/app/auth/password-policy.validator.ts`

**Intent**: Show the four password rules with checkmarks that update live as the user types, mirroring the server policy.

**Contract**: Drive the checklist off the existing `password-policy.validator.ts` rules (≥6 chars, upper+lower, digit, non-alphanumeric); each rule renders a check/empty mark per `rules` in the mockup. Purely presentational; submission still validated as today.

#### 4. Forgot "check your inbox" success state

**File**: `web/src/app/auth/forgot-password/*`

**Intent**: After submit, show the centered check-circle success panel with the sent-to email.

**Contract**: Toggle to the success view on the existing generic-200 response (preserve anti-enumeration — show success regardless); "Back to log in" returns to login.

### Success Criteria:

#### Automated Verification:

- Build, lint, unit tests pass: `cd web && npm run build && npm run lint && npm test`
- No component style exceeds the 8 kB budget

#### Manual Verification:

- Login/register/forgot/reset match the mockup card design
- Show/hide toggles password visibility on all password fields
- Register's rule checklist updates live as you type and matches the enforced policy
- Forgot shows the "Check your inbox" state after submit; login still works end-to-end

**Implementation Note**: Pause for human visual confirmation before Phase 6.

---

## Phase 6: Task Tags (backend + chip input into the redesigned modal)

### Overview

Introduce a `TaskTag` child table (backfilled from `Category`), expose tags through the DTOs and a household-scoped suggestions endpoint, retire `Category`, and replace the redesigned modal's single Category field with the mockup's chip/typeahead control; cards render multiple colored tag chips.

### Changes Required:

#### 1. TaskTag entity + DbContext config

**File**: `src/Homdutio.Data/Entities/TaskTag.cs` (new), `src/Homdutio.Data/Entities/HouseholdTask.cs`, `src/Homdutio.Data/ApplicationDbContext.cs`

**Intent**: Add a one-to-many tag child modeled on `TaskComment`, with `HouseholdId` denormalized so suggestions query without a join.

**Contract**: `TaskTag { Guid Id; Guid TaskId; Guid HouseholdId; string Value }`; nav `ICollection<TaskTag> Tags` on `HouseholdTask`. DbSet + `OnModelCreating` mirroring `TaskComment` (`ApplicationDbContext.cs:103-119`): FK `TaskId`→`HouseholdTask` cascade, `Value` max length 50, index on `TaskId`, index on `(HouseholdId, Value)`. `Category` config stays (column not yet dropped).

#### 2. Additive migration with Category backfill

**File**: `src/Homdutio.Data/Migrations/<timestamp>_AddTaskTags.cs` (generated)

**Intent**: Create `TaskTags` and seed one tag per existing non-blank `Category`, preserving prior categorization.

**Contract**: `dotnet ef migrations add AddTaskTags --project src/Homdutio.Data --startup-project src/Homdutio.Api`. Hand-add an idempotent backfill `INSERT … SELECT` (trimmed `Category`, copying `HouseholdId`) in `Up`. **Do not drop `Category`.** Verify the snapshot updates.

#### 3. Tag normalization helper

**File**: `src/Homdutio.Api/Tasks/TagNormalization.cs` (new)

**Intent**: Centralize tag rules for create/update and tests.

**Contract**: Pure function: trim, collapse internal whitespace, drop blanks, case-insensitive dedup preserving first-seen casing, enforce ≤50 chars/tag and ≤10 tags/task (reject over-limit with a 400 matching the existing `mapValidationProblem` shape).

#### 4. Task endpoints + DTOs (string Category → string[] Tags)

**File**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs`

**Intent**: Accept/return tags on create/edit/board, replacing `Category`, applying normalization and rewriting tag rows on update.

**Contract**: `CreateTaskRequest`/`UpdateTaskRequest` gain `string[] Tags` (drop `Category`); `TaskResponse` gains `string[] Tags` (drop `Category`). Create (48-86) and edit (302-338) normalize then insert/replace `TaskTag` rows scoped to the task; board `GET /` (25-45) projects tags. All paths keep `HouseholdScope`.

#### 5. Per-household tag-suggestions endpoint

**File**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs` (+ isolation-sweep registration)

**Intent**: Return distinct household tag values (incl. closed tasks) for autocomplete.

**Contract**: `GET /api/tasks/tags` `.RequireAuthorization()`; resolve `caller.HouseholdId` via `ResolveCallerAsync`; `SELECT DISTINCT Value WHERE HouseholdId == caller.HouseholdId` ordered alphabetically → `string[]`. **No `ClosedAtUtc == null` filter.** Register in the cross-household isolation sweep.

#### 6. Angular task model + service

**File**: `web/src/app/board/task.service.ts`

**Intent**: Mirror the new DTO shape and add the suggestions fetch.

**Contract**: `Task.category` → `tags: string[]`; request types `category?` → `tags: string[]`; add `getTagSuggestions(): Observable<string[]>` hitting the new endpoint, cached per board load.

#### 7. Tag chip/typeahead component

**File**: `web/src/app/board/tag-input/` (new standalone component)

**Intent**: Build the mockup's chip input with a filtered suggestion dropdown, since no Material/typeahead exists.

**Contract**: Standalone component implementing `ControlValueAccessor` (or driven by a `FormControl<string[]>`); Enter/comma commits a chip, Backspace removes the last; chips render with a deterministic color dot (the Phase 3 helper) + remove button; filters the passed-in suggestions case-insensitively, shows per-tag task counts and a "Create '<x>'" row when no exact match; enforces ≤10 tags / ≤50 chars client-side. Reactive-forms + signals + scoped SCSS, token-styled, within budget.

#### 8. Wire chip input into the redesigned create/edit modal + cards

**File**: `web/src/app/board/create-task/* `, `web/src/app/board/task-detail/*`, `web/src/app/board/task-card/*`

**Intent**: Replace the styled single Category field with `<app-tag-input>` fed by household suggestions, and render multiple tag chips on cards.

**Contract**: Swap the `category` control for a `tags: string[]` control bound to `<app-tag-input>` in both modals; load suggestions when the dialog opens. Card chip row renders up to 3 tags + `+N` (Phase 3 single-chip becomes the multi-tag render).

#### 9. Test-plan cookbook update

**File**: `context/foundation/test-plan.md`

**Intent**: Record how to add a tag/endpoint test in this project.

**Contract**: Fill the relevant §6 entry (location, naming, reference test, run command) for the tags test category.

### Success Criteria:

#### Automated Verification:

- Migration applies cleanly against a fresh DB: `dotnet ef database update --project src/Homdutio.Data --startup-project src/Homdutio.Api`
- Backend builds: `dotnet build`
- Backend tests pass incl. new tag tests: `dotnet test`
- `RouteIsolationCoverageTests` passes (suggestions route covered)
- Angular build + lint + unit tests pass: `cd web && npm run build && npm run lint && npm test`

#### Manual Verification:

- Type + Enter adds a chip; household tags suggested and filter as you type; "Create '<x>'" appears for new tags
- Tags persist on create and edit; removing a chip + save removes the tag
- Suggestions include a tag from a completed/closed task
- Over-limit input (11th tag, 51-char tag) is rejected gracefully with a clear message
- Existing prod-style `Category` values appear as initial tags after migration (seeded DB)

**Implementation Note**: Pause for human confirmation before Phase 7.

---

## Phase 7: Emailed Invite Links + reusable email templating

### Overview

Add an HTML email-templating mechanism, migrate the reset email onto it, widen `IEmailSender` for invites, build the invite link server-side, and add the optional email field to the redesigned invite dialog.

### Changes Required:

#### 1. Embed the HTML email templates

**File**: `src/Homdutio.Api/Email/Templates/reset-password.html`, `invite-member.html` (extracted from `templates/Homdutio Email Preview.html`), `src/Homdutio.Api/Homdutio.Api.csproj`

**Intent**: Make the hardened HTML bodies loadable at runtime, versioned with the assembly.

**Contract**: Extract the two `srcdoc` bodies into standalone `.html` files keeping their `{{snake_case}}` placeholders; add them as `<EmbeddedResource>` in the csproj.

#### 2. Template loader + substitution

**File**: `src/Homdutio.Api/Email/EmailTemplateRenderer.cs` (new)

**Intent**: Load an embedded template by name and substitute `{{token}}` values, HTML-encoding interpolated values.

**Contract**: `string Render(string templateName, IReadOnlyDictionary<string,string> values)`; reads the embedded resource (cached), replaces each `{{key}}`, HTML-encodes values (preserving reset link-encoding). Registered in DI.

#### 3. Widen IEmailSender + both implementations

**File**: `src/Homdutio.Api/Email/IEmailSender.cs`, `AcsEmailSender.cs`, `NoOpEmailSender.cs`

**Intent**: Add an invite-send method and move both bodies onto the renderer.

**Contract**: Add `Task<bool> SendInviteAsync(string recipientEmail, string inviteLink, string householdName, string inviterName, CancellationToken ct = default)`. `AcsEmailSender` builds both bodies via `EmailTemplateRenderer` (reset migrated off `BuildResetMessage`). `NoOpEmailSender` logs recipient + link for both. Keep the bool-return / swallow-failure contract.

#### 4. Server-side invite link + email-on-invite

**File**: `src/Homdutio.Api/Households/HouseholdEndpoints.cs`

**Intent**: Optionally accept a recipient email on invite creation and, when present, build the absolute `/join/<token>` link from `AppBaseUrl` and send the invite email with the inviter's display name.

**Contract**: Extend the invite request with optional `RecipientEmail`; reuse the `AppBaseUrl` pattern to build `{baseUrl}/join/{token}`; look up caller `DisplayName` for `{{inviter_name}}` and the household name for `{{household_name}}`; call `SendInviteAsync` only when an email was provided. Response still returns the token. Keep rate-limiting consistent with other email endpoints.

#### 5. Angular invite UI — optional email field

**File**: `web/src/app/shell/topbar/topbar.component.*` (or invite dialog), `web/src/app/household/invite.service.ts`

**Intent**: Add the mockup's "Invite by email" field + Send above the share-link box, keeping copy-link intact.

**Contract**: Add the email input + Send to the redesigned invite dialog (the slot left in Phase 4); `invite.service.ts` `generate(...)` passes optional `recipientEmail`; show a sent confirmation (toast) when an email was provided. `buildJoinUrl` stays for the copy display. No new public endpoint (invite creation is already authorized), so no interceptor-allowlist change.

#### 6. Test-plan cookbook update

**File**: `context/foundation/test-plan.md`

**Intent**: Record how to add an email-rendering / invite-email test.

**Contract**: Fill the relevant §6 entry (location, naming, reference test via the capturing-fake `IEmailSender`, run command).

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build`
- Reset-email tests still pass, now asserting the template-rendered body/link
- New invite-email test passes (capturing-fake asserts link + household/inviter substitution)
- Renderer unit test loads both embedded templates
- Angular build + lint + unit tests pass: `cd web && npm run build && npm run lint && npm test`

#### Manual Verification:

- Inviting without an email behaves exactly as today (generate + copy link)
- Inviting with an email sends an invite mail (dev `NoOpEmailSender` log) with a working `/join/<token>` link, household name, and inviter name
- The emailed link opens the join landing and accepts the invite
- The password-reset email still arrives and its link still works (no regression)

**Implementation Note**: After automated verification passes, pause for human confirmation of the manual testing.

---

## Testing Strategy

### Unit Tests:

- `TagNormalization` — trim, whitespace-collapse, blank-drop, case-insensitive dedup, casing preservation, length/count caps.
- `EmailTemplateRenderer` — loads embedded templates; substitutes all placeholders; HTML-encodes values.
- Angular: `tag-input` chip add/remove/filter/caps; the deterministic tag-color helper; the live password-rule checklist.

### Integration Tests:

- Tag create/edit/board round-trip via `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs` patterns.
- Tag-suggestions endpoint: distinct, household-scoped, includes closed-task tags; covered by `RouteIsolationCoverageTests`.
- Reset email (regression) and invite email via the capturing-fake `IEmailSender` from `AuthApiFactory`.
- Migration backfill: seed `Category` values, apply migration, assert one `TaskTag` per value.

### Manual Testing Steps:

1. Visual sweep of every surface (auth, shell, board, dialogs, members) in the new design at desktop + ≤400px; confirm drag-drop intact and no horizontal scroll.
2. Add/remove tags with suggestions + "Create"; confirm caps and closed-task suggestions; confirm migrated Category values appear as tags.
3. Invite with and without email; verify copy-link unchanged and emailed link works.
4. Trigger a password reset; confirm the email still renders and links correctly.

## Performance Considerations

- Tag suggestions: a single indexed DISTINCT query per board load (`(HouseholdId, Value)` index); negligible at MVP scale.
- Email sends stay synchronous in-request; template rendering is in-memory with cached embedded resources.
- Font payload grows (two IBM Plex families); self-hosted + `font-display: swap`. Watch the 8 kB per-component style budget across the rebuilt shell/card/dialog SCSS — push shared rules to global primitives.

## Migration Notes

- `AddTaskTags` is additive: new table + backfill, **`Category` retained** this deploy. A later follow-up migration may drop `Category` once unreferenced. Apply manually out-of-band per `MIGRATIONS.md` (`--project`/`--startup-project`, `dotnet tool restore` first).

## References

- Research: `context/changes/redesign-tags-and-email-invites/research.md`
- Decoded mockups: `templates/Homdutio Pro.html`, `templates/Homdutio Auth Pro.html` (bundle `__bundler/template` scripts hold the readable markup + `renderVals()` styles)
- One-to-many pattern: `src/Homdutio.Data/ApplicationDbContext.cs:103-119`; migration template `src/Homdutio.Data/Migrations/20260611175249_AddTaskComments.cs`
- Household scoping: `src/Homdutio.Api/HouseholdScope.cs:25-41`
- Reset email pattern: `src/Homdutio.Api/Auth/AuthEndpoints.cs:103-132`; sender `src/Homdutio.Api/Email/AcsEmailSender.cs:55-80`
- Token source: `web/src/styles.scss:7-64`
- Prior art: `context/archive/2026-06-08-ui-redesign/`, `context/archive/2026-06-25-password-reset/`, `context/archive/2026-06-02-invite-and-multiplayer-board/`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Design Foundation — tokens, fonts, global primitives

#### Automated

- [x] 1.1 Angular build succeeds (`cd web && npm run build`)
- [x] 1.2 Lint passes (`cd web && npm run lint`)
- [x] 1.3 Unit tests pass (`cd web && npm test`)
- [x] 1.4 No component style exceeds the 8 kB budget

#### Manual

- [ ] 1.5 App renders in IBM Plex + teal palette with no leftover violet
- [ ] 1.6 Auth/create/join buttons, inputs, cards match the mockup treatment
- [ ] 1.7 Focus rings and hover states visible and on-brand

### Phase 2: Shell — top header, text-label sidebar, mobile nav

#### Automated

- [ ] 2.1 Build, lint, unit tests pass
- [ ] 2.2 No component style exceeds the 8 kB budget

#### Manual

- [ ] 2.3 Header shows logo lockup, workspace pill + ADMIN badge, Invite / ＋New task, avatar menu
- [ ] 2.4 Sidebar shows text labels with dots + active state; navigates Board/Members
- [ ] 2.5 ≤400px collapses to bottom nav with FAB; no horizontal scroll
- [ ] 2.6 Avatar dropdown opens/closes and logs out

### Phase 3: Board & Cards

#### Automated

- [ ] 3.1 Build, lint, unit tests pass
- [ ] 3.2 No component style exceeds the 8 kB budget

#### Manual

- [ ] 3.3 Board + columns match the mockup; mobile tab switcher works
- [ ] 3.4 Cards show chip, avatars, mono date, correct status-driven primary action
- [ ] 3.5 Drag-reorder and cross-column lifecycle moves still work
- [ ] 3.6 Kebab actions (edit/unclaim/send-back/delete) behave as before

### Phase 4: Dialogs & Members

#### Automated

- [ ] 4.1 Build, lint, unit tests pass
- [ ] 4.2 No component style exceeds the 8 kB budget

#### Manual

- [ ] 4.3 All five dialogs match the mockup; their actions still work
- [ ] 4.4 Invite dialog shows the link box + Copy and copies a working `/join/<token>` link
- [ ] 4.5 Members page matches the mockup; make-admin/remove still work (last-admin guard intact)

### Phase 5: Auth Screens

#### Automated

- [ ] 5.1 Build, lint, unit tests pass
- [ ] 5.2 No component style exceeds the 8 kB budget

#### Manual

- [ ] 5.3 Login/register/forgot/reset match the mockup card design
- [ ] 5.4 Show/hide toggles password visibility on all password fields
- [ ] 5.5 Register's rule checklist updates live and matches the enforced policy
- [ ] 5.6 Forgot shows "Check your inbox" after submit; login works end-to-end

### Phase 6: Task Tags (backend + chip input into the redesigned modal)

#### Automated

- [ ] 6.1 Migration applies cleanly against a fresh DB
- [ ] 6.2 Backend builds (`dotnet build`)
- [ ] 6.3 Backend tests pass incl. new tag tests (`dotnet test`)
- [ ] 6.4 `RouteIsolationCoverageTests` passes (suggestions route covered)
- [ ] 6.5 Angular build + lint + unit tests pass
- [ ] 6.6 Test-plan §6 cookbook updated for the tags test category

#### Manual

- [ ] 6.7 Type + Enter adds a chip; household tags suggested and filter; "Create '<x>'" appears for new tags
- [ ] 6.8 Tags persist on create and edit; removing a chip + save removes the tag
- [ ] 6.9 Suggestions include a tag from a closed task
- [ ] 6.10 Over-limit input rejected gracefully with a clear message
- [ ] 6.11 Existing `Category` values appear as initial tags after migration

### Phase 7: Emailed Invite Links + reusable email templating

#### Automated

- [ ] 7.1 Backend builds (`dotnet build`)
- [ ] 7.2 Reset-email tests still pass, now asserting template-rendered body/link
- [ ] 7.3 New invite-email test passes (link + household/inviter substitution)
- [ ] 7.4 Renderer unit test loads both embedded templates
- [ ] 7.5 Angular build + lint + unit tests pass
- [ ] 7.6 Test-plan §6 cookbook updated for the email/invite test category

#### Manual

- [ ] 7.7 Invite without email behaves as today (generate + copy)
- [ ] 7.8 Invite with email sends mail with a working `/join/<token>` link, household + inviter names
- [ ] 7.9 The emailed link opens the join landing and accepts the invite
- [ ] 7.10 Password-reset email still arrives and its link still works
