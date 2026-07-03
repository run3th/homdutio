# Invite/token abuse — integration abuse tests (Phase 4) Implementation Plan

## Overview

Close the remaining Risk #6 (invite/token abuse) gaps with integration tests on
the existing xUnit + `WebApplicationFactory` layer. The single-use invite design
already shipped (roadmap S-06: rowversion optimistic-concurrency token), so this
is a **verification-and-hardening** phase — tests only, no production code. The
one gap the risk map explicitly names ("ignoring the concurrent double-consume
race") is the headline; two narrow serial holes are closed alongside it.

This is Phase 4 of the test rollout defined in `context/foundation/test-plan.md`
(§3 row 4, Risk #6). It stays on the classic integration layer — the cheapest
real signal for this risk and the project's established convention.

## Current State Analysis

The invite feature lives entirely in `src/Homdutio.Api/Households/HouseholdEndpoints.cs`
(no separate service class): create `POST /api/households/invites`, anonymous
preview `GET /api/households/invites/{token}`, accept `POST /api/households/invites/{token}/accept`.
There is **no revoke and no list endpoint** — so "revoke then accept" is not
testable against the current API (out of scope, see below).

Single-use and scoping are enforced by several layers working together:

- **Consume atomicity** — accept reads the invite, rejects if `ConsumedAtUtc is not null`
  or expired (410 Gone), then sets `ConsumedAtUtc`/`ConsumedById` and inserts the
  `HouseholdMember` in **one atomic `SaveChanges`**. The `HouseholdInvite.RowVersion`
  (`HouseholdInvite.cs:40`, `IsRowVersion()` in `ApplicationDbContext.cs:156`) turns a
  concurrent second accept into a `DbUpdateConcurrencyException` → 410
  (`HouseholdEndpoints.cs:229-233`). No explicit transaction/lock is used on this
  path (unlike the last-admin guard, which holds `UPDLOCK/HOLDLOCK`).
- **One-household-per-user (FR-007)** — an `AnyAsync` pre-check → 409 Conflict
  (`HouseholdEndpoints.cs:206-210`), backstopped by a `UserId` unique index
  (`ApplicationDbContext.cs:61`); a race that beats the pre-check throws
  `DbUpdateException` → 409 (`HouseholdEndpoints.cs:235-238`).
- **Household scoping (US-02)** — the invite's `HouseholdId` is set server-side from
  the creator's own membership, and accept uses `invite.HouseholdId`, so a token is
  bound to exactly one household via the FK.
- **Expiry** — a 7-day window (`InviteLifetime`, `HouseholdEndpoints.cs:490`), checked
  at read time on both preview and accept → 410 Gone.

Existing coverage in `tests/Homdutio.Api.Tests/HouseholdInviteEndpointsTests.cs` is
already broad and must **not** be duplicated: happy join, *serial* double-consume
(410, no 2nd membership), expiry (410 on preview + accept), one-household-per-user
(409, token left unconsumed), cross-household scoping, unknown-token 404, auth
requirements, the emailed-link path, and rate limiting.

### Key Discoveries:

- **The named gap is concurrency.** No concurrent-accept test exists; double-consume
  is only tested serially (`HouseholdInviteEndpointsTests.cs:185`). The §2 Risk #6
  anti-pattern is literally "ignoring the concurrent double-consume race."
- **This race is finally observable at the integration layer.** Unlike the
  double-*claim* race on `HouseholdTask` (no rowversion — re-parked in Phases 2–3),
  `HouseholdInvite` **has** a rowversion, and DbContext is `Scoped`, so two in-process
  requests get separate scopes/connections and genuinely race (per §6.2's rationale).
- **Consumed-token preview is untested.** 410-on-preview is only asserted for the
  *expiry* branch (`:205`); the *consumed* branch on preview has no test. Per the
  implementation map, preview returns 410 for a consumed token — pinning this seals
  the anonymous preview surface against a consumed-token information leak.
- **Helpers are private, no shared base** (§6.1 per-file convention). The invite
  helpers (`GenerateInviteAsync`, `AcceptAsync`, `ExpireInviteAsync`, `SeedMemberAsync`,
  `RegisterAndLoginAsync`, `Authed`) are private to `HouseholdInviteEndpointsTests.cs`;
  the concurrency primitive `SendConcurrentlyAsync` is private to
  `HouseholdMemberAdminTests.cs`. New tests reuse the former in place and copy the latter.

## Desired End State

`HouseholdInviteEndpointsTests.cs` proves — deterministically, every run — that:
concurrent double-consume of one token yields exactly one membership; a free user
racing two valid tokens lands in exactly one household; and a consumed token is
sealed on the anonymous preview surface. `dotnet test tests/Homdutio.Api.Tests`
passes green (a red concurrent test would mean the guard is genuinely unsafe — a
real defect to escalate as its own change, not fix here).

Verify: the three new tests exist, pass, and each asserts a **post-state invariant**
read from a fresh DbContext scope as its primary oracle (status codes secondary).

## What We're NOT Doing

- **No production code changes.** The single-use/scoping design shipped (S-06); this
  phase verifies it. If a test goes red, that is a finding to escalate into a separate
  fix change — this plan stays tests-only.
- **No revoke/list-invite tests** — those endpoints do not exist in the API.
- **No duplication of the broad existing coverage** (serial reuse, expiry, scoping,
  one-household-per-user, unknown-token, auth, emailed-link, rate limiting).
- **No deleted-household / deleted-inviter cascade test and no malformed/oversized
  token-format test** — considered and held below the bar (§7 leans against
  low-signal edge tests; the token is a 64-char random hex and unknown tokens are
  already 404-tested).
- **No new test file, no e2e.** Phase 5 owns the e2e layer; this is classic integration.

## Implementation Approach

Extend the existing invite test file rather than create a new one: it already owns
every invite helper, and §6.1's per-file convention keeps invite tests together.
Copy the ~6-line `SendConcurrentlyAsync` from `HouseholdMemberAdminTests.cs` (the
suite has no shared base by design) and add a small `LoadInviteRowAsync` fresh-scope
reader. Follow the §6.2 concurrency discipline throughout: **assert the persisted
post-state invariant as the primary oracle; treat status codes as a secondary check.**

## Critical Implementation Details

**Concurrency model — the one fact that makes the "obvious" test wrong.** The invite
accept path does **not** serialize with a held `UPDLOCK/HOLDLOCK` the way the
last-admin guard does. It uses **rowversion optimistic concurrency**: both racing
requests read the same `RowVersion`, both attempt `UPDATE … WHERE RowVersion = @original`;
SQL Server serializes the two, the first commits (bumps the version), the second
matches 0 rows and EF throws `DbUpdateConcurrencyException` → 410. If one request
happens to fully commit before the other reads, the other hits the `ConsumedAtUtc`
pre-check → also 410. Either interleaving yields the **same deterministic outcome**:
exactly one 200 and one 410, exactly one membership. Therefore assert the *invariant*
(one membership; `ConsumedById` set to exactly one caller), and treat the status
codes as an unordered set — never assert *which* request won or *which* branch
produced the 410. No synchronization barrier or iteration loop is needed (as with
§6.2): if such a test flakes, that is the signal the guard regressed.

For the same-user double-join race, the `UserId` unique index is the deterministic
backstop: one insert wins, the other throws `DbUpdateException` → 409 (or the
`AnyAsync` pre-check catches it → 409). Assert the user is in exactly one household.

## Phase 1: Invite abuse — concurrency + serial gap closure

### Overview

Add three tests plus two private helpers to `HouseholdInviteEndpointsTests.cs`,
closing the concurrent double-consume gap (the Risk #6 anti-pattern) and two narrow
serial holes. Each test builds its own isolated state (unique emails/household) since
the fixture is shared per class.

### Changes Required:

#### 1. Concurrency + fresh-scope helpers

**File**: `tests/Homdutio.Api.Tests/HouseholdInviteEndpointsTests.cs`

**Intent**: Give the new tests a way to fire two accepts truly in parallel and to
read the persisted invite/membership state from a fresh scope for the primary oracle.

**Contract**: A private `SendConcurrentlyAsync(HttpRequestMessage first, HttpRequestMessage second)`
returning both responses positionally via `Task.WhenAll` — copied verbatim from
`HouseholdMemberAdminTests.cs:125` (the suite has no shared base; a per-file copy is
the established pattern). A private `LoadInviteRowAsync(string token)` that opens
`_factory.Services.CreateScope()` → `ApplicationDbContext` and reads the
`HouseholdInvite` `AsNoTracking()` (mirrors `LoadTaskRowAsync` at `TaskEndpointsTests.cs:727`).
Membership counts are read the same fresh-scope way (as at `:198`).

#### 2. Test — concurrent double-consume of one token

**File**: `tests/Homdutio.Api.Tests/HouseholdInviteEndpointsTests.cs`

**Intent**: Prove the single-use rowversion seal holds under a true race: two
different free users accepting the *same* token simultaneously produce exactly one
membership — the gap the §2 anti-pattern names.

**Contract**: Admin creates a household + one invite; two registered free users
(unique emails) accept the same token via `SendConcurrentlyAsync`. **Primary oracle**
(fresh scope): the household has exactly one *joined* member beyond the admin, and
`LoadInviteRowAsync(token).ConsumedById` equals exactly one of the two callers with
`ConsumedAtUtc != null`. **Secondary**: the two response statuses are the set
{200 OK, 410 Gone}. Do not assert which caller won.

#### 3. Test — same-user concurrent double-join across two tokens

**File**: `tests/Homdutio.Api.Tests/HouseholdInviteEndpointsTests.cs`

**Intent**: Prove the one-household-per-user invariant (FR-007) holds under a race —
the `UserId` unique-index backstop, not just the serial `AnyAsync` pre-check already
tested at `:221`.

**Contract**: Two admins create households A and B, each mints an invite; one free
user accepts both tokens via `SendConcurrentlyAsync`. **Primary oracle** (fresh
scope): that user has exactly one `HouseholdMember` row. **Secondary**: statuses are
the set {200 OK, 409 Conflict}. Assert the losing token remains consumable by a
different free user is out of scope (covered serially at `:221`); focus on the
single-membership invariant.

#### 4. Test — consumed token returns 410 on anonymous preview

**File**: `tests/Homdutio.Api.Tests/HouseholdInviteEndpointsTests.cs`

**Intent**: Seal the consumed-token branch on the *preview* surface — currently only
the expiry branch is preview-tested (`:205`). A consumed token must not leak
household name / inviter identity via the anonymous preview.

**Contract**: Mint a token, accept it successfully (consuming it), then `GET
/api/households/invites/{token}` **without a bearer** → 410 Gone. If the endpoint
instead returns 200 with household/inviter fields, the test fails and that is an
information-leak finding to escalate (ties Risk #6 to the Risk #1 existence-oracle
concern) — do not weaken the assertion to match a leaky implementation.

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build`
- Invite test suite passes green, all runs (determinism check): `dotnet test tests/Homdutio.Api.Tests --filter "FullyQualifiedName~HouseholdInviteEndpointsTests"`
- Full API test suite passes (no regression): `dotnet test tests/Homdutio.Api.Tests`

#### Manual Verification:

- The two concurrent tests pass on repeated local runs (≥5×) with no flake — confirming the race is deterministic, not timing-lucky.
- Each new test's primary assertion is the persisted post-state invariant (membership count / `ConsumedById` from a fresh scope), with status codes only as a secondary check — reviewed by reading the test, per the §6.2 anti-pattern.
- The consumed-on-preview test asserts 410 (not a relaxed-to-match-impl status); if it reveals a leak, a follow-up fix change is opened rather than the assertion softened.

**Implementation Note**: After completing this phase and all automated verification
passes, pause for manual confirmation that the repeated-run determinism check and the
oracle review above were done before considering the phase complete. Phase blocks use
plain bullets — the `- [ ]` checkboxes live in `## Progress` below.

---

## Testing Strategy

### Integration Tests:

- Concurrent double-consume of one token → exactly one membership (invariant), {200,410} statuses.
- Same-user concurrent double-join across two tokens → user in exactly one household (invariant), {200,409} statuses.
- Consumed token → 410 on anonymous preview (single-use seal on the leak surface).

### Manual Testing Steps:

1. Run the invite suite 5× and confirm no flake on the two concurrency tests.
2. Read each new test and confirm the primary oracle is the persisted invariant from a fresh scope.
3. Confirm no existing invite test was modified or duplicated.

## Performance Considerations

Negligible — three added integration tests against per-instance LocalDB. The two
concurrency tests fire a single pair of parallel requests each (no iteration loop),
matching the §6.2 pattern.

## Migration Notes

None — no schema or production change.

## References

- Test plan: `context/foundation/test-plan.md` (§2 Risk #6 response guidance; §6.1 invite tests; §6.2 concurrency; §6.3 fresh-scope reads)
- Implementation: `src/Homdutio.Api/Households/HouseholdEndpoints.cs:191-242` (accept), `:156-187` (preview); `src/Homdutio.Data/Entities/HouseholdInvite.cs:40` (rowversion); `src/Homdutio.Data/ApplicationDbContext.cs:61` (UserId unique index), `:156` (rowversion config)
- Existing coverage (do not duplicate): `tests/Homdutio.Api.Tests/HouseholdInviteEndpointsTests.cs`
- Concurrency helper to copy: `tests/Homdutio.Api.Tests/HouseholdMemberAdminTests.cs:125` (`SendConcurrentlyAsync`)
- Fresh-scope reader pattern: `tests/Homdutio.Api.Tests/TaskEndpointsTests.cs:727` (`LoadTaskRowAsync`)
- Lessons: `context/foundation/lessons.md` (atomic check-and-mutate for min-count invariants — the sibling reasoning behind the rowversion single-use consume)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Invite abuse — concurrency + serial gap closure

#### Automated

- [x] 1.1 Build passes: `dotnet build` — 8f1eb1b
- [x] 1.2 Invite test suite passes green on repeated runs: `dotnet test tests/Homdutio.Api.Tests --filter "FullyQualifiedName~HouseholdInviteEndpointsTests"` — 8f1eb1b
- [x] 1.3 Full API test suite passes (no regression): `dotnet test tests/Homdutio.Api.Tests` — 8f1eb1b

#### Manual

- [x] 1.4 Concurrent tests pass ≥5× locally with no flake (determinism confirmed) — 8f1eb1b
- [x] 1.5 Each new test's primary oracle is the persisted post-state invariant from a fresh scope (status codes secondary) — 8f1eb1b
- [x] 1.6 Consumed-on-preview test asserts 410 and is not softened to match a leaky impl; a leak becomes a follow-up fix change — 8f1eb1b
