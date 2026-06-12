# Member Administration Implementation Plan

## Overview

Deliver roadmap slice **S-09 (FR-008, FR-009)**: a household admin can view the member roster and **promote** an adult member to admin, **demote** an admin back to member, and **remove** a member from the household. The slice spans both tiers — three new endpoints on the existing `HouseholdEndpoints` group plus a new `/members` page hanging off the S-11 app shell — and requires **no schema change**: role is already persisted as a string (a one-field flip) and removal deletes the membership row.

## Current State Analysis

The membership domain and the authorization patterns this slice needs are already in place:

- **`HouseholdMember`** is the join row carrying `Role` (`HouseholdRole` enum: `Admin` / `Member`), with a **unique index on `UserId`** enforcing one-household-per-user (FR-007). The entity doc-comment explicitly anticipates this slice: *"S-09 updates `Role` to promote/demote."* (`src/Homdutio.Data/Entities/HouseholdMember.cs`).
- **`HouseholdRole` is stored as a string** (`HasConversion<string>()`, `ApplicationDbContext.cs:56-57`), so promote/demote is a value update with no numeric remap and **no migration**.
- **The admin-authorization pattern is canonical** across `TaskEndpoints.cs`: resolve the caller from the JWT `sub` claim → server-derive the household (never trust a client id) → `caller.Role != HouseholdRole.Admin` returns `Results.Forbid()` (e.g. `TaskEndpoints.cs:175`, `:258`, `:317`). The `ResolveMemberAsync` helper returns a `CallerContext(HouseholdId, Role, UserId)`.
- **`HouseholdEndpoints.cs`** already hosts the `/api/households` group (`.RequireAuthorization()`) with `/me`, create, and invite endpoints — but **no member-list, promote, or remove endpoint**. This is where the new routes belong.
- **Task attribution is FK-free**: `CreatedById` / `ClaimedById` / `ConfirmedById` are raw `AspNetUsers.Id` columns with **no FK to `HouseholdMember`** (`ApplicationDbContext.cs:86-88`). So deleting a membership row leaves the audit record intact (NFR-3) — but an in-progress task the removed member *claimed* would be left with an absent claimer unless explicitly handled.
- **The unclaim transition exists** (S-05): `POST /api/tasks/{id}/unclaim` flips an in-progress task to To do, clears `ClaimedById`/`ClaimedAtUtc`, reorders to the column bottom, and appends a `TaskEventType.Unclaimed` event (`TaskEndpoints.cs:203-240`). The removal sweep reuses this exact mutation shape.
- **The SPA shell has reserved slots**: `app.routes.ts` defines the guarded shell with `board` as its only child; the sidebar (`sidebar.component.html`) has Home + Tasks (both → `/board`) and **no Members item**; `avatar-menu.component.ts:7` notes it is *"the future home for S-09 account/settings items."* The S-11 design brief explicitly planned a **"Members" sidebar nav**.
- **`HouseholdService`** already exposes the caller's `role: 'Admin' | 'Member'` from `GET /api/households/me` — the SPA can gate admin-only controls on it without a new identity call. The caller's own `userId` is the JWT `sub`; the member roster response will carry an `isSelf` flag computed server-side so the SPA needn't parse the token.

### Key Discoveries:

- Promote/demote is a string flip — **no migration** (`HouseholdMember.cs:5-6`, `ApplicationDbContext.cs:56-57`).
- Mirror the `Forbid()` admin-gate + server-derived scope from `TaskEndpoints.cs:175`.
- Removal preserves attribution because attribution columns have no FK to membership (`ApplicationDbContext.cs:86-88`); only the in-progress *claim* needs sweeping.
- Reuse the S-05 unclaim mutation (`TaskEndpoints.cs:228-234`) for the removed-member task sweep — same status flip, claim clear, reorder, and `Unclaimed` event.
- The `delete-confirm` component (`web/src/app/board/delete-confirm/`) is the confirm-dialog pattern to reuse for the destructive remove.

## Desired End State

An admin opening **Members** in the sidebar sees every household member with their name, email, and role. For each *other* member the admin can promote/demote (role flips immediately) or remove (after a confirmation dialog); a removed member loses their membership, their in-progress tasks return to To do, and their closed-task audit record is untouched. A non-admin opening the same page sees the roster read-only. The server refuses any action that would leave the household with zero admins, and an admin cannot act on their own row.

**Verification:** Promote a member → their role badge flips to Admin and they gain confirm/admin powers on the board. Demote them back → badge returns to Member. Remove a member who has an in-progress task → they vanish from the roster and that task reappears in To do unassigned. Attempt to demote/remove the last admin → blocked with a clear message. Attempt to act on your own row → no controls offered (and the API rejects it).

## What We're NOT Doing

- **No "leave household" / self-removal flow** — an admin cannot remove or demote themselves this slice (a self-managed leave path is a separate future feature).
- **No membership audit log** — promote/demote/remove emit no new audit entity; the `TaskEvent` log stays task-scoped. (The removal *task-unclaim* sweep does emit the existing `Unclaimed` events, as that is a task transition.)
- **No background polling of the roster** — fetch on load + refetch after the caller's own action; a concurrent second admin's change needs a manual reload.
- **No schema/migration change** — role flip + row delete only.
- **No invite/re-invite changes** — a removed user simply has no household and can be re-invited through the existing S-06 flow unchanged.
- **No demote-confirmation dialog** — only the irreversible *remove* is confirmed; role flips are immediate and reversible.
- **No general settings surface** — only the Members page; other avatar-menu/settings items stay out of scope.

## Implementation Approach

Two phases, backend then frontend, mirroring how prior slices landed. Phase 1 adds the roster-read and two mutating endpoints to the existing `HouseholdEndpoints` group, enforcing all guards server-side and proving them with xUnit integration tests. Phase 2 builds the `/members` page, sidebar nav, and a `MemberService`, gating destructive controls on the caller's role and reusing the existing confirm-dialog pattern. The SPA stays "dumb" about authorization: the roster DTO carries server-computed `canManage` / `isSelf` flags (matching how `TaskResponse` ships affordance flags), so the UI renders controls from flags rather than re-deriving rules.

## Critical Implementation Details

**State sequencing (removal).** The removed member's in-progress task sweep and the `HouseholdMember` row delete MUST occur in a **single `SaveChanges`** — the unclaim mutations (status→ToDo, clear claim, reorder, `Unclaimed` event) and the membership delete commit atomically, so a task can never be left claimed by a non-member. Order within the transaction: load the member's in-progress tasks scoped to the household, apply the unclaim mutation to each, append the `Unclaimed` events, remove the membership row, then `SaveChanges` once.

**Last-admin guard timing.** Compute the admin count *before* mutating. A demote or remove targeting an `Admin` row must be rejected with 409 when the household's admin count is exactly 1 (i.e. the target is the last admin). A promote never threatens the invariant. Removing a *Member* never threatens it either — only admin-targeted demote/remove needs the check.

## Phase 1: Backend — member roster + role/remove endpoints

### Overview

Add three endpoints to the `/api/households` group: read the roster (any member), change a member's role (admin-only), and remove a member (admin-only). Enforce self-action blocks, the last-admin guard, and the in-progress-task sweep, all server-side, with integration tests for every branch. No migration.

### Changes Required:

#### 1. Member roster + role/remove endpoints

**File**: `src/Homdutio.Api/Households/HouseholdEndpoints.cs`

**Intent**: Add the three S-09 routes to the existing group, reusing the established caller-resolution + admin-gate + server-derived-scope pattern. The roster lists the caller's household members with display names, emails, and roles, plus per-row `isSelf` and `canManage` flags so the SPA renders controls from flags. Role-change and remove are admin-only and enforce the guardrails.

**Contract**:
- `GET /api/households/members` — any authenticated member of a household. Returns `200` with `MemberResponse[]` (the caller's household roster); the caller with no household → `404` (consistent with the existing no-membership convention). Each row: `userId`, `displayName`, `email`, `role` (`"Admin"`/`"Member"`), `isSelf` (== caller), `canManage` (caller is admin AND not self). Order by role then display name.
- `POST /api/households/members/{userId}/role` with body `{ role: "Admin" | "Member" }` — admin-only (`Forbid` otherwise). Resolves the target within the caller's household (foreign/unknown userId → `404`, no existence leak). Blocks self (`userId` == caller → `403` or `409` — pick `409` with a clear message). Demoting the last admin → `409`. Idempotent: setting the role it already has is a no-op `200`. Returns the updated `MemberResponse`.
- `DELETE /api/households/members/{userId}` — admin-only. Target scoped to caller's household (foreign/unknown → `404`). Self → `409` ("You cannot remove yourself."). Removing the last admin → `409`. On success: in one `SaveChanges`, unclaim the target's in-progress tasks in this household (status→`ToDo`, clear `ClaimedById`/`ClaimedAtUtc`, `SortOrder` = column bottom, append a `TaskEventType.Unclaimed` event per task) **then** remove the `HouseholdMember` row. Returns `204`.
- New DTOs: `MemberResponse(string UserId, string DisplayName, string Email, string Role, bool IsSelf, bool CanManage)` and `UpdateMemberRoleRequest(string Role)`. Resolve display names + emails from `db.Users` in one query (no N+1), mirroring `ResolveNamesAsync` in `TaskEndpoints.cs:517`.

The last-admin count and the in-progress-task sweep are the non-obvious parts — see Critical Implementation Details for transaction ordering. Everything else follows the existing endpoint shape in this file and `TaskEndpoints.cs`.

#### 2. Integration tests

**File**: `tests/Homdutio.Api.Tests/HouseholdMemberAdminTests.cs` (new; place beside the existing endpoint test classes — confirm the actual test project path/namespace during implementation and match it)

**Intent**: Lock every guard branch so the rules can't silently regress.

**Contract**: Cover — roster returns the caller's household members only (foreign household members never appear); non-admin role-change/remove → `403`; promote `Member`→`Admin` and demote `Admin`→`Member` succeed and persist; demote/remove of the last admin → `409`; self role-change and self-remove → `409`; foreign/unknown `userId` → `404`; remove sweeps the target's in-progress task back to `ToDo` unassigned with an `Unclaimed` event while a *closed* task's attribution (`ConfirmedById` etc.) is left intact (NFR-3); caller with no household → `404` on roster.

### Success Criteria:

#### Automated Verification:

- [ ] Solution builds: `dotnet build`
- [ ] Tests pass: `dotnet test`
- [ ] No new migration was generated (role flip + row delete only): `dotnet ef migrations list` shows the same set as before

#### Manual Verification:

- [ ] `GET /api/households/members` returns the roster with correct `isSelf`/`canManage` flags for an admin vs a member token
- [ ] Promote then demote a member round-trips the role; last-admin demote/remove and self-actions are refused with a clear message
- [ ] Removing a member with an in-progress task returns that task to To do unassigned; a previously closed task's audit fields remain unchanged

**Implementation Note**: After Phase 1 automated verification passes, pause for manual confirmation before starting Phase 2.

---

## Phase 2: Frontend — Members page + sidebar nav

### Overview

Add the `/members` route under the app shell, a "Members" sidebar item, and a `MemberService`. Render the roster with role badges; for an admin, show per-row promote/demote (immediate) and remove (confirm dialog). Non-admins see a read-only roster. Refetch the roster after the caller's own action.

### Changes Required:

#### 1. Member service

**File**: `web/src/app/household/member.service.ts` (new)

**Intent**: Wrap the three endpoints, mirroring `InviteService`/`HouseholdService` conventions (typed interface matching the camelCase DTO, `inject(HttpClient)`).

**Contract**: `Member` interface (`userId`, `displayName`, `email`, `role`, `isSelf`, `canManage`); `list(): Observable<Member[]>` → `GET /api/households/members`; `setRole(userId, role): Observable<Member>` → `POST …/members/{userId}/role`; `remove(userId): Observable<void>` → `DELETE …/members/{userId}`.

#### 2. Members page component

**File**: `web/src/app/household/members/members.component.ts` + `.html` (new)

**Intent**: The routed page. Loads the roster on init; renders each member with name, email, and a role badge. For rows where `canManage` is true, shows a promote (if Member) or demote (if Admin) button that calls `setRole` and refetches, and a Remove button that opens the confirm dialog then calls `remove` and refetches. Surfaces the server's `409`/`403` messages (last-admin, self-action) as an inline error. Non-admins / self rows show no action controls. Mobile-first ≤400px (NFR-2).

**Contract**: Standalone component, `inject(MemberService)`. Reuse the `delete-confirm` dialog component (`web/src/app/board/delete-confirm/`) for the remove confirmation, or lift it to a shared location if its current placement couples it to the board — decide during implementation and keep one copy. Map API error bodies via the existing `validation-problem` helper / the `{ message }` shape the endpoints return.

#### 3. Route + sidebar nav

**File**: `web/src/app/app.routes.ts`, `web/src/app/shell/sidebar/sidebar.component.html`

**Intent**: Register `members` as a child of the guarded shell route (alongside `board`) and add a "Members" sidebar item pointing to `/members`, replacing or repurposing one of the two current `/board`-pointing items so the nav reflects real destinations.

**Contract**: New child route `{ path: 'members', loadComponent: … }` under the existing `canActivate: [authGuard, requireHousehold]` shell parent. Sidebar gains a `routerLink="/members"` item with an icon (follow the existing inline-SVG pattern) and a "Members" label.

#### 4. Specs

**File**: `web/src/app/household/member.service.spec.ts`, `web/src/app/household/members/members.component.spec.ts` (new)

**Intent**: Cover the service request shapes and the component's admin-vs-member rendering + action/refetch flow, matching the existing vitest conventions.

**Contract**: Service specs assert the three request URLs/methods/bodies. Component specs assert: admin sees promote/demote/remove on other rows but not on self; non-admin sees a read-only roster; a successful action triggers a refetch; a `409` surfaces the server message.

### Success Criteria:

#### Automated Verification:

- [ ] SPA builds: `npm run build` (in `web/`)
- [ ] Unit tests pass: `npm test` (in `web/`)
- [ ] Lint passes: `npm run lint` (in `web/`)

#### Manual Verification:

- [ ] "Members" appears in the sidebar and routes to a roster of the household's members
- [ ] As an admin: promote/demote flips a member's badge immediately; Remove shows a confirm dialog, and confirming drops the member from the list
- [ ] Last-admin demote/remove and any self-action are prevented, with the server's message shown inline
- [ ] As a non-admin: the roster is visible but read-only (no action controls)
- [ ] The page has no horizontal scroll at ≤400px (NFR-2)

**Implementation Note**: After Phase 2 automated verification passes, pause for manual confirmation.

---

## Testing Strategy

### Unit Tests:
- Backend (xUnit): every guard branch (admin-gate, self-block, last-admin, foreign-id 404, idempotent role set) and the removal task-sweep + attribution-preservation invariant.
- Frontend (vitest): service request shapes; component admin-vs-member rendering and action/refetch behaviour.

### Integration Tests:
- The remove → unclaim sweep verified end-to-end at the API: a removed member's in-progress task is in `ToDo` unassigned with an `Unclaimed` event, while a closed task's audit fields are unchanged.

### Manual Testing Steps:
1. With two accounts in one household (use the S-06 invite flow), open Members as the admin.
2. Promote the member → confirm they can now confirm tasks on the board; demote → confirm the power is gone.
3. Have the member claim a task (In progress), then remove the member → confirm the task returns to To do unassigned.
4. Try to remove/demote yourself and the last admin → confirm both are blocked with a clear message.
5. Log in as the (re-)added member and confirm the roster is read-only.
6. Check the page at ≤400px for no horizontal scroll.

## Performance Considerations

Single-household, low-volume; the roster is a small scoped query with a one-shot name/email resolve (no N+1). The removal sweep touches only the target's in-progress tasks (typically ≤ a handful) in one transaction. No polling. No indexing changes needed — the `HouseholdMember.UserId` unique index and `HouseholdTask.HouseholdId` index already back these queries.

## Migration Notes

None. No schema change — promote/demote updates `HouseholdMember.Role` (already a string column) and removal deletes a `HouseholdMember` row. Confirm `dotnet ef migrations list` is unchanged as a Phase 1 success check.

## References

- Roadmap slice: `context/foundation/roadmap.md` (S-09, FR-008/FR-009)
- Admin-gate + server-scope pattern: `src/Homdutio.Api/Tasks/TaskEndpoints.cs:175`, `:497-510`
- Unclaim transition to reuse for the sweep: `src/Homdutio.Api/Tasks/TaskEndpoints.cs:203-240`
- Membership entity + role-as-string config: `src/Homdutio.Data/Entities/HouseholdMember.cs`, `src/Homdutio.Data/ApplicationDbContext.cs:50-68`
- Existing endpoint group to extend: `src/Homdutio.Api/Households/HouseholdEndpoints.cs`
- Confirm-dialog pattern to reuse: `web/src/app/board/delete-confirm/`
- Shell route + sidebar: `web/src/app/app.routes.ts`, `web/src/app/shell/sidebar/sidebar.component.html`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend — member roster + role/remove endpoints

#### Automated

- [x] 1.1 Solution builds: `dotnet build`
- [x] 1.2 Tests pass: `dotnet test`
- [x] 1.3 No new migration generated: `dotnet ef migrations list` unchanged

#### Manual

- [x] 1.4 Roster returns correct `isSelf`/`canManage` flags for admin vs member token
- [x] 1.5 Promote/demote round-trips; last-admin and self-actions refused with clear message
- [x] 1.6 Removing a member with an in-progress task returns it to To do; closed-task audit unchanged

### Phase 2: Frontend — Members page + sidebar nav

#### Automated

- [ ] 2.1 SPA builds: `npm run build`
- [ ] 2.2 Unit tests pass: `npm test`
- [ ] 2.3 Lint passes: `npm run lint`

#### Manual

- [ ] 2.4 "Members" sidebar item routes to the household roster
- [ ] 2.5 Admin can promote/demote (immediate) and remove (confirm dialog drops the member)
- [ ] 2.6 Last-admin and self-actions prevented with the server message shown inline
- [ ] 2.7 Non-admin sees a read-only roster
- [ ] 2.8 No horizontal scroll at ≤400px (NFR-2)
