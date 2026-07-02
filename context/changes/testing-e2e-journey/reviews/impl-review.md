<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: E2E Journey + CI Gate for the MVP Cross-Stack Flow (Risk #4)

- **Plan**: context/changes/testing-e2e-journey/plan.md
- **Scope**: Full plan (Phases 1–3 of 3)
- **Date**: 2026-07-02
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

Success criteria evidence: `npx playwright --version` → 1.61.1; `tsc -b --noEmit` → green; `ng lint` → all pass; full `npm run e2e` proven green in CI on commit c84cec8 (PR #18); 3.4 break-verified (poll break → e2e red at journey.spec.ts:109, reverted). All manual items confirmed.

## Findings

### F1 — Required `e2e` check + workflow `paths-ignore` will deadlock docs-only PRs

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: .github/workflows/deploy.yml:10-19 (+ ruleset `e2e-gate`)
- **Detail**: The workflow has `paths-ignore: ['context/**','docs/**','**/*.md']`. The `e2e` job is now a *required* status check (ruleset `e2e-gate`, created this session for item 3.3). A PR whose entire base…head diff touches only ignored paths never triggers the workflow, so the `E2E (journey, Risk #4)` context never reports and GitHub blocks the merge ("waiting for status"). This repo produces many docs-only PRs (the whole `context/**` workflow), so this is practically reachable. NOTE: mixed PRs are fine — PR #18's doc-only epilogue commit still re-triggered CI because the *cumulative* PR diff includes `web/`+`.github/`. The footgun is docs-only PRs specifically.
- **Fix A ⭐ Recommended**: Add a companion "status-shim" job named exactly `E2E (journey, Risk #4)` in a second workflow that triggers on the ignored paths and just `exit 0`, so the required context always reports green when the real job is skipped.
  - Strength: Keeps the merge-blocking gate on code PRs (3.3 intact) while unblocking docs-only PRs — the documented GitHub workaround for required-check + path filters.
  - Tradeoff: A second workflow/job to maintain; the shim name must stay in lockstep with the real job name (rename one → rename both).
  - Confidence: HIGH — this is the standard, widely-used pattern for exactly this situation.
  - Blind spot: Haven't drafted the shim YAML; job-name matching must be exact.
- **Fix B**: Remove `e2e` from the required-checks ruleset — treat it as a strong non-blocking signal instead of a hard gate.
  - Strength: Zero deadlock risk; simplest.
  - Tradeoff: Undoes item 3.3 — a red journey no longer blocks a merge, so the gate loses its teeth.
  - Confidence: HIGH — trivially correct, but weakens the intended guarantee.
  - Blind spot: None significant.
- **Decision**: FIXED (revised). Fix A (a shim workflow emitting the same-named check) was tried first but caused a WORSE deadlock: two workflows producing the required context `E2E (journey, Risk #4)` made GitHub register a phantom "Expected — waiting for status" that blocked even a fully-green mixed PR (#18). Replaced with the robust single-producer approach: removed `paths-ignore` from `deploy.yml`'s `pull_request` trigger (kept on `push`) and deleted the shim, so exactly one workflow always reports the required check on every PR — no docs-only deadlock, no cross-workflow collision. Actions is free on this now-public repo, so docs-only PRs running the pipeline cost only wall-clock.

### F2 — Run-scoped DB name doesn't vary across re-run attempts; "never collide" is overstated

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: .github/workflows/deploy.yml:90-104
- **Detail**: `homdutio_e2e_${{ github.run_id }}` is unique per run but stable across "Re-run failed jobs" (same `run_id`, incrementing `run_attempt`), so a re-run targets the same DB name. `ef database update` is idempotent and all test data is `Date.now()`-unique, so this is benign in practice — but the comment's "parallel PR runs never collide" overclaims. On GitHub-hosted runners each job gets a fresh VM (LocalDB is per-VM), so cross-run collision cannot happen anyway; the claim only needs hardening if the "shared Windows runner pool" is ever self-hosted.
- **Fix**: Append `_${{ github.run_attempt }}` to the DB name and soften the comment to state the isolation guarantee holds for GitHub-hosted runners (fresh per-VM LocalDB); if self-hosted runners are ever used, note the shared-instance caveat.
- **Decision**: FIXED — DB name now `homdutio_e2e_${{ github.run_id }}_${{ github.run_attempt }}` (env + drop step); comment softened.

### F3 — CI step named "Run Playwright journey" actually runs all three specs

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: .github/workflows/deploy.yml:153-155
- **Detail**: The step `Run Playwright journey` runs `npm run e2e` = `playwright test`, which executes smoke + seed + journey specs, not only the journey. Not wrong (more coverage), but the name implies journey-only.
- **Fix**: Rename the step to "Run Playwright E2E suite" (keep running all specs — the extra coverage is desirable), or scope the command if journey-only was intended.
- **Decision**: FIXED — step renamed to "Run Playwright E2E suite"; comment clarifies it runs smoke+seed+journey.

### F4 — test-plan.md §3 Phase 5 status reads `implementing` though the plan is now complete

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/foundation/test-plan.md:98
- **Detail**: Phase 5's row shows status `implementing`. That was correct when set (Progress still had `[ ]` rows), but the plan's Progress is now fully `[x]` and `change.md` is `implemented`/`impl_reviewed`. Per the §3 status vocabulary (`complete` = Progress fully `[x]`), the row should now read `complete`. Minor doc lag; no functional impact. (Note: the test-plan orchestrator normally advances this; a manual bump is fine.)
- **Fix**: Update the §3 Phase 5 status cell from `implementing` to `complete`.
- **Decision**: FIXED — §3 Phase 5 status set to `complete`.

## Notes (benign — no action)

- Agent-confirmed non-issues: the invite token is read from the `POST /api/households/invites` response (not a `<code>` DOM scrape) because that element has no accessible name — a justified exception that upholds the no-DOM-selector rule. `tsconfig.e2e.json` `types: ["node"]` typechecks fine (Playwright types come via import). `UseHttpsRedirection` on the http-only profile logs a benign warning and doesn't redirect. `retries: 1` can mask a flaky-but-real convergence bug — watch the retry rate. No delete API means CI DBs accumulate orphan accounts (fine — DB dropped per run).
