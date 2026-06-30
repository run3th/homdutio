# Join-Household Flow + User Settings (Display Name & Profile Photo) Implementation Plan

## Overview

Two user-facing features delivered as one change in three shippable phases:

1. **Join-household flow** — reshape the existing `/join/:token` page into the three designed states (`joinLoggedOut`, `join`, `joinTaken`) and surface the inviter on it.
2. **User settings** — a Settings modal to edit display name and upload/remove a profile photo, with avatars shown everywhere a user appears.

Avatars are net-new infrastructure (no column, upload, or serving exists today). Display-name changes already propagate for free because names are resolved at fetch time from `AspNetUsers` — nothing is denormalized.

## Current State Analysis

- **Invite/join backend** (`src/Homdutio.Api/Households/HouseholdEndpoints.cs`): `POST /api/households/invites` (mint), `GET /api/households/invites/{token}` (anonymous preview → `InvitePreviewResponse { householdName }` only), `POST /api/households/invites/{token}/accept` (join). The accept path returns `409` with `{ message }` when the caller already belongs to a household (`HouseholdEndpoints.cs:192-196`); the token stays unconsumed. `HouseholdInvite.CreatedById` already records the inviter (`HouseholdInvite.cs:26`) — no migration needed to show them.
- **Join page** (`web/src/app/join/join.component.*`, route `/join/:token`, no guard): already branches on preview state and auth/membership, but as utilitarian/error-ish UI. Logged-out branch links to `/login?returnUrl=/join/<token>` and `/register` (carries `returnUrl` through to login). Already-member branch shows an error-styled message + link to `/board`.
- **Identity model**: `ApplicationUser : IdentityUser` has `DisplayName` only — **no avatar field** (`ApplicationUser.cs:16`). `HouseholdTask` (`CreatedById`/`ClaimedById`/`ConfirmedById`), `TaskComment` (`AuthorId`), and `HouseholdMember` (`UserId`) store **only FKs** — display names are resolved server-side at fetch (`TaskEndpoints.ResolveNamesAsync`, `ResolveCommentNamesAsync`; members roster joins `Users`). Comment `Body` is user-authored text; the audit trail is a separate `TaskEvents` table keyed by user-id + event enum — **no display name is ever baked into stored text**.
- **Current-user surface**: `GET /api/auth/me` → `MeResponse(Sub, Email)` — no DisplayName, no avatar. The topbar/avatar-menu render the **email** and an email-initial glyph (`avatar-menu.component.ts:24,29-31`); they never use DisplayName.
- **Avatar rendering today**: every surface (task cards creator/claimer, members list, avatar-menu) draws a colored initial. Color is a deterministic client-side hash of the name (`web/src/app/board/tag-color.ts:48-51 avatarColor()`). Comments show author name as text, no glyph. No `<img>` anywhere.
- **Conventions**: Angular CDK Dialog + global `.modal` styles (`styles.scss:373-443`) — see `invite-dialog.component.*`, `delete-confirm.component.*`. Reactive forms (`fb.nonNullable.group`, `markAllAsTouched`, `field-error`/`form-error`). Signals for state; services return `Observable<T>` and update signals via `tap`. Minimal-API groups via `Map…Endpoints()` extension methods registered in `Program.cs:177-182`; current user via `principal.FindFirstValue("sub")` and `HouseholdScope.ResolveCallerAsync`. `wwwroot` serves the built SPA only and is overwritten on deploy. Design tokens in `styles.scss:13-87`.
- **Templates**: `templates/Homdutio Auth Pro.html` (the three join screens) and `templates/Homdutio Pro.html` (avatar-menu Settings item + Settings modal) are the visual source of truth for this work.

## Desired End State

- A logged-out invitee at `/join/<token>` sees a branded landing naming the inviter and household with "Log in to continue" / "No account? Create one". A logged-in invitee with no household sees the inviter's avatar + a house badge and "Accept & join {household}" / "No thanks, maybe later". A logged-in invitee who already has a household sees a calm (non-error) "You're already in" + "Go to your board".
- The avatar menu has a "Settings" item above "Log out". Settings opens a modal to edit display name and upload (zoom/crop) or remove a profile photo with live preview.
- A user's photo and current display name appear everywhere they're shown: header, avatar menu, task cards, comments, members list, and the join inviter badge. Renaming updates existing cards/comments on the next fetch; removing a photo falls back to the colored initial.

**Verification**: build + lint + typecheck clean; new xUnit endpoint tests and Angular specs pass; manual walk of the invite→join journey (all three states) and the Settings flow (rename + upload + remove) shows correct rendering across all surfaces.

### Key Discoveries

- Display names are **never denormalized** — fetch-time resolution means rename propagation is free (`TaskEndpoints.cs:555-572`, members join at `HouseholdEndpoints.cs:242-262`).
- The inviter is already recorded (`HouseholdInvite.CreatedById`), so the preview can name them without a schema change.
- The `409`-on-accept "already a member" branch already exists; the `joinTaken` screen is detectable on load from the cached membership signal (`household.service.ts current/loaded`).
- `wwwroot` is wiped on deploy → file-on-disk avatar storage is unsafe; SQL-backed bytes + a cached endpoint avoids new infra (Azure SQL + managed identity already in place).
- `<img>` requests don't pass through the bearer interceptor, so the avatar GET endpoint must be `AllowAnonymous` (mirrors the public invite preview); user ids are GUIDs.

## What We're NOT Doing

- No Azure Blob Storage / CDN for avatars (SQL-backed bytes instead).
- No server-side image processing/resizing (the client crops + resizes before upload).
- No rewrite of historical text — nothing bakes names into stored text, so there's nothing to backfill.
- No change to the invite-mint or accept endpoints' behavior (only the preview response grows).
- No multi-household support; "already a member" = "already has a household" (single-household model is unchanged).
- No real-time push; existing 4s board polling carries name/avatar updates to other users.
- No gravatar/external avatar sources.

## Implementation Approach

Phase 1 ships the join redesign with a new reusable `UserAvatarComponent` that renders an `<img>` when an avatar URL is present and the existing colored initial otherwise — so every later surface swaps to it once and "lights up" when Phase 3 lands. Phase 2 adds display-name editing and promotes DisplayName + avatar into the current-user surface. Phase 3 adds the avatar column + migration, upload/serve/delete endpoints, the versioned `avatarUrl` on all five DTOs, and the ngx-image-cropper UI, then points `UserAvatarComponent` at the real URLs everywhere.

Avatar URLs are **versioned** (`/api/users/{id}/avatar?v=<version>`) so the browser caches aggressively (ETag + `Cache-Control`) and the 4s poll re-uses cached images (304/from-cache); a new upload bumps the version and busts the cache.

## Critical Implementation Details

- **Avatar endpoint must be anonymous.** `<img src>` does not go through `bearerInterceptor`, so `GET /api/users/{id}/avatar` uses `.AllowAnonymous()` like the invite preview. Return `404` when the user has no avatar so the component falls back to the initial.
- **Cache invalidation via version, not mutation of URL shape.** Store an integer/`rowversion`-derived `AvatarVersion` (or `AvatarUpdatedUtc.Ticks`) on the user; every DTO builds `…/avatar?v=<version>`. Without the version query, browsers would serve a stale cached photo after a change.
- **`joinTaken` is decided on load, not on click.** When authenticated, read the cached membership (`households.loadMine()` / `current`) before showing a join button; if a household exists, render `joinTaken` directly rather than letting the user click into a `409`.
- **Avatar upload payload is the already-resized blob.** ngx-image-cropper outputs a cropped, downscaled image (~256×256); the client posts that blob. The endpoint still validates content-type (png/jpeg) and a max byte size as defense-in-depth.

---

## Phase 1: Join-Household Flow Redesign

### Overview

Surface the inviter in the anonymous preview and reshape `/join/:token` into the three designed states, introducing the shared avatar component the rest of the change reuses.

### Changes Required:

#### 1. Invite preview returns the inviter

**File**: `src/Homdutio.Api/Households/HouseholdEndpoints.cs`

**Intent**: The logged-out and logged-in join screens name the inviter and (later) show their photo. Resolve the inviter from the invite's `CreatedById` and add them to the preview response.

**Contract**: Extend `InvitePreviewResponse` to `{ householdName, inviterName, inviterId }` (`inviterId` lets the client build the versioned avatar URL once Phase 3 lands; until then the avatar component renders initials from `inviterName`). The `GET /api/households/invites/{token}` handler resolves `inviterName` via `db.Users` lookup on `CreatedById`. Endpoint stays `AllowAnonymous`; `404`/`410` behavior unchanged.

#### 2. Shared user-avatar component

**File**: `web/src/app/shared/user-avatar/user-avatar.component.ts` (new)

**Intent**: One component for "show a user's avatar" used on every surface, so Phase 3 wiring is a single substitution. Renders an `<img>` when given an avatar URL, otherwise the existing colored-initial glyph.

**Contract**: Inputs `name: string | null` and `avatarUrl?: string | null`, plus a size input (e.g. `sm`/`md`/`lg` or a px number). Uses `avatarColor(name)` from `web/src/app/board/tag-color.ts` and the first-initial logic already in the cards/members components. `<img>` failure (`onerror`) falls back to the initial.

#### 3. Join page reshaped into three states

**Files**: `web/src/app/join/join.component.ts`, `join.component.html`, `join.component.scss`; `web/src/app/household/invite.service.ts` (preview model)

**Intent**: Replace the current utilitarian branches with the three designed screens, driven by data (auth + membership + preview), matching `templates/Homdutio Auth Pro.html`.

**Contract**: A computed `screen` selects one of `joinLoggedOut | join | joinTaken | invalid | loading`:
- not authenticated → `joinLoggedOut` (inviter + household; "Log in to continue" → `/login?returnUrl=/join/<token>`; "No account? Create one" → `/register?returnUrl=…`).
- authenticated + no household → `join` (inviter avatar + house badge; "Accept & join {household}" → existing `accept()` flow; "No thanks, maybe later" → `/` or `/board`).
- authenticated + has household → `joinTaken` (calm, non-error; "You're already in" + "Go to your board" → `/board`).
- preview `404`/`410` → `invalid` (existing).
Update `InvitePreview` model + `invite.service.preview()` typing to include `inviterName`/`inviterId`. Reuse `UserAvatarComponent` for the inviter; the page uses the shared auth layout/logo where the template does.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build`
- Preview-inviter endpoint test passes: `dotnet test --filter FullyQualifiedName~HouseholdInviteEndpointsTests`
- Frontend builds: `npm --prefix web run build`
- Lint passes: `npm --prefix web run lint`
- Join-component spec passes: `npm --prefix web test -- --include='**/join.component.spec.ts'`

#### Manual Verification:

- Logged-out `/join/<valid-token>` shows inviter + household and both auth links carry the `returnUrl`.
- After logging in via that link, the page shows the `join` screen and "Accept & join" joins and routes to `/board`.
- A user who already has a household sees the calm `joinTaken` screen (no red/error styling), and "Go to your board" works.
- Expired/consumed/unknown token still shows the invalid state.

**Implementation Note**: After automated verification passes, pause for human confirmation of the manual walk before starting Phase 2.

---

## Phase 2: Display-Name Editing + Identity Surfacing

### Overview

Add the Settings modal (display name only for now) and promote DisplayName + avatar into the current-user surface (header/menu), extending `/api/auth/me`.

### Changes Required:

#### 1. Profile update endpoint

**File**: `src/Homdutio.Api/Profile/ProfileEndpoints.cs` (new); register in `src/Homdutio.Api/Program.cs`

**Intent**: Let a signed-in user change their display name; it propagates everywhere via existing fetch-time name resolution.

**Contract**: `PUT /api/profile/me` (group `RequireAuthorization()`), body `{ displayName }`. Validates non-empty + trimmed + a sane max length; loads the user via `sub` claim (`db.Users.FindAsync`); sets `DisplayName`; `SaveChangesAsync`. Returns `200` with the updated profile DTO; `Results.ValidationProblem` on blank/too-long. Mirrors the minimal-API + validation pattern in `TaskEndpoints`/`AuthEndpoints`.

#### 2. Current-user surface includes DisplayName (+ avatar ref)

**File**: `src/Homdutio.Api/Auth/AuthEndpoints.cs`

**Intent**: The header/menu and Settings prefill need the real display name (and, from Phase 3, the avatar URL) — `/me` returns only `sub`+`email` today.

**Contract**: Extend `MeResponse` to `{ sub, email, displayName, avatarUrl? }`. The `/api/auth/me` handler resolves `displayName` from the user record (it's no longer derivable from claims alone, so load the user or add a `display_name` claim at token issue — prefer reading the user record to avoid token-staleness after a rename). `avatarUrl` is `null` until Phase 3.

#### 3. Settings dialog (display-name field) + menu item

**Files**: `web/src/app/shell/settings-dialog/settings-dialog.component.*` (new); `web/src/app/shell/topbar/avatar-menu.component.html` + `.ts`; `web/src/app/profile/profile.service.ts` (new)

**Intent**: A "Settings" entry above "Log out" opens a CDK dialog to edit the display name, following the existing dialog/form conventions.

**Contract**: `ProfileService.updateProfile(displayName)` → `PUT /api/profile/me`, updates the current-user signal via `tap`. `SettingsDialogComponent` uses `.modal` markup, a reactive `displayName` control (required, maxlength), `pending`/`error` signals, `field-error`/`form-error` display, and `dialogRef.close()`. Avatar-menu gains an `openSettings()` opening it via `Dialog.open`.

#### 4. Current-user profile state + header/menu show DisplayName + avatar

**Files**: `web/src/app/auth/auth.service.ts` (or a new `current-user` store), `web/src/app/shell/topbar/avatar-menu.component.*`, `web/src/app/shell/topbar/topbar.component.*`

**Intent**: Hold the signed-in user's DisplayName (+ avatar URL) reactively so header/menu render it and updates after a rename are immediate; demote email to a secondary line.

**Contract**: Add a `displayName` (and `avatarUrl`) signal sourced from `/api/auth/me` at session restore/login and updated by `ProfileService`. Header/menu render `UserAvatarComponent` + DisplayName; email moves to a secondary menu line. `clearOnLogout` resets the new state alongside the existing token/household/task clears.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build`
- Profile + `/me` tests pass: `dotnet test --filter FullyQualifiedName~ProfileEndpointsTests` and the updated `AuthEndpointsTests`
- Frontend builds + lint: `npm --prefix web run build` && `npm --prefix web run lint`
- Settings dialog spec passes: `npm --prefix web test -- --include='**/settings-dialog.component.spec.ts'`

#### Manual Verification:

- "Settings" appears above "Log out"; opening shows the current display name prefilled.
- Saving a new name closes the modal; the header/menu update immediately and the board's next refresh shows the new name on the user's cards/comments.
- Blank/too-long names show inline validation and don't submit.
- The header/menu now show DisplayName (not email); email is still visible as secondary text.

**Implementation Note**: After automated verification passes, pause for human confirmation before starting Phase 3.

---

## Phase 3: Profile Photo Upload, Storage, Serving & Avatars Everywhere

### Overview

Add SQL-backed avatar storage + migration, upload/serve/delete endpoints, a versioned `avatarUrl` on all five DTOs, and the ngx-image-cropper UI; then render real avatars on every surface via `UserAvatarComponent`.

### Changes Required:

#### 1. Avatar columns on ApplicationUser + migration

**Files**: `src/Homdutio.Data/Entities/ApplicationUser.cs`, `src/Homdutio.Data/ApplicationDbContext.cs` (config if needed), new EF migration under `src/Homdutio.Data/Migrations`

**Intent**: Persist the cropped image bytes and enough metadata to serve + cache-bust it.

**Contract**: Add `AvatarData byte[]?`, `AvatarContentType string?`, and a version signal (`AvatarVersion int` defaulting `0`, or `AvatarUpdatedUtc DateTime?`). Generate the migration; it applies cleanly. Bytes are bounded by the client-side resize + the endpoint's max-size guard.

#### 2. Avatar upload / remove / serve endpoints

**File**: `src/Homdutio.Api/Profile/ProfileEndpoints.cs`; serving endpoint may live in a small `UserAvatarEndpoints` or the profile group

**Intent**: Store, clear, and serve the photo; serving must be reachable by a bare `<img>` tag.

**Contract**:
- `PUT /api/profile/me/avatar` (auth): accepts the resized image (raw body or multipart). Validates content-type ∈ {image/png, image/jpeg} and byte length ≤ a cap; stores bytes + content-type, bumps the version. Returns the new `avatarUrl`.
- `DELETE /api/profile/me/avatar` (auth): clears the columns, bumps version. `204`.
- `GET /api/users/{id}/avatar` (**AllowAnonymous**): returns the bytes with the stored content-type, an `ETag` from the version, and `Cache-Control`. `404` when no avatar. Honors `If-None-Match` → `304`.

#### 3. Versioned avatarUrl on all five DTOs

**Files**: `src/Homdutio.Api/Auth/AuthEndpoints.cs` (`MeResponse`), `src/Homdutio.Api/Tasks/TaskEndpoints.cs` (`TaskResponse` creator+claimer, `CommentResponse` author), `src/Homdutio.Api/Households/HouseholdEndpoints.cs` (`MemberResponse`, `InvitePreviewResponse` inviter)

**Intent**: Every place a user is named also carries their avatar URL so the client renders the photo.

**Contract**: Add nullable `…AvatarUrl` fields built as `"/api/users/{id}/avatar?v={version}"` — `null` when the user has no avatar. Reuse one helper to build the URL. Name-resolution queries that today fetch `DisplayName` also fetch the avatar version (extend the `ResolveNamesAsync`/roster/comment projections to select id + version). New fields are additive/optional (older clients ignore them).

#### 4. Cropper upload UI in Settings + remove

**Files**: `web/src/app/shell/settings-dialog/settings-dialog.component.*`, `web/package.json` (add `ngx-image-cropper`), `web/src/app/profile/profile.service.ts`

**Intent**: Upload → zoom/crop → preview → save, plus a "Remove photo" action.

**Contract**: Add `ngx-image-cropper`; file input → cropper (square aspect, round preview) → output a downscaled (~256×256) blob → `ProfileService.uploadAvatar(blob)` → `PUT /api/profile/me/avatar`; "Remove" → `DELETE`. Both update the current-user `avatarUrl` signal (with the new version) via `tap`.

#### 5. Render avatars on every surface

**Files**: `web/src/app/board/task-card/task-card.component.*`, `web/src/app/board/comments/comments.component.*`, `web/src/app/household/members/members.component.*`, `web/src/app/shell/topbar/avatar-menu.component.*` + `topbar.component.*`, `web/src/app/join/join.component.*`; TS models for task/comment/member/me/preview

**Intent**: Swap initial-only glyphs for `UserAvatarComponent` fed the new `avatarUrl`s, including the comments author (which has no glyph today) and the join inviter badge.

**Contract**: Each surface passes `name` + `avatarUrl` to `UserAvatarComponent`. Extend the corresponding TS interfaces with optional `…AvatarUrl`. Initials/colors remain the fallback when `avatarUrl` is null.

### Success Criteria:

#### Automated Verification:

- Migration applies: `dotnet ef database update` (against a dev/test DB) or the test host's migrate step
- Backend builds + tests: `dotnet build` && `dotnet test --filter FullyQualifiedName~ProfileEndpointsTests`
- Frontend builds + lint: `npm --prefix web run build` && `npm --prefix web run lint`
- Avatar/cropper specs pass: `npm --prefix web test -- --include='**/{settings-dialog,user-avatar}.component.spec.ts'`

#### Manual Verification:

- Uploading a photo with zoom/crop shows a live preview and, after save, the avatar appears in the header, menu, the user's cards, their comments, and the members list.
- Removing the photo reverts every surface to the colored initial.
- A second user (or the 4s poll) sees the updated photo on shared cards/comments.
- The join `join` screen shows the inviter's real photo (initials if they have none).
- Re-opening the app shows the photo from cache (no flicker); a fresh upload busts the cache (new version).

**Implementation Note**: After automated verification passes, pause for human confirmation of the full cross-surface walk.

---

## Testing Strategy

### Unit / Endpoint Tests (xUnit):

- Invite preview returns `inviterName`/`inviterId`; still `404`/`410` for unknown/expired; still anonymous.
- `PUT /api/profile/me`: updates DisplayName; rejects blank/too-long; `/api/auth/me` reflects the new name.
- Avatar: `PUT` stores + validates content-type/size; `DELETE` clears; `GET /api/users/{id}/avatar` returns bytes anonymously with ETag, `404` when none, `304` on `If-None-Match`.
- DTO projections include `avatarUrl` (versioned) for a user with an avatar and `null` for one without.

### Component Tests (Angular):

- `join.component`: `screen` selection across not-auth / auth-no-household / auth-has-household / invalid.
- `user-avatar.component`: renders `<img>` with URL, initial without, and falls back on image error.
- `settings-dialog.component`: name validation + submit; avatar upload + remove call the service and update state.

### Manual Testing Steps:

1. Walk the invite→join journey for all three states (logged-out, logged-in joinable, already-member).
2. Edit display name; confirm header/menu + existing cards/comments update.
3. Upload+crop a photo; confirm it appears on all surfaces; remove it; confirm fallback to initials.
4. Verify a second account sees the new name/photo on shared cards within one poll cycle.

## Performance Considerations

Avatar images are small (client-resized ~256²) and served with `ETag` + `Cache-Control`, so the 4s board poll re-uses cached bytes (304/from-cache) rather than refetching. Avatar bytes in SQL are bounded by the upload size cap. DTO payload growth is one short URL string per user reference.

## Migration Notes

One additive EF migration adds nullable avatar columns to `AspNetUsers`; no backfill (existing users simply have no avatar and render initials). New DTO fields are optional, so no client breaks during a rolling deploy.

## References

- Change identity: `context/changes/join-flow-and-user-settings/change.md`
- Plan brief: `context/changes/join-flow-and-user-settings/plan-brief.md`
- Invite/join: `src/Homdutio.Api/Households/HouseholdEndpoints.cs:86-228`, `web/src/app/join/join.component.*`
- Name resolution (free rename propagation): `src/Homdutio.Api/Tasks/TaskEndpoints.cs:555-572`
- Identity model: `src/Homdutio.Data/Entities/ApplicationUser.cs`, `HouseholdInvite.cs:26`
- Conventions: dialogs `web/src/app/shell/topbar/invite-dialog.component.*`; tokens `web/src/styles.scss:13-87`; avatar color `web/src/app/board/tag-color.ts:48-51`
- Design source: `templates/Homdutio Auth Pro.html`, `templates/Homdutio Pro.html`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Join-Household Flow Redesign

#### Automated

- [x] 1.1 Backend builds: `dotnet build`
- [x] 1.2 Preview-inviter endpoint test passes
- [x] 1.3 Frontend builds: `npm --prefix web run build`
- [x] 1.4 Lint passes: `npm --prefix web run lint`
- [x] 1.5 Join-component spec passes

#### Manual

- [x] 1.6 Logged-out `/join` shows inviter + household; auth links carry `returnUrl`
- [x] 1.7 Logged-in joinable: "Accept & join" joins and routes to `/board`
- [x] 1.8 Already-member shows calm `joinTaken` (non-error) + working "Go to your board"
- [x] 1.9 Expired/consumed/unknown token still shows invalid state

### Phase 2: Display-Name Editing + Identity Surfacing

#### Automated

- [ ] 2.1 Backend builds: `dotnet build`
- [ ] 2.2 Profile + `/me` tests pass
- [ ] 2.3 Frontend builds + lint
- [ ] 2.4 Settings dialog spec passes

#### Manual

- [ ] 2.5 "Settings" appears above "Log out"; opens with name prefilled
- [ ] 2.6 Saving a name updates header/menu immediately and cards/comments on next refresh
- [ ] 2.7 Blank/too-long names show inline validation and don't submit
- [ ] 2.8 Header/menu show DisplayName; email demoted to secondary text

### Phase 3: Profile Photo Upload, Storage, Serving & Avatars Everywhere

#### Automated

- [ ] 3.1 Migration applies cleanly
- [ ] 3.2 Backend builds + Profile/avatar tests pass
- [ ] 3.3 Frontend builds + lint
- [ ] 3.4 Avatar/cropper specs pass

#### Manual

- [ ] 3.5 Upload+crop shows preview; avatar appears on header, menu, cards, comments, members
- [ ] 3.6 Remove reverts every surface to the colored initial
- [ ] 3.7 Second user / next poll sees updated photo on shared cards/comments
- [ ] 3.8 Join `join` screen shows inviter's real photo (initials if none)
- [ ] 3.9 Cached photo on reload (no flicker); fresh upload busts cache
