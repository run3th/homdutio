# Household Data Isolation Implementation Plan

## Overview

S-07 hardens and *proves* Homdutio's single most important guardrail: **no member of one
household may ever view, mutate, or infer the existence of another household's data** (US-02,
FR-019 — "failure here is the worst possible bug"). The cross-household boundary already exists
and is enforced uniformly at every endpoint; this slice does not invent it. It instead (1) makes
the guarantee **certain** via an exhaustive, both-directions isolation test sweep, and (2) makes
it **drift-resistant** by collapsing the duplicated scoping logic into one shared helper and adding
a route-coverage convention guard that fails the build when a new endpoint isn't categorized.

Backend-only. No data-model change, no migration, no user-facing feature. The deliverable is
confidence plus a structural backstop against future regressions.

## Current State Analysis

Isolation is **already implemented and substantially tested**, per direct reading of the source:

- **Tasks** (`src/Homdutio.Api/Tasks/TaskEndpoints.cs`): every handler resolves the caller's
  household server-side from the JWT `sub` claim via `ResolveMemberAsync` (never a client-supplied
  id), and `LoadScopedTaskAsync` (line 513) scopes by `t.Id == id && t.HouseholdId == caller.HouseholdId`
  → a foreign or missing id returns `404` with no existence leak. `PUT /api/tasks/order` rejects the
  whole request (404, no partial reindex) if any supplied id is foreign or wrong-status.
- **Households / members / invites** (`src/Homdutio.Api/Households/HouseholdEndpoints.cs`): `ResolveCallerAsync`
  derives the household server-side; roster (`GET /members`), role-change, and remove all scope the
  target to `caller.HouseholdId` (foreign/unknown → 404). Invites are token-scoped with a `rowversion`
  single-use guard; preview is the one deliberately-anonymous endpoint (leaks only the household name).
- **Existing cross-household tests** already cover foreign-household `404` for: task claim, reorder
  (+ no partial reindex), edit, delete, unclaim, sendback, comments (post + list); roster scoping
  (a foreign member never appears); role/remove foreign-user; and invite token scoping.

### Key Discoveries

- **Two parallel, near-identical scoping helpers exist** — `ResolveMemberAsync` +
  `LoadScopedTaskAsync` in `TaskEndpoints.cs:497,513` and `ResolveCallerAsync` in
  `HouseholdEndpoints.cs:372`. Both build the same `CallerContext(HouseholdId, Role, UserId)` record
  (declared privately in *each* file). This duplication is exactly the drift vector S-07 exists to kill.
- **Verification gaps**: `POST /api/tasks/{id}/done` and `POST /api/tasks/{id}/confirm` go through
  `LoadScopedTaskAsync` (so they *are* protected) but have **no** explicit foreign-household 404 test.
- **Coverage is scattered** across four feature-named test files; there is no single fixture that
  proves the entire boundary, so "be certain" is not currently demonstrable in one place.
- **Body-shape parity is untested**: a foreign-id 404 and an unknown-id 404 must be byte-identical
  so the status code is not an existence oracle. `LoadScopedTaskAsync` returns the same `Results.NotFound()`
  in both cases, so this already holds — but nothing locks it.
- **Route enumeration is feasible**: the host exposes `Program` as `public partial class Program`
  (`Program.cs:107`) and tests already drive it via `WebApplicationFactory<Program>` (`AuthApiFactory`).
  `IEndpointRouteBuilder`/`EndpointDataSource` can be read from the test host's services to enumerate
  registered routes for the convention guard.
- **Test infra is ready**: `AuthApiFactory` spins a throwaway LocalDB per run; the House A / House B
  + register→login→create-household→bearer pattern is established in every existing test file.

## Desired End State

- A single `HouseholdIsolationTests` fixture exercises **every** household-scoped endpoint from a
  foreign household and asserts the leak is sealed (404 / empty payload), including the previously
  untested `done` and `confirm`, with explicit body-shape-parity assertions.
- The two duplicated scoping helpers are replaced by **one** shared internal helper that both
  endpoint files route through, with all existing 404/403/409/200 behavior preserved exactly.
- A route-coverage convention test enumerates the live `/api/tasks` + `/api/households` routes and
  fails if any route is neither marked scoped-and-covered nor explicitly exempted — so a future
  endpoint that forgets isolation cannot pass CI silently.
- Verify: `dotnet test` green (all existing + new tests); the convention test fails if a dummy
  unlisted route is added (demonstrated once during development, then reverted).

## What We're NOT Doing

- **No EF Core global query filters.** Defense-in-depth via `HasQueryFilter` keyed on an injected
  household context was considered and rejected for this slice (cross-cutting DbContext change with
  real pitfalls on the anonymous-preview and create-household-before-membership paths). The shared
  helper + convention guard is the chosen backstop.
- **No frontend change.** The SPA renders only what the scoped API returns; US-02's acceptance
  criteria are entirely backend.
- **No observability/leak logging or metrics.** Out of scope per PRD (no observability in v1).
- **No timing side-channel hardening.** Flaky and disproportionate for a single-household MVP.
- **No new data model, migration, or behavior change to any endpoint.** Phase 2 is a pure refactor.
- **No re-architecting of the invite-preview anonymous path.** It stays the one documented exemption.

## Implementation Approach

Three phases, ordered by the agreed cut line (must-have first, cuttable last):

1. Write the consolidated isolation sweep **first** — it both delivers the certainty outcome and
   becomes the regression net that guards the Phase 2 refactor.
2. Extract the shared scoping helper, relying on Phase 1 + existing tests to prove no behavior changed.
3. Add the route-coverage convention guard so future endpoints are forced into the categorization.

## Critical Implementation Details

- **Body-shape parity is the subtle assertion.** "Indistinguishable" means a foreign-household
  request and a never-existed-id request return the same status *and* the same response body. The
  endpoints already return bare `Results.NotFound()` (empty body) in both cases — the test must
  assert the body is empty/identical, not merely that the status is 404, so a future change that
  adds a distinguishing message (e.g. "this task belongs to another household") is caught.
- **The convention guard enforces categorization, not query correctness.** It asserts the *set* of
  discovered routes equals a known set partitioned into `scoped` (must be hit by the sweep) and
  `exempt` (anonymous/non-household — e.g. `GET /api/households/invites/{token}` preview). A new
  route makes the set mismatch and fails the test, forcing the author to categorize it. The sweep
  itself proves each scoped route's filter is correct; the guard proves none was forgotten.

## Phase 1: Consolidated isolation sweep + gap fill

### Overview

Create one authoritative test fixture that proves the entire cross-household boundary from a
foreign household, filling the `done`/`confirm` gaps and locking body-shape parity. Must-have;
also the regression net for Phase 2.

### Changes Required

#### 1. New isolation test fixture

**File**: `tests/Homdutio.Api.Tests/HouseholdIsolationTests.cs` (new)

**Intent**: A single `IClassFixture<AuthApiFactory>` test class that sets up two fully-populated
households (House A with a seeded task in each lifecycle state + comments + members + a live invite;
House B as the foreign caller) and drives every household-scoped endpoint as a House B member against
House A's ids, asserting the leak is sealed. This is the canonical "be certain" artifact.

**Contract**: Reuses the established helpers (`RegisterAndLoginAsync`, `Authed`, `CreateHouseholdAsync`,
`SeedMemberAsync`, `NewEmail`) — copy them in following the existing per-file convention (the test
project does not share a base class). Endpoints to exercise from the foreign household, each expecting
the documented seal:

- `GET /api/tasks` → House A's tasks never appear (empty/own-only board).
- `POST /api/tasks/{id}/claim` `/done` `/confirm` `/unclaim` `/sendback` → `404` (seed House A task in
  the state each transition requires; **`done` and `confirm` are the new gap-filling cases**).
- `PUT /api/tasks/{id}` (edit), `DELETE /api/tasks/{id}` → `404`.
- `PUT /api/tasks/order` with a House A id → `404`, House B order unchanged.
- `POST /api/tasks/{id}/comments`, `GET /api/tasks/{id}/comments` → `404`.
- `GET /api/households/members` → only House B's roster; no House A member.
- `POST /api/households/members/{userId}/role`, `DELETE /api/households/members/{userId}` targeting a
  House A member → `404`.

#### 2. Body-shape parity assertions

**File**: `tests/Homdutio.Api.Tests/HouseholdIsolationTests.cs`

**Intent**: For the representative scoped-by-id task routes, assert a foreign-household-id response is
indistinguishable from a never-existed-id (`Guid.NewGuid()`) response — same status **and** same
(empty) body — so the 404 cannot serve as an existence oracle.

**Contract**: A small helper comparing `(StatusCode, await Content.ReadAsStringAsync())` for a
foreign id vs a random id on the same route. Applied to at least `claim`, `done`, `confirm`, edit,
delete, and `members/{userId}/role` (the role route's foreign-vs-unknown parity is the member-side
analogue).

### Success Criteria

#### Automated Verification

- All new isolation tests pass: `dotnet test tests/Homdutio.Api.Tests`
- The full suite stays green: `dotnet test`
- `done` and `confirm` foreign-household 404 cases now exist and pass.

#### Manual Verification

- Reviewer can read `HouseholdIsolationTests.cs` top-to-bottom and confirm every household-scoped
  endpoint in the two endpoint files is represented.

**Implementation Note**: After Phase 1 passes, pause for human confirmation before the refactor.

---

## Phase 2: Extract the shared scoping helper

### Overview

Collapse the two duplicated member-resolution + scoped-load helpers into one canonical internal
helper used by both endpoint files. Pure refactor — no behavior change. Cuttable.

### Changes Required

#### 1. Shared scoping helper

**File**: `src/Homdutio.Api/Households/HouseholdScope.cs` (new — or a shared location both endpoint
namespaces can reference)

**Intent**: One internal static helper owning the canonical scoping primitives so there is a single
place the household boundary is derived. Both `TaskEndpoints` and `HouseholdEndpoints` call it instead
of their private copies.

**Contract**: Expose the equivalents of today's duplicated logic — resolve the caller's
membership from a `ClaimsPrincipal` (`sub` → `HouseholdMember` → a single shared `CallerContext`
record carrying `HouseholdId`, `Role`, `UserId`; null when no membership), and a scoped task load
(`id + householdId` → task or null). Move the `CallerContext` record to one shared definition; delete
the per-file duplicates. No change to return types, null-handling, or `AsNoTracking` usage — the
handlers must behave byte-identically.

#### 2. Rewire both endpoint files

**File**: `src/Homdutio.Api/Tasks/TaskEndpoints.cs`, `src/Homdutio.Api/Households/HouseholdEndpoints.cs`

**Intent**: Replace the inline `ResolveMemberAsync` / `ResolveCallerAsync` / `LoadScopedTaskAsync`
calls with the shared helper; remove the now-dead private copies.

**Contract**: Every existing handler keeps its exact current control flow (same 404/403/409/200
branches in the same order). The diff is mechanical substitution, not logic change. The
`IsLastAdminLockedAsync` / sweep / transaction logic in `HouseholdEndpoints` is untouched.

### Success Criteria

#### Automated Verification

- The full suite stays green with zero test changes: `dotnet test`
- Build is clean: `dotnet build` (no unused-method/dead-code warnings from the removed duplicates).

#### Manual Verification

- Reviewer confirms the refactor is behavior-preserving: only one `CallerContext` and one scoping
  helper now exist; no handler's branch logic changed.

**Implementation Note**: After Phase 2 passes, pause for human confirmation before the convention guard.

---

## Phase 3: Route-coverage convention guard

### Overview

Add a test that enumerates the live API routes and fails when a route is neither marked
scoped-and-covered nor explicitly exempt — the structural backstop against a future endpoint
forgetting isolation. Cuttable.

### Changes Required

#### 1. Route-coverage convention test

**File**: `tests/Homdutio.Api.Tests/RouteIsolationCoverageTests.cs` (new)

**Intent**: Enumerate the registered `/api/tasks` + `/api/households` routes from the running test
host and assert the discovered set equals a known, categorized set — `scoped` routes that the
isolation sweep covers, and `exempt` routes (anonymous/non-household). A new uncategorized route
breaks the equality and fails the build, forcing the author to decide which bucket it belongs in.

**Contract**: Resolve `EndpointDataSource` from `_factory.Services`, project the `RouteEndpoint`s to
`(HttpMethod, Pattern)` tuples filtered to the two domain prefixes. Compare against two in-test
allowlists. Documented exempt route(s): `GET /api/households/invites/{token}` (anonymous preview);
`POST /api/households` (create-household — caller has no membership yet, scoping N/A); `POST
/api/households/invites/{token}/accept` (pre-membership join) — confirm the precise exempt set against
the actual enumeration during implementation. The test message must explain the contract so a future
author knows to add a new scoped route to `HouseholdIsolationTests` (or justify an exemption).

### Success Criteria

#### Automated Verification

- The convention test passes against the current route set: `dotnet test`
- Temporarily adding a dummy unlisted `/api/tasks` route makes the test FAIL (demonstrated during
  development, then reverted) — proving the guard has teeth.

#### Manual Verification

- Reviewer confirms the exempt allowlist is minimal and each exemption is justified in a comment.

**Implementation Note**: After Phase 3 passes, the slice is complete — pause for final human confirmation.

---

## Testing Strategy

### Unit / Integration Tests

- `HouseholdIsolationTests` — the both-directions sweep across every scoped endpoint (Phase 1).
- Body-shape parity assertions (foreign-id 404 ≡ unknown-id 404, empty body) (Phase 1).
- `RouteIsolationCoverageTests` — route enumeration vs categorized allowlists (Phase 3).
- All existing feature tests (`TaskEndpointsTests`, `HouseholdMemberAdminTests`,
  `HouseholdInviteEndpointsTests`, `HouseholdEndpointsTests`) remain unchanged and green — they are
  the behavior-preservation guard for the Phase 2 refactor.

### Manual Testing Steps

1. Run `dotnet test` — confirm all suites green.
2. Temporarily add a dummy unlisted scoped route; confirm `RouteIsolationCoverageTests` fails; revert.
3. Read `HouseholdIsolationTests.cs` and cross-check each `/api/tasks` + `/api/households` route in
   the two endpoint files appears in the sweep.

## Performance Considerations

None. Test-only + a behavior-preserving refactor. The shared helper keeps the existing `AsNoTracking`
and single-query patterns; no new queries on any request path.

## Migration Notes

None — no schema change, no migration.

## References

- Roadmap slice: `context/foundation/roadmap.md` (S-07)
- PRD: US-02, FR-019, Guardrails (`context/foundation/prd.md`)
- Existing scoping: `src/Homdutio.Api/Tasks/TaskEndpoints.cs:497,513`,
  `src/Homdutio.Api/Households/HouseholdEndpoints.cs:372`
- Existing cross-household tests: `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs:243`,
  `HouseholdMemberAdminTests.cs:269,316`, `HouseholdInviteEndpointsTests.cs:238`
- Test host pattern: `tests/Homdutio.Api.Tests/AuthApiFactory.cs`, `Program.cs:107`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Consolidated isolation sweep + gap fill

#### Automated

- [x] 1.1 All new isolation tests pass: `dotnet test tests/Homdutio.Api.Tests` — f0722ce
- [x] 1.2 The full suite stays green: `dotnet test` — f0722ce
- [x] 1.3 `done` and `confirm` foreign-household 404 cases now exist and pass — f0722ce

#### Manual

- [x] 1.4 Reviewer confirms every household-scoped endpoint is represented in the sweep — f0722ce

### Phase 2: Extract the shared scoping helper

#### Automated

- [x] 2.1 The full suite stays green with zero test changes: `dotnet test` — cde628f
- [x] 2.2 Build is clean with no dead-code warnings: `dotnet build` — cde628f

#### Manual

- [x] 2.3 Reviewer confirms one `CallerContext` + one scoping helper; no handler branch logic changed — cde628f

### Phase 3: Route-coverage convention guard

#### Automated

- [x] 3.1 The convention test passes against the current route set: `dotnet test`
- [x] 3.2 A dummy unlisted scoped route makes the test FAIL (demonstrated, then reverted)

#### Manual

- [x] 3.3 Reviewer confirms the exempt allowlist is minimal and each exemption is justified
