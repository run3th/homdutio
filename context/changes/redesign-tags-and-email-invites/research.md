---
date: 2026-06-29T00:00:00Z
researcher: Rafal Michalak
git_commit: 194ace744231fb9f3ce5f197ffc481a7350ffe70
branch: main
repository: Homdutio
topic: "New UI design + task tags (per-household autocomplete) + emailed invite links"
tags: [research, codebase, tasks, tags, email, invites, angular, ui-redesign]
status: complete
last_updated: 2026-06-29
last_updated_by: Rafal Michalak
---

# Research: New UI design + task tags + emailed invite links

**Date**: 2026-06-29
**Researcher**: Rafal Michalak
**Git Commit**: 194ace744231fb9f3ce5f197ffc481a7350ffe70
**Branch**: main
**Repository**: Homdutio (github: run3th/homdutio)

## Research Question

Ground three pieces of work before planning:
1. **Task tags** — each task can carry multiple free-text tags; tags already used in the same household are suggested (autocomplete) while typing.
2. **Emailed invite links** — add an option to email a household invite link (templates for invite + password-reset mail were added under `templates/`).
3. **New UI design** — apply the Claude-design mockups (`templates/*.html`) as a reskin of the existing Angular SPA.

Stack: ASP.NET Core 9 minimal-API + EF Core 9 (Azure SQL) backend; Angular 21.2 standalone SPA built into `wwwroot/`; ASP.NET Core Identity + JWT auth.

## Summary

- **Tags is a generalization of an existing feature, not greenfield.** `HouseholdTask` already has a single nullable `Category` string. The full plumbing (entity → DbContext config → create/update endpoints → DTOs → Angular form field) is the exact template; tags just need a *collection* shape. **Open decision: do tags replace `Category` or coexist?**
- **Email sending is real (Azure Communication Services), but deliberately reset-only.** A `NoOpEmailSender` runs in dev/test; the live `AcsEmailSender` runs in prod. The `IEmailSender` interface has exactly one method (`SendPasswordResetAsync`) and bodies are **inline C# strings** — there is **no template engine**. Adding emailed invites means widening the interface, building server-side invite-link construction, capturing a recipient email, and introducing a `{{token}}` template-substitution step (none exists).
- **The UI is already componentized; the redesign is a reskin/repalette.** A single `:root` CSS-custom-property token block in `web/src/styles.scss` drives the whole app. Swapping `--color-primary` from soft-violet `#6c5ce7` to the design's teal `#2C6E63` repalettes every component for free. No CSS framework / Angular Material / Tailwind — plain SCSS + Angular CDK. The mockups are **bundled exports** (visual references only, not drop-in markup).

## Detailed Findings

### Area 1 — Task tags + per-household autocomplete

**Existing analogue — single `Category` field**
- Entity `HouseholdTask` (named to dodge `System.Threading.Tasks.Task`): `src/Homdutio.Data/Entities/HouseholdTask.cs`. `Category` (nullable string) at line 24; `HouseholdId` FK at line 16; the only existing collection nav is `ICollection<TaskEvent> Events` at line 57.
- DbContext config: `src/Homdutio.Data/ApplicationDbContext.cs:70-89`. `Category` `.HasMaxLength(100)` (line 73); enums stored as strings via `.HasConversion<string>()` (house convention); `HasIndex(t => t.HouseholdId)` (line 79) backs the board query. Household link configured `.WithMany()` (no reverse nav on `Household`) `OnDelete(Cascade)` at lines 81-84.

**Endpoints, DTOs, household scoping**
- `src/Homdutio.Api/Tasks/TaskEndpoints.cs` — minimal-API group `/api/tasks` `.RequireAuthorization()` (wired `Program.cs:158`). Create at lines 48-86 (`Category` trimmed/set line 71); edit (admin-only) `PUT /{id}` lines 302-338 (`Category` line 332); board `GET /` lines 25-45 returns **only open tasks** (`ClosedAtUtc == null`, line 35).
- DTOs (records, bottom of file): `CreateTaskRequest(Title, Description, Category)` line 601; `UpdateTaskRequest(...)` line 603; `TaskResponse(...)` lines 618-635 (`Category` line 622, plus server-computed affordance flags + `CommentCount`).
- **Household scoping is centralized and JWT-derived** — `src/Homdutio.Api/HouseholdScope.cs`. `ResolveCallerAsync` (lines 25-38) derives `HouseholdId` from the JWT `sub` claim; never trusts a client-supplied id. `LoadScopedTaskAsync` (line 41) filters `t.Id == id && t.HouseholdId == householdId`. **A tag-suggestion endpoint must follow this exact pattern**: resolve `caller.HouseholdId`, then `SELECT DISTINCT` tag values scoped to that household — and likely include closed tasks (don't reuse the board's `ClosedAtUtc == null` filter).

**House style for collections**
- No many-to-many and no owned collections exist. The universal pattern is **one-to-many via a child table with its own Guid PK + FK + cascade + FK index** — see `TaskComment` (DbContext lines 103-119, `IX_TaskComments_TaskId` line 111) and `TaskEvent` (lines 91-101). The migration template is `Migrations/20260611175249_AddTaskComments.cs` (CreateTable + ForeignKey cascade + CreateIndex).
- **Idiomatic fit**: a `TaskTag` child table (`Id`, `TaskId` FK cascade, `Value`, plus `HouseholdId` denormalized or joined for the suggestion query), modeled on `TaskComment`. A true `Tag`+join many-to-many would introduce a pattern with no precedent; free-text + household-scoped favors the simpler child-row design.

**Migration tooling (authoritative: `src/Homdutio.Data/MIGRATIONS.md`)**
- Context lives in `Homdutio.Data`, host in `Homdutio.Api` → every command needs `--project src/Homdutio.Data --startup-project src/Homdutio.Api`.
- EF CLI is a repo-local tool in `.config/dotnet-tools.json` (`dotnet tool restore` once). EF pinned 9.0.9.
- **Hard rule: migrations must be additive / backward-compatible** (nullable or defaulted columns, new tables), applied manually out-of-band, never on startup — prod is single-instance Azure SQL B1, no slots, no auto-rollback. Naming is PascalCase `Add<Thing>`.

**Angular task UI**
- All under `web/src/app/board/`. Model+service `board/task.service.ts`: `interface Task` (lines 9-31, `category?: string | null` line 14); `CreateTaskRequest`/`UpdateTaskRequest` (lines 44-55); `TaskService` (`providedIn: 'root'`) `create()` line 117, `update()` line 149, `load()` line 83 — a new `getTagSuggestions()` HTTP call lands here.
- Create form: `board/create-task/create-task.component.ts:29-33` (reactive `nonNullable.group`); category `<input>` at `create-task.component.html:31`.
- Edit form (admin-only): `board/task-detail/task-detail.component.ts:37-41`; editable input `task-detail.component.html:37`, read-only render lines 55-60.
- **A tag/chip/typeahead control must be built from scratch** — no `@angular/material`, no `MatAutocomplete`/`MatChip`. Only `@angular/cdk` is present (used for `dialog` + `drag-drop`). Build a custom standalone component with a reactive `FormControl` + filtered signal of suggestions, optionally `@angular/cdk/overlay`/`cdk/listbox` for the dropdown. Follow the existing reactive-forms + signals + `mapValidationProblem` pattern.

### Area 2 — Emailed invite links + password-reset email infra

**Email sending: real but config-gated, and reset-only by design**
- `Program.cs:100-109` selects the sender at startup: blank `AcsEmail:Endpoint` → `NoOpEmailSender`; configured endpoint → singleton `EmailClient` (Entra ID / `DefaultAzureCredential`, no key) + `AcsEmailSender`.
- Prod `appsettings.json:16-19` has a real endpoint (`https://homdutio.europe.communication.azure.com/`) + sender → live sends. Dev/test `appsettings.Development.json:8-10` blank → `NoOpEmailSender` (logs link, returns true). NuGet `Azure.Communication.Email` 1.1.0 (`Homdutio.Api.csproj:12`).
- `src/Homdutio.Api/Email/IEmailSender.cs:9-18` — **one method only**: `Task<bool> SendPasswordResetAsync(recipientEmail, resetLink, ...)`. Link construction is intentionally NOT here (lives in the endpoint). **This is the seam to widen for invites.**
- `AcsEmailSender.cs` — `SendAsync(WaitUntil.Started, …)` (fire-and-accept, no delivery polling) line 31; swallows failures → false (34-47). **Bodies are inline strings** in static `BuildResetMessage` (55-80): hardcoded subject, plain-text + minimal HTML, HTML-encodes the link. **No template engine.**
- `NoOpEmailSender.cs:11-19` logs recipient + link. `AcsEmailOptions.cs` binds `AcsEmail` (`SenderAddress` + `Endpoint`, non-secret).

**Password-reset flow (the working email pattern to copy)**
- `Auth/AuthEndpoints.cs`: `POST /api/auth/forgot-password` (103-132) — `GeneratePasswordResetTokenAsync` (113), Base64Url-encode token + URL-encode email (116-118), **build link from config `AppBaseUrl`** (117) → `{baseUrl}/reset-password?email=…&token=…`, then `SendPasswordResetAsync` (120). Always returns generic 200 (anti-enumeration); rate-limited (132). `POST /api/auth/reset-password` (137-176) decodes, resets, revokes all sessions.
- `AppBaseUrl`: prod `https://homdutio.azurewebsites.net` (`appsettings.json:15`), dev `https://localhost:5001`. SPA reset page is same-origin via `MapFallbackToFile`.

**Invite flow: currently in-app copy-link, NOT emailed**
- `Households/HouseholdEndpoints.cs`: `POST /api/households/invites` (82-109) mints a `HouseholdInvite` with a 256-bit hex token (`NewInviteToken()` 436), 7-day expiry (`InviteLifetime` 433); returns `InviteResponse(Token, ExpiresAtUtc)` — **just the raw token, no link, no email, no recipient captured**. `GET /api/households/invites/{token}` (113-132) public preview. `POST .../{token}/accept` (136-187) single-use consume via **rowversion optimistic concurrency** (load-bearing).
- **Link is built client-side**: `web/src/app/household/invite.service.ts:23-25` `buildJoinUrl(origin, token)` → `${origin}/join/${token}`. **Route shape differs from reset**: invite = `/join/<hexToken>` (path); reset = `/reset-password?email=…&token=…` (query). Generated/copied in `web/src/app/shell/topbar/topbar.component.ts:58-83`; join landing `web/src/app/join/join.component.ts`.

**The added templates are standalone previews, not wired in**
- `templates/Homdutio Email Preview.html` is a preview harness embedding two email-client-hardened (table-based, MSO conditionals, inline styles, base64 logo) templates as `srcdoc` iframes:
  - reset-password — placeholder `{{reset_link}}`
  - invite-member — placeholders `{{household_name}}`, `{{inviter_name}}`, `{{invite_link}}`
- They are NOT under `wwwroot/` and NOT embedded resources. Using them requires new scaffolding: embed/copy the HTML, load it, substitute `{{snake_case}}` tokens. None exists today (bodies are inline strings).

**Angular auth/invite UI**
- Reset: `web/src/app/auth/forgot-password/` and `web/src/app/auth/reset-password/`; API calls `web/src/app/auth/auth.service.ts:74-85`. New public endpoints must be added to the interceptor allowlist `web/src/app/auth/unauthorized.interceptor.ts`.

### Area 3 — New UI design (reskin)

**App structure (Angular 21.2: standalone, signals, lazy `loadComponent`, `@for`/`@if`)** — routes in `web/src/app/app.routes.ts`:

| Route | Component | Maps to mockup |
|---|---|---|
| `/login`, `/register`, `/forgot-password`, `/reset-password` | `auth/*` | **Auth Pro** |
| `/create-household` | `household/create-household/` | — |
| `/join/:token` | `join/join.component.ts` | — |
| `/board` (shell child) | `board/board.component.ts` | **Pro** (main) |
| `/members` (shell child) | `household/members/members.component.ts` | **Pro** (section) |

- Shell layout route (`app.routes.ts:44-58`) loads `shell/shell.component.ts` with guards `authGuard` + `requireHousehold` lifted to the parent. Shell = `<app-sidebar>` (icon rail desktop / bottom bar ≤400px) + `<app-topbar>` (household name, role badge, **+ Add task**, **Invite a member**, avatar menu) + `<router-outlet>`.
- Board sub-tree: `board.component.ts` orchestrates 3 columns (To do / In progress / Done, lines 38-42), CDK drag-reorder (within-column only; cross-column moves are lifecycle transitions), 4s polling, all lifecycle actions + dialogs. Presentational `task-column/`, `task-card/`; CDK dialogs `task-detail/`, `create-task/`, `comments/`, `delete-confirm/`, `send-back/`.

**Styling = single token source**
- `web/src/styles.scss:7-64` — a `:root` block of **CSS custom properties** (deliberately, to pierce component encapsulation and enable a future `data-theme`). Covers color, 4px spacing scale, radius, shadows, typography, focus ring, shell metrics. Current palette is soft-violet `--color-primary: #6c5ce7` (line 17), bg `#f7f7fb`.
- **Primary swap point**: change the `--color-primary` / `-hover` / `-soft` family here to teal `#2C6E63`; all components inherit via `var(--color-primary)`.
- Font: self-hosted Inter (`@import '@fontsource-variable/inter'`, line 5). Global primitives lines 87-191 (`.page`, `.card`, `.field`, `.notice`, `.btn`, `.alt-link`) — shared by auth/create/join pages, so reskinning auth ≈ restyling these globals + palette.
- Per-component scoped `.scss` referencing `var(--…)` (no hard-coded hex left after the last redesign). Inline-SVG icons, no icon library. Production budget caps a single component style at 8 kB (`angular.json:48-50`).

**Build / serve**
- `@angular/build:application`; output → `../src/Homdutio.Api/wwwroot` (`angular.json:26-29`). Dev via `proxy.conf.json`. csproj target `BuildAngularSpa` (`Homdutio.Api.csproj:27-31`) runs **only on Release** (`npm ci` if needed + `npm run build -- --base-href /`).
- Commands: dev `cd web && npm start` (ng serve via proxy); build `npm run build`; tests `npm test` (Vitest); lint `npm run lint`. Full release: `dotnet build -c Release`.

**Template bundle format**
- `templates/*.html` are Claude-design single-file bundles: a loader script base64-decodes + gunzips assets and mounts client-side; `<script type="__bundler/manifest">` holds UUID→gzip-base64 assets (not human-readable); `<script type="__bundler/template">` holds a JSON-encoded string of the **readable** rendered markup (JSX-like `<x-dc>` with inline styles, literal brand colors `#2C6E63`/`#1C2330`, `isMobile` logic, `sendInvite`/`toast` behaviors). **They only fully render in a browser**; design intent is extractable from the template script or by opening in a browser. **Visual references only — not drop-in Angular markup.**

## Code References

- `src/Homdutio.Data/Entities/HouseholdTask.cs:24` — existing `Category` field (tags analogue)
- `src/Homdutio.Data/ApplicationDbContext.cs:70-89` — task config; `:91-119` — `TaskEvent`/`TaskComment` one-to-many template
- `src/Homdutio.Data/MIGRATIONS.md` — additive-only migration rules + `--project`/`--startup-project` commands
- `src/Homdutio.Data/Migrations/20260611175249_AddTaskComments.cs` — child-table migration template
- `src/Homdutio.Api/Tasks/TaskEndpoints.cs:48-86,302-338,601-635` — create/edit/DTOs
- `src/Homdutio.Api/HouseholdScope.cs:25-41` — JWT-derived household scoping (copy for tag suggestions)
- `src/Homdutio.Api/Email/IEmailSender.cs:9-18` — one-method seam to widen
- `src/Homdutio.Api/Email/AcsEmailSender.cs:55-80` — inline-string body composition (no templating)
- `src/Homdutio.Api/Program.cs:100-109` — config-gated sender selection
- `src/Homdutio.Api/Auth/AuthEndpoints.cs:103-132` — reset email + `AppBaseUrl` link pattern
- `src/Homdutio.Api/Households/HouseholdEndpoints.cs:82-109,433-436` — invite mint (token only)
- `web/src/styles.scss:7-64` — design-token `:root` block (primary reskin point)
- `web/src/app/app.routes.ts:44-58` — shell layout route + guards
- `web/src/app/board/task.service.ts:9-31` / `create-task.component.html:31` / `task-detail.component.html:37` — Angular tag-input attach points
- `web/src/app/household/invite.service.ts:23-25` — client-side `/join/<token>` link build
- `web/src/app/auth/unauthorized.interceptor.ts` — public-endpoint allowlist
- `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs` / `AuthApiFactory.cs` — integration test + capturing-fake `IEmailSender` patterns

## Architecture Insights

- **Household scoping is server-derived from the JWT, never client-supplied** — any new household-scoped route (tag suggestions, emailed invites) must go through `HouseholdScope.ResolveCallerAsync` and is enforced by `RouteIsolationCoverageTests` (referenced `HouseholdScope.cs:15`). A new route without isolation coverage will fail that guard.
- **Migrations are additive-only against a no-rollback single-instance Azure SQL B1.** Tags persistence and any invite columns must be nullable/new-table, applied out-of-band.
- **Email is intentionally minimal** — one-method interface, inline bodies, synchronous in-request send (no queue), config-gated provider. Both invites and HTML templating are net-new seams the prior reset work deliberately deferred.
- **Link-base divergence**: reset builds links server-side from `AppBaseUrl`; invites build them client-side as `/join/<token>`. Emailing invites forces server-side link construction — reconcile the two (reuse `AppBaseUrl`, decide `/join/<token>` path vs query form).
- **The UI redesign is a repalette of an already-clean component tree** — single token source + scoped SCSS + inline SVG. The risk is low-structure, high-surface (touch the token block + many small SCSS files), not architectural.

## Historical Context (from prior changes)

- `context/archive/2026-06-25-password-reset/plan.md` — switched SendGrid→ACS (2026-06-25); kept email **reset-only** ("S-08 the only permitted v1 transactional email"; explicit non-goal "No invite or notification emails"); no background queue; token Base64Url-encoded in link; link base from config not Host; test harness swaps a capturing fake `IEmailSender` via `ConfigureWebHost`.
- `context/archive/2026-06-02-invite-and-multiplayer-board/plan.md` — explicit non-goal **"No invite emails — link shown in-app, shared out-of-band"** (the exact gap this change fills); invite API returns token only (no host); single-use via rowversion (don't replace); no recipient-email field, no pending-invite roster, no revoke UI.
- `context/archive/2026-06-08-ui-redesign/plan.md` — slice S-11 created the current architecture (Claude.ai + Scanye soft-violet inspiration). Phase 1 introduced the `:root` CSS-custom-property token layer + self-hosted Inter and tokenized all components; Phase 2 built the shell/sidebar/topbar/avatar-menu and the parent layout route; Phase 3 split the board into column/card + dialogs + kebab menu. Conventions to respect: single-source CSS-custom-property tokens, per-component scoped SCSS, inline-SVG icons, mobile-first NFR-2 (≤400px sidebar→bottom bar), ≤8 kB component-style budget.

## Related Research

- `context/archive/2026-06-25-password-reset/research.md` (if present) — email infra exploration
- `context/archive/2026-06-08-ui-redesign/research.md` (if present) — prior UI structure

## Open Questions

1. **Tags vs Category**: do free-text tags **replace** the existing `Category` field, or **coexist** with it? (Affects migration + DTO shape + UI.)
2. **Tag persistence shape**: `TaskTag` child table (recommended, matches house style) vs. a normalized `Tag`+join. Where does `HouseholdId` live for the suggestion query (denormalized on `TaskTag` vs joined through `HouseholdTask`)?
3. **Suggestion scope**: include tags from **closed** tasks (likely yes) — so the suggestion query must not reuse the board's `ClosedAtUtc == null` filter. Any tag normalization (case-insensitive de-dup, trimming, max length/count per task)?
4. **Invite emailing**: does it **replace** the copy-link flow or **add** an optional "send by email" path? Does the API now capture a recipient email + look up the inviter's `DisplayName`? Reconcile the `/join/<token>` link shape with server-side `AppBaseUrl` construction.
5. **Email templating mechanism**: how to load the `templates/*.html` (embedded resource vs `wwwroot` vs csproj `Content`) and what substitution approach for `{{token}}` placeholders. Should the existing reset email be migrated onto the same template mechanism, or only the new invite email?
6. **Design fidelity**: extract exact colors/spacing/layout from the bundles — open in a browser or decode the `__bundler/template` script? How much beyond the teal repalette does "new design" entail (layout changes vs pure color/typography swap)?
7. **Phasing**: these are three fairly independent slices (reskin / tags / invite-email). Plan as one change with 3 phases, or split? (Raised at `/10x-new`.)
