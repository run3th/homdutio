# UI Redesign (Board Experience Overhaul) Implementation Plan

## Overview

Reskin the entire authenticated experience to a minimalist, elegant, pastel style inspired by
Claude.ai and Scanye, replacing the bare v1 shell. Three things change together: (1) a **design-token
foundation** — CSS custom properties for colour/spacing/radius/shadow/typography plus a genuinely
loaded Inter font — that every component consumes; (2) a **persistent app shell** — a light /
translucent sidebar + a topbar whose avatar menu finally surfaces logout, with the sidebar collapsing
to a bottom icon bar on phones; and (3) a **recomposed board** — the monolithic board split into
`task-column` / `task-card`, the create-task form moved into a dialog (consistent with edit), and the
edit dialog's delete reworked into a quiet header trash-icon affordance.

The new layout is designed up front to host the affordances of the surrounding slices — S-04 (edit
dialog), S-05 (loop recovery on cards), S-06 (invite), S-09 (member admin) — so those slot into a
ready surface rather than forcing a re-layout. This delivers roadmap slice **S-11**.

## Current State Analysis

- **No app shell.** `web/src/app/app.html` is just `<router-outlet />` and `app.scss` is empty
  (`app.scss:1`). Every route (login, register, create-household, board, join) renders as its own
  full page. There is no sidebar/topbar today.
- **The board is monolithic.** `board.component` (`web/src/app/board/board.component.ts`,
  `board.component.html`) owns the header (household name + role badge + Invite button + generated
  link panel), the inline create-task form, three **inline** columns with **inline** task cards
  (`board.component.html:26-104`), drag-drop, polling (F-03), lifecycle actions, and opening the
  task-detail dialog. `task-column` and `task-card` are not separate components.
- **`task-form` already exists** as `create-task.component` (`board/create-task/`), embedded inline in
  the board. `task-detail.component` is a CDK dialog (S-04) that both **edits** (form) and **views**
  (read-only) a task — and today shows **Save and Delete as two equally-weighted buttons side by
  side** (`task-detail.component.html:35-62`), the UX the user flagged as weak.
- **Logout is effectively unreachable.** `AuthService.logout()` exists
  (`web/src/app/auth/auth.service.ts`) but no template on the board (or anywhere reachable post-login)
  calls it — `''` redirects to `/board` and the board has no logout control. The redesign's avatar
  menu is the fix.
- **Design system is ad-hoc.** Global `styles.scss` defines `.card` / `.field` / `.btn` with
  hard-coded hex (`#4f46e5` indigo, `#f5f5f7` bg) repeated across every component's scoped SCSS. There
  are **no tokens/variables**. `'Inter'` is named in the `body` font stack (`styles.scss:16-18`) but
  **never loaded** — `index.html` has no font `<link>`, so it silently falls back to system fonts.
- **Routing is flat, standalone, lazy-loaded** (`app.routes.ts`): `login`, `register`,
  `create-household` (guards `authGuard` + `requireNoHousehold`), `board` (guards `authGuard` +
  `requireHousehold`), `join/:token` (public). `''` → `board`; `**` → `board`.
- **NFR-2 is honoured everywhere** — the board stacks columns vertically and goes three-up at
  ≥ 640px (`board.component.scss:90-95, 288-298`); cards are full-width. This must survive.
- **Test stack:** vitest with `HttpTestingController`; specs exist for board, create-task,
  task-detail, auth, household. Build: the Release `BuildAngularSpa` MSBuild target runs `ng build`.

## Desired End State

After this plan:

1. A logged-in household member lands on `/board` inside a polished shell: a light/translucent
   **sidebar** (Home, Tasks; collapsing to a bottom icon bar at ≤ 400px) and a **topbar** showing the
   household name + role on the left and, on the right, a **+ Add task** CTA, an **Invite a member**
   CTA, and an **avatar menu** (email + Logout).
2. The board is a clean Claude-style kanban: three white, soft-shadowed columns of pastel task cards
   with metadata (Created by, Claimed by, Created), driven by extracted `task-column` / `task-card`
   components. Drag-reorder, polling, and the lifecycle actions all work exactly as before.
3. **Creating** a task opens a dialog (the same dialog language as editing); **editing** a task opens
   the detail dialog whose **delete** is a quiet header trash-icon with an inline confirm, with **Save**
   the sole primary action.
4. Every screen uses the shared **design tokens** and a **really-loaded Inter**; the auth / join /
   create-household pages adopt the tokens + font (no layout redesign) so nothing looks half-reskinned.
5. **Logout is reachable** from the avatar menu and lands cleanly on `/login`.
6. The whole thing holds at ≤ 400px with no horizontal scroll (NFR-2).

**Verification:** the automated suites in each phase pass; the manual checks in the Testing Strategy
confirm the visual outcome, the responsive shell, the dialogs, and that nothing regressed.

### Key Discoveries:

- The shell must wrap **only the authenticated household routes** without disturbing the standalone
  auth/join pages — Angular's idiomatic fit is a **parent layout route with child routes**, moving the
  board's guards (`authGuard` + `requireHousehold`) up to the shell route (`app.routes.ts:25-29`).
- Relocating Invite out of the board means moving the `InviteService` wiring + generated-link surface
  into the topbar; the board's invite state (`board.component.ts:39-45, 130-155`) lifts with it.
- `task-detail.component` is **both** an edit form and a read-only view — the delete rework touches
  only the editable branch (`task-detail.component.html:15-63`); the read-only branch is unchanged.
- Converting create to a dialog reuses `CreateTaskComponent`'s logic almost verbatim
  (`create-task.component.ts:35-65`); it gains a dialog wrapper that closes on success instead of an
  inline reset.
- CSS custom properties (not SCSS variables) are required because they pierce Angular's per-component
  style encapsulation from a single `:root` definition and leave the door open for a dark theme.

## What We're NOT Doing

- **No Members or Settings pages, and no dead nav icons for them.** The sidebar shows only the
  destinations that exist today (Home / Tasks → the board). The component structure and tokens are
  built so a Members page (S-09) drops in later, but no such page or icon ships here.
- **No member-administration, loop-recovery, or invite-redesign behaviour.** We design *slots* — the
  task-card action area accommodates S-05 unclaim/send-back; the topbar/avatar menu accommodates S-09
  — but ship no new backend-touching behaviour. Invite keeps its existing generate-copy logic, only
  relocated.
- **No dark mode now.** Light theme only; tokens are structured so a dark set is a later add.
- **No layout redesign of the auth / join / create-household pages** — they adopt tokens + font only.
- **No API, data-model, or endpoint changes.** This is a pure presentation slice.
- **No new e2e/browser harness** — responsive/visual behaviour is verified manually (project has none).
- **No behaviour change to lifecycle, drag-reorder, polling, or the task-detail read-only view.**

## Implementation Approach

Land the design-token foundation first (pure styling, zero structural change) so the riskiest visual
churn is isolated and reversible, and every later phase builds on stable tokens. Then introduce the
shell (new components + a routing restructure that leaves the auth pages untouched), relocating Invite
and adding the avatar-menu logout. Finally recompose the board: extract `task-column` / `task-card`,
restyle the kanban and cards, move create into a dialog, and rework the edit dialog's delete — keeping
every existing behaviour (and its specs) green throughout.

## Critical Implementation Details

- **Routing restructure must preserve guards.** When `board` becomes a child of a `ShellComponent`
  parent route, the parent route carries `canActivate: [authGuard, requireHousehold]` and the child
  carries none — otherwise the guards run twice or the shell renders for an unauthorised user. `''` and
  `**` redirects continue to resolve to `board` (now nested).
- **Mobile shell is a distinct layout, not a narrowed sidebar.** At ≤ 400px the sidebar is replaced by
  a fixed bottom icon bar (NFR-2). Drive it with a CSS breakpoint on the shell, not JS, so there is no
  layout-shift on resize; reserve bottom padding on the scroll area equal to the bottom bar height so
  the last card is never occluded.
- **Polling pause must survive the create dialog.** The board pauses polling while the task-detail
  dialog is open (`board.component.ts:89-93`). The new **create** dialog must do the same — pause on
  open, resume on close — or a 4s tick can refetch mid-entry. Wire it through the same
  `tasks.setPaused(true/false)` path.

## Phase 1: Design-System Foundation

### Overview

Introduce the CSS-custom-property token layer and a really-loaded Inter, then refactor the global
stylesheet and every existing screen to consume the tokens — with **no structural or behavioural
change**. Same layouts, new skin; all specs stay green. This isolates the visual-foundation risk.

### Changes Required:

#### 1. Design tokens

**File**: `web/src/styles.scss`

**Intent**: Define one canonical set of design tokens as CSS custom properties on `:root`, so every
component references them instead of hard-coded hex. Locked values (Claude/Scanye pastel direction).

**Contract**: Add a `:root` block with these tokens (exact values locked here):

```css
:root {
  /* Surfaces & neutrals */
  --color-bg: #f7f7fb;            /* app background (lavender-grey) */
  --color-surface: #ffffff;       /* cards, columns, dialogs */
  --color-surface-2: #f4f4f9;     /* inset surfaces (a card on a column) */
  --color-border: #ececf3;
  --color-text: #1d1c2b;
  --color-text-muted: #6b6a7b;
  --color-text-subtle: #9a99a8;
  /* Primary (soft violet) */
  --color-primary: #6c5ce7;
  --color-primary-hover: #5a48d6;
  --color-primary-soft: #eeebff;  /* active nav, chips, role badge */
  --color-on-primary: #ffffff;
  /* Accent + semantic (soft pairs: fg / bg) */
  --color-blue: #5b8def;   --color-blue-soft: #e8f0ff;
  --color-success: #1f9d63; --color-success-soft: #e6f7ef;
  --color-danger: #e5484d;  --color-danger-soft: #fdecec;
  --color-warning: #cc8a1a; --color-warning-soft: #fbf0db; /* reserved: S-05 send-back */
  /* Spacing (4px base) */
  --space-1: .25rem; --space-2: .5rem; --space-3: .75rem; --space-4: 1rem;
  --space-5: 1.5rem; --space-6: 2rem; --space-8: 3rem;
  /* Radius */
  --radius-sm: .5rem; --radius-md: .75rem; --radius-lg: 1rem; --radius-pill: 999px;
  /* Shadows (soft) */
  --shadow-sm: 0 1px 2px rgba(29,28,43,.06), 0 1px 3px rgba(29,28,43,.08);
  --shadow-md: 0 4px 12px rgba(29,28,43,.08);
  --shadow-lg: 0 12px 32px rgba(29,28,43,.12);
  /* Typography */
  --font-sans: 'Inter Variable', 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
  --text-xs: .75rem; --text-sm: .875rem; --text-base: 1rem; --text-lg: 1.125rem; --text-xl: 1.5rem;
  --weight-normal: 400; --weight-medium: 500; --weight-semibold: 600; --weight-bold: 700;
  /* Focus + shell metrics */
  --focus-ring: 0 0 0 2px var(--color-primary);
  --shell-sidebar-w: 4.75rem; --shell-topbar-h: 3.5rem; --shell-bottombar-h: 3.5rem;
}
```

#### 2. Self-host Inter

**File**: `web/package.json`, `web/src/styles.scss`

**Intent**: Actually load Inter from our own origin (no third-party CDN; works offline; matches the
same-origin App Service deploy) so the named font renders everywhere instead of the system fallback.

**Contract**: Add the `@fontsource-variable/inter` dependency; `@import` its CSS once at the top of
`styles.scss` (above the token block). `--font-sans` leads with `'Inter Variable'`. No `index.html`
change required.

#### 3. Global stylesheet refactor to tokens

**File**: `web/src/styles.scss`

**Intent**: Rewrite the existing global primitives (`body`, `.page`, `.card`, `.field`, `.btn`,
`.notice`, error/alt-link helpers) to consume the tokens — same look-and-feel, now centralised — and
add the soft-shadow / rounded / pastel polish.

**Contract**: Replace literal hex/length values in the existing rules with `var(--…)` tokens; `.btn`
and `button[type='submit']` use `--color-primary`; inputs' focus uses `--focus-ring`; surfaces use
`--shadow-sm`/`--radius-md`. No new class names; no markup change.

#### 4. Token alignment of existing component styles

**File**: `web/src/app/board/board.component.scss`, `board/create-task/create-task.component.scss`,
`board/task-detail/task-detail.component.scss`, `auth/login/login.component.scss`,
`auth/register/register.component.scss`, `household/create-household/create-household.component.scss`,
`join/join.component.scss`

**Intent**: Swap each component's hard-coded hex/spacing/shadow for the shared tokens so the whole app
shares one palette and the auth/join/create-household pages stop looking foreign — without touching
their markup or layout.

**Contract**: Mechanical replacement of literals with `var(--…)` tokens across these `.scss` files.
The indigo `#4f46e5` maps to `--color-primary`; greys map to the neutral tokens; `#f5f5f7` →
`--color-bg`. Drag/CDK state styles keep their behaviour; only colours/shadows are tokenised. No
`.html`/`.ts` changes in this phase.

### Success Criteria:

#### Automated Verification:

- `@fontsource-variable/inter` installs and the app builds: `npm install` + `npm run build` (in `web/`)
- Release `BuildAngularSpa` target succeeds: `dotnet build -c Release`
- vitest passes unchanged (no behaviour touched): `npm test` (in `web/`)
- Prettier clean on changed files: `npx prettier --check` (in `web/`)

#### Manual Verification:

- Every screen (login, register, create-household, board, join) renders with the new tokens and Inter
  actually loads (DevTools → Network shows the Inter font file, not a system fallback)
- No layout regressions versus today on any screen
- ≤ 400px: no horizontal scroll on any screen (NFR-2)

**Implementation Note**: After automated verification passes, pause for manual confirmation before
Phase 2.

---

## Phase 2: App Shell (Sidebar + Topbar)

### Overview

Introduce the persistent shell — a light/translucent sidebar and a topbar with the household identity,
the Invite CTA (relocated from the board), and an avatar menu that surfaces logout — wrapping the board
via a parent layout route. The sidebar collapses to a bottom icon bar at ≤ 400px.

### Changes Required:

#### 1. ShellComponent (layout)

**File**: `web/src/app/shell/shell.component.ts` / `.html` / `.scss` (new)

**Intent**: The persistent layout frame: renders the sidebar, the topbar, and a `<router-outlet>` for
the page content. Owns the desktop-sidebar ↔ mobile-bottom-bar responsive switch.

**Contract**: Standalone component importing `RouterOutlet`, `SidebarComponent`, `TopbarComponent`.
Grid/flex layout: sidebar (width `--shell-sidebar-w`) + main column (topbar height `--shell-topbar-h`
+ scrollable outlet). At ≤ 400px a CSS breakpoint hides the side rail and shows the bottom bar; the
outlet reserves `--shell-bottombar-h` bottom padding so content isn't occluded. No business logic.

#### 2. SidebarComponent

**File**: `web/src/app/shell/sidebar/sidebar.component.ts` / `.html` / `.scss` (new)

**Intent**: Icon navigation — light/translucent, pastel active state. Shows only the destinations that
exist today (Home, Tasks), structured so Members/Settings slot in later (S-09).

**Contract**: Renders `routerLink` nav items with `routerLinkActive` for the active-pill styling.
Items today: **Home** and **Tasks**, both → `/board` (they converge on the single board destination
now and diverge when a distinct home/members page lands — documented interim). Renders as the side rail
on desktop and the fixed bottom bar on mobile (styling driven by the shell breakpoint). Icons via inline
SVG (no icon-font dependency).

#### 3. TopbarComponent (section title + Invite + avatar menu)

**File**: `web/src/app/shell/topbar/topbar.component.ts` / `.html` / `.scss` (new)

**Intent**: The top bar: household name + role badge on the left; on the right an **Invite a member**
CTA (relocated from the board) and the avatar menu. (The **+ Add task** CTA is added in Phase 3, once
the create dialog exists.)

**Contract**: Reads `HouseholdService.current` for the name + role badge. Hosts the relocated invite
action: injects `InviteService`, generates a token, builds the URL with `buildJoinUrl`, copies to
clipboard, and surfaces the link + copied/“error” state (move the existing behaviour from
`board.component.ts:39-45,126-155` and its template/styles). Embeds `AvatarMenuComponent`.

#### 4. AvatarMenuComponent (logout)

**File**: `web/src/app/shell/topbar/avatar-menu.component.ts` / `.html` / `.scss` (new)

**Intent**: The avatar button + dropdown that finally surfaces logout (and is the future home for S-09
account/settings items).

**Contract**: A toggle button (user initial/avatar) opening a small menu showing the signed-in email
(`AuthService.email`) and a **Logout** item. Logout calls `AuthService.logout()` then navigates to
`/login` (the path the unauthorized interceptor already uses). Closes on outside-click / Escape
(accessible: `aria-expanded`, focus return).

#### 5. Routing restructure (parent layout route)

**File**: `web/src/app/app.routes.ts`

**Intent**: Nest the board under the shell so the shell persists across authenticated navigations,
while the auth / join / create-household pages stay full-page.

**Contract**: Add a parent route with `component: ShellComponent` carrying
`canActivate: [authGuard, requireHousehold]` and a `children` array containing the board
(`path: 'board'`, no own guards now). `login`, `register`, `create-household`, `join/:token` remain
top-level (unchanged). `''` → `board` and `**` → `board` still resolve (now nested). The board's
lazy `loadComponent` is preserved as a child.

#### 6. Strip relocated chrome from the board

**File**: `web/src/app/board/board.component.html`, `board.component.ts`, `board.component.scss`

**Intent**: Remove the board header (household name + role badge + Invite button + link panel) now that
the topbar owns identity + invite, leaving the board to render the create-task form + columns
(recomposed in Phase 3).

**Contract**: Delete the `<header class="board-header">` block and the invite panel/error markup
(`board.component.html:2-22`) and the corresponding invite state/methods + styles moved to the topbar.
The board keeps `<app-create-task />` and the columns for now. No lifecycle/drag/polling change.

### Success Criteria:

#### Automated Verification:

- Build + Release `BuildAngularSpa` succeed with the restructured routing
- vitest passes including new specs: sidebar renders nav to `/board`; avatar menu Logout calls
  `AuthService.logout()` and navigates to `/login`; topbar invite generates + exposes the link
- Existing board/auth specs updated and green (board no longer owns invite/header)
- Prettier clean on changed files

#### Manual Verification:

- `/board` renders inside the shell: light/translucent sidebar (Home, Tasks), topbar with household
  name + role, Invite CTA, and avatar
- Logout is reachable from the avatar menu and lands on `/login` (no redirect loop)
- Invite still works from the topbar (generate → copy → visible link fallback)
- ≤ 400px: the sidebar is a bottom icon bar, no horizontal scroll, last content not occluded; the
  auth / join / create-household pages remain full-page with no shell
- No regressions to lifecycle actions, drag-reorder, or polling on the board

**Implementation Note**: After automated verification passes, pause for manual confirmation before
Phase 3.

---

## Phase 3: Board Recomposition & Task Dialogs

### Overview

Recompose the board into `task-column` / `task-card` components, restyle the kanban and cards to the
Claude pastel style, move create-task into a dialog (consistent with edit), add the **+ Add task** CTA
to the topbar, and rework the edit dialog's delete into a quiet header trash-icon with inline confirm.
Card layout reserves a slot for S-05 loop-recovery actions. All existing behaviour stays intact.

### Changes Required:

#### 1. Extract TaskCardComponent

**File**: `web/src/app/board/task-card/task-card.component.ts` / `.html` / `.scss` (new)

**Intent**: A presentational, Claude-style pastel task card — title (opens detail), optional
category chip + description, metadata (Created by / Claimed by / Created), the lifecycle action
buttons, and the drag handle — emitting events the board handles.

**Contract**: `@Input() task: Task`; `@Output()` events for `openDetail`, `claim`, `markDone`,
`confirm`, `edit`, `delete` (and drag handled via the parent's `cdkDrag` on the host or a wrapping
element). Markup lifted from `board.component.html:38-98`, restyled with tokens (`--color-surface`,
`--shadow-sm`, `--radius-md`, pastel category chip). The card's top-right tool cluster holds the drag
handle **and** a kebab (⋯) trigger for the per-task overflow menu (defined in change #5). Only the
**primary** lifecycle action (Claim / Mark done / Confirm) is a button on the card; management actions
live in the menu. Keeps the drag handle distinct from the title (so a drag never opens the dialog).

#### 2. Extract TaskColumnComponent

**File**: `web/src/app/board/task-column/task-column.component.ts` / `.html` / `.scss` (new)

**Intent**: A white, soft-shadowed column — title + the CDK drop list of `task-card`s — so the board
template is a clean three-column composition.

**Contract**: `@Input() label`, `@Input() status`, `@Input() tasks: Task[]`; hosts the
`cdkDropList` + drop event passthrough and the empty-state. Renders `app-task-card` per task and
re-emits card events up to the board. Styling: `--color-surface`, `--shadow-sm`, `--radius-lg`. The
board keeps owning drag/drop wiring and the `tasksFor`/`drop` logic; the column is the presentational
container.

#### 3. Board template recomposition

**File**: `web/src/app/board/board.component.html`, `board.component.ts`, `board.component.scss`

**Intent**: Reduce the board to a composition of `task-column`s and remove the now-extracted inline
card markup; the board retains the column/drag/polling/lifecycle orchestration.

**Contract**: Replace the inline `@for column … article.task-card …` block
(`board.component.html:26-104`) with `@for (column …) { <app-task-column …/> }`, wiring inputs/outputs
to existing handlers (`tasksFor`, `drop`, `claim`, `markDone`, `confirm`, `openDetail`, drag
start/end). Move card/column styles out of `board.component.scss` into the new components. Behaviour
unchanged.

#### 4. Create-task as a dialog + topbar CTA

**File**: `web/src/app/board/create-task/create-task.component.ts` / `.html` (modify),
`web/src/app/shell/topbar/topbar.component.*` (add CTA)

**Intent**: Move task creation from an inline board form into a dialog opened by a **+ Add task** CTA
in the topbar — consistent with the edit dialog — and remove the inline form from the board.

**Contract**: Wrap `CreateTaskComponent` as CDK dialog content (reusing its reactive form + 
`TaskService.create` logic); on success it **closes the dialog** instead of resetting inline. Add a
**+ Add task** primary CTA to the topbar that opens the dialog via `Dialog.open(...)`; pause/resume
polling around it (`tasks.setPaused`), mirroring the detail dialog. Remove `<app-create-task />` from
`board.component.html`. At ≤ 400px the CTA collapses to an icon-only button.

#### 5. Per-task overflow menu (⋯) on the card + delete-free edit dialog

**File**: `web/src/app/board/task-card/task-card.component.*` (menu), `board/task-detail/task-detail.component.html` + `.ts` (strip delete), `board/board.component.ts` (delete-confirm dialog)

**Intent**: Move **all per-task management actions off the edit dialog and onto a kebab (⋯) menu on the
card** — Edit, Delete (To-do only), and the reserved S-05 Unclaim / Send back — so the edit dialog is a
**pure form** (Title / Description / Category + Cancel / Save) and destruction never sits beside Save.
This is the user's chosen pattern, replacing the earlier header-trash idea.

**Contract**:
- **Card menu**: `task-card` renders a ⋯ `icon-button` in its top-right tool cluster (beside the drag
  handle) opening a small dropdown. Items are gated by the task's affordance flags — **Edit** + **Delete**
  when `canEdit` / `canDelete` (To-do), with **Unclaim** / **Send back** as reserved S-05 slots (not
  wired now). The menu emits `edit` and `delete` (and later the S-05 events) up to the board. Single-open,
  closes on outside-click / Escape, `aria-expanded`.
- **Edit dialog → pure form**: in `task-detail.component`, remove the delete affordance from the
  editable branch entirely (the `canDelete` button + inline-confirm markup `task-detail.component.html:40-61`
  and the `requestDelete` / `confirmDelete` / `cancelDelete` wiring). The dialog becomes Title /
  Description / Category + Cancel / Save. Read-only branch unchanged.
- **Delete confirm**: the board opens a small dedicated confirm dialog (CDK Dialog: “Delete task? · Cancel
  / Delete”) from the card's `delete` event, calling `TaskService.delete`; on success the board refetches
  (existing self-heal pattern). The To-do-only + 409 guards are unchanged server-side.

### Success Criteria:

#### Automated Verification:

- Build + Release `BuildAngularSpa` succeed
- vitest passes including: board specs updated for the `task-column`/`task-card` composition; new
  `task-card` spec (renders metadata + emits actions, incl. the ⋯ menu's `edit`/`delete`);
  create-dialog spec (creates + closes); delete-confirm-dialog spec; detail spec updated for the
  now delete-free edit dialog
- Prettier clean on changed files

#### Manual Verification:

- **+ Add task** in the topbar opens the create dialog; creating adds a card to “To do” and closes the
  dialog; polling pauses while it’s open
- Card **⋯ menu** hosts Edit + Delete (To-do) and the reserved S-05 Unclaim / Send back; **Delete**
  opens a confirm dialog and works (To-do-only, 409 guard intact); the **edit dialog is a pure form**
  with no delete affordance
- Kanban restyled: white soft-shadowed columns, pastel Claude task cards with metadata; lifecycle
  actions, drag-reorder, and polling all behave as before
- ≤ 400px: columns stack, cards full-width, both dialogs usable, **+ Add task** is an icon button; no
  horizontal scroll (NFR-2)

**Implementation Note**: Final phase — after manual confirmation, the slice is complete.

---

## Testing Strategy

### Unit Tests (vitest, SPA):

- **Sidebar**: renders nav items linking to `/board`; active state on the current route.
- **Avatar menu**: opens/closes; Logout calls `AuthService.logout()` and navigates to `/login`.
- **Topbar**: shows household name/role; invite generates a token and exposes the copyable link.
- **Task-card**: renders title + metadata (Created by / Claimed by / Created) and emits
  claim/markDone/confirm/openDetail; shows only the permitted lifecycle actions.
- **Create dialog**: submits a valid form → `TaskService.create` → closes; invalid form blocks submit.
- **Card ⋯ menu**: emits `edit` / `delete`; items gated by affordance flags (Edit/Delete only on
  To-do). **Delete-confirm dialog**: confirm → `TaskService.delete` → board refetch.
- **Task-detail**: the edit dialog is a pure form (no delete affordance); read-only branch unchanged.
- **Regression**: existing board/auth/household specs stay green after extraction + routing change.

### Integration Tests:

- None (no backend/API change in this slice).

### Manual Testing Steps:

1. Reload `/board`: shell renders (sidebar + topbar); Inter loaded; pastel restyle visible.
2. Add a task via the topbar CTA dialog → appears in “To do”.
3. Edit a task → header-trash delete flow; Save-only footer.
4. Drag-reorder within a column; lifecycle (claim → done → confirm); confirm a task drops off — all intact.
5. Invite from the topbar → copy link.
6. Logout from the avatar menu → `/login`.
7. ≤ 400px: bottom nav bar, stacked columns, usable dialogs, no horizontal scroll.

## Performance Considerations

- Self-hosting the Inter variable font adds one bundled font asset (cached, same-origin) — negligible,
  and removes the silent system-fallback inconsistency. No runtime cost from CSS custom properties.
- Component extraction does not change the polling/drag behaviour or request volume.

## Migration Notes

- No data or API migration — pure presentation. The token layer is additive; component extraction is a
  refactor behind the same behaviour.
- Tokens are authored so a future `:root[data-theme='dark']` (or media-query) set is a later add
  without touching component styles.

## References

- Roadmap slice: `context/foundation/roadmap.md` S-11 (Experience / UI stream)
- Change identity: `context/changes/ui-redesign/change.md`
- Current board surfaces: `web/src/app/board/board.component.{ts,html,scss}`,
  `board/create-task/`, `board/task-detail/`
- Shell-less app today: `web/src/app/app.{html,scss}`, `app.routes.ts`
- Auth/logout to surface: `web/src/app/auth/auth.service.ts`,
  `web/src/app/auth/unauthorized.interceptor.ts`
- Global styles to tokenise: `web/src/styles.scss`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Design-System Foundation

#### Automated

- [x] 1.1 `@fontsource-variable/inter` installs + app builds (`npm install` + `npm run build`) — 1d75381
- [x] 1.2 Release `BuildAngularSpa` target succeeds (`dotnet build -c Release`) — 1d75381
- [x] 1.3 vitest passes unchanged (`npm test`) — 1d75381
- [x] 1.4 Prettier clean on changed files (`npx prettier --check`) — 1d75381

#### Manual

- [x] 1.5 All screens render with tokens + Inter actually loads (DevTools shows the font file) — 1d75381
- [x] 1.6 No layout regressions; ≤ 400px no horizontal scroll on any screen (NFR-2) — 1d75381

### Phase 2: App Shell (Sidebar + Topbar)

#### Automated

- [x] 2.1 Build + Release `BuildAngularSpa` succeed with restructured routing — a745e2c
- [x] 2.2 vitest: sidebar nav, avatar-menu logout (→ `AuthService.logout` + `/login`), topbar invite — a745e2c
- [x] 2.3 Existing board/auth specs updated + green (board no longer owns invite/header) — a745e2c
- [x] 2.4 Prettier clean on changed files — a745e2c

#### Manual

- [x] 2.5 `/board` inside shell: sidebar (Home, Tasks), topbar (name + role, Invite, avatar) — a745e2c
- [x] 2.6 Logout reachable from avatar menu → `/login`, no loop — a745e2c
- [x] 2.7 Invite works from the topbar (generate + copy + visible link) — a745e2c
- [x] 2.8 ≤ 400px: sidebar → bottom bar, no horizontal scroll; auth/join/create-household stay full-page — a745e2c
- [x] 2.9 No regressions to lifecycle/drag/polling on the board — a745e2c

### Phase 3: Board Recomposition & Task Dialogs

#### Automated

- [x] 3.1 Build + Release `BuildAngularSpa` succeed — 0d7e13b
- [x] 3.2 vitest: board recomposition + new task-card/create-dialog/detail-delete specs green — 0d7e13b
- [x] 3.3 Prettier clean on changed files — 0d7e13b

#### Manual

- [x] 3.4 Topbar **+ Add task** opens create dialog; create adds a To-do card + closes; polling pauses — 0d7e13b
- [x] 3.5 Card **⋯ menu** = Edit + Delete (+S-05 Unclaim/Send back slots); Delete via confirm dialog; edit dialog is delete-free — 0d7e13b
- [x] 3.6 Kanban restyled (white shadowed columns, pastel cards w/ metadata); lifecycle/drag/polling intact — 0d7e13b
- [x] 3.7 ≤ 400px: stacked columns, full-width cards, usable dialogs, icon **+ Add task**, no h-scroll — 0d7e13b
