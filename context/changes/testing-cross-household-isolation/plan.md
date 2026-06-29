# Cross-household isolation hardening — Implementation Plan

## Overview

Rollout Phase 1 of `context/foundation/test-plan.md` defends **Risk #1** — a
member of household A reads, acts on, or infers the existence of household B's
tasks, roster, or invites (the PRD's worst-possible bug, US-02 / FR-019).

The `/10x-research` pass established that the isolation contract is **sealed by
construction**: every household-scoped handler AND-s the resource id with the
JWT-derived `HouseholdId`, returns an argument-less `Results.NotFound()`, and
`Program.cs` registers no `ProblemDetails`/exception middleware, so a
foreign-household 404 is byte-identical to an unknown-id 404. No route has
escaped the existing sweep. This phase therefore does **not** add greenfield
coverage — it closes three specific gaps so the seal cannot silently regress:

1. **Parity is not asserted for every 404-producing route** (Gap #1).
2. **The coverage guard proves "categorized," not "exercised"** — a route can
   sit in the `Scoped` set with no behavioral assertion (Gap #2).
3. **`/api/auth` is an unconditional exempt prefix** — a future
   household-scoped route added there would be silently exempted (Gap #3).

The chosen approach introduces a **shared scoped-route inventory** as the single
source of truth that drives *both* the coverage guard (set-equality) *and* an
inventory-driven parity sweep. Being in the inventory makes a route both
"categorized" and "exercised" at once — Gap #2 is closed by construction rather
than by convention.

## Current State Analysis

Two existing tests in `tests/Homdutio.Api.Tests/` (both
`IClassFixture<AuthApiFactory>`, integration vs LocalDB):

- **`HouseholdIsolationTests.cs` (the behavioral sweep)** — builds House A (admin
  + seeded Member + a task in each lifecycle state) and House B, then drives B's
  bearer token against A's ids. Routes are **hardcoded inline per fact**, grouped
  by category (lifecycle, management, comment, member-admin). The parity helper
  `AssertNotFoundParityAsync` (`:156-168`) asserts `foreignBody == unknownBody`
  AND `foreignBody == string.Empty`.
- **`RouteIsolationCoverageTests.cs` (the build guard)** — reflects over the live
  `EndpointDataSource` (`:94-125`) and, via an **inverted filter** (every `/api/*`
  route except `ExemptPrefixes = { "/api/auth" }`), asserts set-equality against a
  hardcoded `Scoped` (`:34-50`) ∪ `Exempt` (`:57-65`). Any uncategorized or stale
  route fails the build.

**Accurate parity baseline (corrects the research prose).** Research said parity
runs for "only 2 of 14" — that counted *facts*, not routes. Reading the code, the
helper is already invoked for **7 routes**:

- task-id parity fact (`:274-286`): claim, done, confirm, `PUT /api/tasks/{id}`, `DELETE /api/tasks/{id}`
- member-id parity fact (`:288-299`): role, remove

The **genuine parity gaps** are: `POST /api/tasks/{id}/unclaim`,
`POST /api/tasks/{id}/sendback`, `POST /api/tasks/{id}/comments`,
`GET /api/tasks/{id}/comments`, and `PUT /api/tasks/order` (reorder has a 404
assertion but no body parity). The seal holds for these today only because every
handler happens to use argument-less `NotFound()` — nothing in the test locks
that in.

**The 14 `Scoped` routes split into three behavioral shapes** (this drives the
inventory's `Behavior` enum):

- **Foreign-id 404 (parity applies) — 11 routes:** claim, done, confirm, unclaim,
  sendback, `PUT /api/tasks/{id}`, `DELETE /api/tasks/{id}`, comments POST,
  comments GET, member role, member remove.
- **Own-only collection 200 (no 404 to parity) — 2 routes:** `GET /api/tasks`
  (board) and `GET /api/households/members` (roster). A foreign caller gets a
  200 containing only their own data; these have read-isolation assertions today.
- **Mixed-batch rejection — 1 route:** `PUT /api/tasks/order` — a foreign id mixed
  into B's own batch rejects the whole request with 404 and must not corrupt B's
  order.

### Key Discoveries:

- The guard's `Scoped` set and the sweep's facts have **no programmatic link**
  (`RouteIsolationCoverageTests.cs:34` vs the inline calls in
  `HouseholdIsolationTests.cs`) — the root of Gap #2.
- `NormalizePattern` (`RouteIsolationCoverageTests.cs:131-135`) strips inline
  constraints (`{id:guid}` → `{id}`); the inventory's route keys must match this
  normalized form (`{id}`, `{userId}`).
- `Program.cs:65-83` registers no `AddProblemDetails`/exception middleware — the
  structural reason the empty-body 404 is bare. (No change here; this is *why*
  parity holds, and the parity sweep is what locks it in.)
- `GET /api/households/me` returns **204** (not 404) when the caller has no
  household (`HouseholdEndpoints.cs:31-33`) — it is `Exempt`, untouched here.
- The invite preview `GET /api/households/invites/{token}` is an **intentional
  oracle** (`.AllowAnonymous()`, leaks household name by design) — `Exempt`, out
  of scope for the 404-parity rule (it is Phase 4 / Risk #6 territory).
- House A/House B construction is expensive (multiple register/login/create
  round-trips against LocalDB per fact); the sweep should reuse one pair.

## Desired End State

- The set of household-scoped routes lives in **one place** (a shared inventory in
  the test project). The coverage guard derives its `Scoped` set from it, and the
  behavioral sweep iterates the same list — so a `Scoped` route that lacks a
  behavioral assertion is impossible to express.
- **Every foreign-id-404 scoped route** is parity-checked (empty body == unknown-id
  body), not just status-code-checked. A future `NotFound(new { message })` on any
  of them fails a test instead of leaking an existence oracle.
- A new household-scoped route **cannot ship without being added to the inventory**
  (the guard fails the build), and once in the inventory it is **automatically
  swept and parity-checked** (no second manual step).
- The `/api/auth` exempt-prefix blind spot is documented at the guard and in the
  §6 cookbook so a household-scoped route is never placed there.
- The §6 cookbook records the inventory-driven pattern as the canonical way to add
  a backend integration / isolation test.

**Verification:** `dotnet test` green; the tripwire check (Phase 2 Manual
Verification) confirms the new assertions go red on a real regression and a dummy
un-exercised scoped route; the §3 Phase 1 row in `test-plan.md` flips to
`complete`.

## What We're NOT Doing

- **No production code changes.** This is a test-and-docs hardening phase. The
  isolation contract is already correct; we are locking it, not fixing it.
- **No new `ProblemDetails`/middleware** — the bare empty 404 is the seal; we
  assert it, we don't reshape it.
- **Not re-proving the sealed-by-construction parts** (that the WHERE clause
  AND-s `HouseholdId`, that 403 sits below the scoped lookup) — research confirmed
  these; the sweep exercises them as a side effect, but they are not new test
  targets.
- **No guard reflection over handler bodies** to detect "touches `HouseholdId`"
  under `/api/auth` (Gap #3) — rejected as fragile; a comment + cookbook rule is
  the chosen response.
- **No e2e / Playwright** — Risk #1's cheapest real signal is the integration
  layer (the project convention); e2e is Phase 5 / Risk #4.
- **Not touching invite-token routes** (`Exempt`, intentional oracle) — Phase 4 /
  Risk #6.
- **No concurrency / TOCTOU work** on member-remove — that is Phase 3 / Risk #3
  (`lessons.md` last-admin guard).

## Implementation Approach

Introduce a `ScopedRouteInventory` in the test project: a static list of
descriptors, each carrying enough to (a) project a normalized `"METHOD /pattern"`
key for the guard and (b) build a real foreign request for the sweep. The guard's
`Scoped` set becomes a projection of the inventory; the sweep iterates the
inventory and dispatches on each entry's `Behavior` via an **exhaustive switch**
(a new `Behavior` value without a case fails to compile; a new inventory entry is
auto-swept). This makes "categorized" and "exercised" the same fact.

Phasing is incremental and keeps the suite green at each step: Phase 1 is a pure
refactor of where the `Scoped` set comes from (behavior unchanged), Phase 2
rewires the sweep to the inventory and extends parity, Phase 3 documents.

## Critical Implementation Details

- **Route-key parity with the guard.** Inventory templates must match
  `NormalizePattern`'s output exactly (`{id}`, `{userId}`, leading slash, no
  constraints) or Phase 1's set-equality assertion will spuriously fail. This is
  the one load-bearing coupling between the inventory and the existing guard.
- **xUnit data-binding.** Prefer a **single `[Fact]` that loops the inventory**
  and aggregates failures (asserting the collected failure list is empty with a
  descriptive message) over `[Theory]`/`MemberData` — the descriptors carry
  delegates and request bodies that are not cleanly serializable, and `MemberData`
  would warn/serialize awkwardly. (`[Theory]` is a viable alternative only if
  descriptors are made serializable; not worth it here.)
- **Fixture reuse safety.** Reusing one House A/House B across all sweep entries
  is safe because every foreign/unknown call resolves no row and therefore mutates
  nothing — including `DELETE` and `PUT` (they 404 before any write). Build the
  pair once per sweep fact.

## Phase 1: Shared scoped-route inventory + guard refactor

### Overview

Establish the single source of truth and point the coverage guard at it. Pure
refactor — no behavioral assertion changes; the full suite stays green. Also lands
the Gap #3 guard-side comment.

### Changes Required:

#### 1. Scoped-route inventory (new file)

**File**: `tests/Homdutio.Api.Tests/ScopedRouteInventory.cs` (new)

**Intent**: Define the canonical list of the 14 household-scoped routes as
descriptors that both tests consume, so the route set is declared once.

**Contract**: A static class exposing an `IReadOnlyList<ScopedRoute>` and a
`Behavior` enum with exactly three values — `ParityNotFound`, `OwnOnlyCollection`,
`MixedBatchRejected`. Each `ScopedRoute` carries: HTTP method; the normalized route
template (e.g. `/api/tasks/{id}/claim`, `/api/households/members/{userId}`); an id
shape (task `Guid` vs member `userId` string) so the sweep can pick the right
foreign id and an unknown id of the matching shape; the `Behavior`; and an optional
request-body factory (for sendback `{comment}`, edit `{title,description,category}`,
comments POST `{body}`, role `{role}`, reorder `{status,orderedIds}`). Expose a
projection to the guard's `"METHOD /pattern"` key form. The 14 entries and their
behavior classes are enumerated in Current State Analysis.

#### 2. Guard derives `Scoped` from the inventory

**File**: `tests/Homdutio.Api.Tests/RouteIsolationCoverageTests.cs`

**Intent**: Replace the hardcoded `Scoped` HashSet with a projection of the
inventory so the guard and the sweep can never disagree about the scoped set.

**Contract**: `Scoped` becomes `ScopedRouteInventory` projected to the
`"METHOD /pattern"` keys (preserving `StringComparer.OrdinalIgnoreCase`). `Exempt`,
`ExemptPrefixes`, `DiscoverDomainRoutes`, `NormalizePattern`, and both `[Fact]`s
keep their current behavior. Set-equality against the live `EndpointDataSource`
must still hold (the projected keys equal the previous 14 literals).

#### 3. `/api/auth` blind-spot comment (Gap #3, code-side)

**File**: `tests/Homdutio.Api.Tests/RouteIsolationCoverageTests.cs`

**Intent**: Make the one structural blind spot explicit at the line that creates
it, so a future author placing a household-scoped route under `/api/auth` is
warned at the source.

**Contract**: A pointed comment at `ExemptPrefixes` (`:72`) stating that
`/api/auth` is unconditionally exempt and a household-scoped route must never be
added under it (cross-reference §6 cookbook).

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build`
- Full backend suite green (guard set-equality still holds against the live route graph): `dotnet test tests/Homdutio.Api.Tests`
- The inventory projects to exactly the 14 previously-hardcoded `Scoped` keys (asserted by the existing `Every_household_domain_route_is_categorized_scoped_or_exempt` fact)

#### Manual Verification:

- Inventory entries' templates visually match `NormalizePattern` output (`{id}`/`{userId}`, no constraints)
- The `Behavior` assigned to each route matches its real foreign-caller shape (11 `ParityNotFound`, 2 `OwnOnlyCollection`, 1 `MixedBatchRejected`)

**Implementation Note**: After completing this phase and all automated
verification passes, pause for human confirmation of the manual checks before
proceeding.

---

## Phase 2: Inventory-driven parity + behavior sweep

### Overview

Rewire the behavioral sweep to iterate the inventory, extend parity to every
`ParityNotFound` route, and route the collection/reorder shapes through an
exhaustive `Behavior` switch — so every `Scoped` (inventory) entry is exercised by
construction (Gap #2) and every foreign-id 404 is body-parity-sealed (Gap #1).

### Changes Required:

#### 1. Inventory-driven sweep

**File**: `tests/Homdutio.Api.Tests/HouseholdIsolationTests.cs`

**Intent**: Replace the four hand-written per-category 404 facts with one sweep
that iterates `ScopedRouteInventory`, dispatching each entry on its `Behavior`, so
parity now covers unclaim, sendback, comments POST/GET, and reorder in addition to
the seven already covered.

**Contract**: A single `[Fact]` builds one House A + one House B, then loops the
inventory. An **exhaustive `switch` on `Behavior`**: `ParityNotFound` →
`AssertNotFoundParityAsync` against the entry's foreign-id URI vs an unknown-id URI
of the matching shape (using the entry's body factory where present);
`OwnOnlyCollection` → the existing read-isolation assertion (board shows none of
A's task ids / roster lists only B's own member); `MixedBatchRejected` → the
reorder assertion (foreign id mixed into B's batch → 404 and B's order unchanged).
Aggregate per-route failures into one descriptive assertion. The reused
House A/House B is safe because no foreign/unknown call mutates state (see Critical
Implementation Details). The standalone read-isolation board fact and the existing
two parity facts are subsumed by the sweep and removed (parity status-code checks
are a strict subset of the parity assertion).

#### 2. Preserve special-case assertions

**File**: `tests/Homdutio.Api.Tests/HouseholdIsolationTests.cs`

**Intent**: Keep the assertions that the generic parity helper does not express —
board/roster own-only payloads and reorder no-corruption — wired through the
inventory's non-parity `Behavior` values rather than as orphaned facts.

**Contract**: The `OwnOnlyCollection` and `MixedBatchRejected` switch arms carry
the same assertions the removed facts made (`DoesNotContain` A's ids on B's board;
`Single` own member on B's roster; B's order array unchanged after a rejected
mixed reorder). No assertion strength is lost relative to the current file.

### Success Criteria:

#### Automated Verification:

- Full backend suite green: `dotnet test tests/Homdutio.Api.Tests`
- The sweep exercises all 14 inventory entries (no entry skipped — the loop count equals `ScopedRouteInventory.Count`; assert this explicitly inside the fact)
- Parity now asserted for unclaim, sendback, comments POST, comments GET, and reorder (previously status-only)

#### Manual Verification:

- **Tripwire — parity is not vacuous**: transiently change one scoped handler's `Results.NotFound()` to `Results.NotFound(new { message = "x" })`; confirm the sweep goes **red** for that route; revert.
- **Tripwire — coverage→behavior bridge is real**: transiently add a dummy household-scoped route (e.g. `POST /api/tasks/{id}/ping`) without adding it to the inventory; confirm the **guard** fails the build (uncategorized); then add it to the inventory and confirm the **sweep** now drives it automatically; revert both.
- Sweep wall-clock is acceptable (one House A/House B build, not one per route)

**Implementation Note**: After automated verification passes, perform both
tripwire checks and confirm the reverts leave the suite green before proceeding.

---

## Phase 3: Cookbook §6 update

### Overview

Record the inventory-driven pattern as the canonical backend-integration/isolation
test recipe and land the Gap #3 documentation rule. Mandated by the test-plan
workflow (each rollout phase ends by updating the relevant §6 entry).

### Changes Required:

#### 1. §6.1 backend integration test pattern

**File**: `context/foundation/test-plan.md`

**Intent**: Replace the §6.1 `TBD` placeholder with the concrete pattern this
phase established.

**Contract**: §6.1 documents: **location** (`tests/Homdutio.Api.Tests/`,
`IClassFixture<AuthApiFactory>`, LocalDB, no shared base class); **the inventory
rule** — a new household-scoped route must be added to
`ScopedRouteInventory.cs`, after which the coverage guard *and* the parity sweep
exercise it automatically; **the `/api/auth` rule** — never place a
household-scoped route under `/api/auth` (unconditionally exempt); **reference
test** (`HouseholdIsolationTests.cs` sweep + `ScopedRouteInventory.cs`); **run
command** (`dotnet test tests/Homdutio.Api.Tests`).

#### 2. §6.6 per-phase note

**File**: `context/foundation/test-plan.md`

**Intent**: Capture the one surprising thing this phase taught for future readers.

**Contract**: A 2–3 line §6.6 note recording that the guard previously proved
coverage but not behavior, and that the shared inventory now makes the two the same
fact; and that parity was already on 7 routes (not 2 — research counted facts).

#### 3. Flip the rollout status

**File**: `context/foundation/test-plan.md` §3 + change folder `change.md`

**Intent**: Mark Phase 1 complete in the orchestrator's state.

**Contract**: §3 Phase 1 row Status → `complete`; `change.md` `status:` and
`updated:` stamped. (The orchestrator re-derives state from disk on the next
`/10x-test-plan` run.)

### Success Criteria:

#### Automated Verification:

- §6.1 no longer contains `TBD` for the backend integration pattern: `grep -n "6.1" context/foundation/test-plan.md`
- §3 Phase 1 row reads `complete`

#### Manual Verification:

- A reader unfamiliar with the project can add a new scoped route and know to put it in the inventory from §6.1 alone
- The `/api/auth` rule is stated in both the guard comment (Phase 1) and §6.1

**Implementation Note**: Documentation-only phase; no app behavior to verify
manually beyond the doc read-through.

---

## Testing Strategy

### Unit Tests:

- N/A — this project has no isolated domain/unit layer; all backend tests are
  integration vs LocalDB (`test-plan.md` §4).

### Integration Tests:

- The inventory-driven sweep (Phase 2) **is** the integration test surface: every
  scoped route driven from a foreign household, asserting 404 + empty-body parity
  (or own-only payload / batch rejection), reusing one House A/House B.
- The coverage guard (Phase 1) reflects the live route graph and fails the build on
  any uncategorized/stale route.

### Manual Testing Steps:

1. Run `dotnet test tests/Homdutio.Api.Tests` — all green.
2. Tripwire A: break one handler's 404 body → sweep red → revert.
3. Tripwire B: add a dummy scoped route, omit from inventory → guard red; add to
   inventory → sweep drives it; revert both.

## Performance Considerations

- Reusing one House A/House B per sweep fact replaces ~N per-route builds with one,
  cutting LocalDB round-trips materially. Safe because foreign/unknown calls never
  mutate (they 404 before any write).

## Migration Notes

- None — test-and-docs only; no schema, no data, no production code.

## References

- Research: `context/changes/testing-cross-household-isolation/research.md`
- Quality contract: `context/foundation/test-plan.md` §2 (Risk #1) + §3 (Phase 1)
- Existing sweep: `tests/Homdutio.Api.Tests/HouseholdIsolationTests.cs:156-299`
- Existing guard: `tests/Homdutio.Api.Tests/RouteIsolationCoverageTests.cs:34-125`
- Contract origin: `context/archive/2026-06-12-household-data-isolation/` (S-07)
- Lessons (Phase 3 / Risk #3, not here): `context/foundation/lessons.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Shared scoped-route inventory + guard refactor

#### Automated

- [x] 1.1 Build passes: `dotnet build` — 50ad576
- [x] 1.2 Full backend suite green; guard set-equality still holds: `dotnet test tests/Homdutio.Api.Tests` — 50ad576
- [x] 1.3 Inventory projects to exactly the 14 previously-hardcoded `Scoped` keys — 50ad576

#### Manual

- [x] 1.4 Inventory templates match `NormalizePattern` output (`{id}`/`{userId}`, no constraints) — 50ad576
- [x] 1.5 Each route's `Behavior` matches its real foreign-caller shape (11 parity / 2 collection / 1 batch) — 50ad576

### Phase 2: Inventory-driven parity + behavior sweep

#### Automated

- [x] 2.1 Full backend suite green: `dotnet test tests/Homdutio.Api.Tests`
- [x] 2.2 Sweep exercises all 14 inventory entries (loop count == `ScopedRouteInventory.Count`, asserted)
- [x] 2.3 Parity now asserted for unclaim, sendback, comments POST, comments GET, reorder

#### Manual

- [ ] 2.4 Tripwire — parity not vacuous: break one handler's 404 body → sweep red → revert
- [ ] 2.5 Tripwire — coverage→behavior bridge real: dummy scoped route uncategorized → guard red; added to inventory → sweep drives it; revert
- [ ] 2.6 Sweep wall-clock acceptable (one House A/House B build)

### Phase 3: Cookbook §6 update

#### Automated

- [ ] 3.1 §6.1 no longer reads `TBD` for the backend integration pattern
- [ ] 3.2 §3 Phase 1 row reads `complete`

#### Manual

- [ ] 3.3 A new reader can add a scoped route knowing to put it in the inventory from §6.1 alone
- [ ] 3.4 The `/api/auth` rule appears in both the guard comment and §6.1
