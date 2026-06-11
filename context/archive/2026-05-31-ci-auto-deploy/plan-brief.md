# CI Auto-Deploy — Plan Brief

> Full plan: `context/changes/ci-auto-deploy/plan.md`

## What & Why

Replace the manual Windows zip deploy with a GitHub Actions pipeline (F-04). PRs and pushes to `main`
run a build + test gate; merges to `main` then — behind a human-approved `production` environment —
migrate Azure SQL, deploy the single .NET+Angular artifact via OIDC, and smoke-test `/health`. Removes
the botched-manual-deploy footgun and the `Compress-Archive` gotcha, and delivers `ci_default_flow:
auto-deploy-on-merge`.

## Starting Point

No CI exists (`.github/` is absent). Deploy today is manual: `dotnet publish -c Release` (which bundles
Angular via the `BuildAngularSpa` MSBuild target) → `tar.exe` zip → `az webapp deploy` to the live B1
App Service `homdutio`. The live artifact is the original scaffold — it predates `/health`, the
DbContext, and all auth, so the first auto-deploy is a large F-01+F-02 jump.

## Desired End State

Merging to `main` auto-runs the gate, pauses for one approval, then migrates + deploys + verifies
`/health` is `Healthy`. PRs run the gate without deploying. No long-lived deploy credential in the repo.
The manual ritual survives only as documented break-glass rollback.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Azure credential | OIDC federated credential | No stored long-lived secret; infra.md's stated preference. | Infra/Plan |
| CI test runner | `windows-latest` + LocalDB | Tests hardcode LocalDB (Windows-only) → zero test changes. | Plan |
| Migrations in CI | Auto-apply, migrate-before-deploy | User chose automation; expand pattern + approval gate mitigate the no-rollback risk. | Plan (user) |
| Triggers | PR = gate; push-to-main = gate + deploy | Pre-merge validation plus auto-deploy on merge. | Plan |
| Runner → Azure SQL | Temp firewall rule per run (added, then removed `if: always()`) | GitHub-hosted runner IPs are ephemeral; no standing exposure. | Plan |
| Approval | `production` GitHub environment + required reviewer | infra.md "first prod publish human-only"; human checkpoint before each no-rollback migrate+deploy. | Infra/Plan |
| Post-deploy | `/health` smoke, fail loud, no auto-rollback | Closes F-01 4.2/4.6; surfaces broken deploys; rollback stays manual (no slots). | Plan |
| Env scope | Production only | Delivers F-04 with least machinery; staging is a documented later option. | Plan |

## Scope

**In scope:** `.github/workflows/deploy.yml` (build-test + gated deploy jobs); a `.github/DEPLOY_SETUP.md`
runbook for the out-of-band OIDC/roles/secrets/environment setup + prod `Jwt__SigningKey`; auto-applied
`AddIdentity` migration; post-deploy `/health` smoke; deploy-plan.md updates.

**Out of scope:** staging environment; auto-rollback; deployment slots / tier upgrade; self-hosted
runners; any app behavior change; Key Vault; secret values in the repo.

## Architecture / Approach

One workflow, two jobs. `build-test` (windows-latest): setup .NET 9 + Node 22 → build → `dotnet test`
(LocalDB) → web `npm test` → `dotnet publish -c Release` → upload artifact. `deploy` (needs build-test,
push-to-main, `environment: production`, OIDC): add temp SQL firewall rule → `dotnet ef database update`
(before code) → remove rule (`if: always()`) → `azure/webapps-deploy@v3` → `/health` smoke.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Prerequisites + setup doc | OIDC app, roles, GitHub secrets/environment, prod `Jwt__SigningKey`; `.github/DEPLOY_SETUP.md` | Mostly manual Azure AD/portal work; thin automated verification |
| 2. Build + test gate | `build-test` job, verifiable on a PR without touching prod | Windows runner speed; vitest/LocalDB running headless in CI |
| 3. Gated deploy + migrate + smoke | `deploy` job: migrate-first → deploy → `/health` smoke | First run does a real no-rollback prod migration; firewall-rule cleanup must run on failure |

**Prerequisites:** Phase 1's out-of-band setup must be complete before Phase 3 can succeed. Repo has a
GitHub remote (`run3th/homdutio`) and the live App Service + Azure SQL already exist.
**Estimated effort:** ~1–2 sessions; Phase 1 is human-gated setup, Phases 2–3 are workflow authoring.

## Open Risks & Assumptions

- **Auto-apply migrations on a no-rollback tier** — the chosen path; mitigated by migrate-before-deploy, the approval gate, and backward-compatible discipline. A breaking migration coupled to a deploy remains the thing to never do.
- **OIDC setup is manual and out-of-band** — Phase 3 fails until the Entra app, roles, secrets, and environment exist.
- **Temp firewall rule** — must be removed even on failure (`if: always()`); the OIDC identity needs SQL firewall-management rights.
- **First run is a big jump** (scaffold → F-01 + F-02) — safe because the live code touches no tables, but it's the first real prod migration + auth go-live.

## Success Criteria (Summary)

- A PR runs the build+test gate (and a bad test blocks it); merging to `main` auto-deploys after one approval.
- Production migrates + deploys with no long-lived secret in the repo, and deployed `/health` returns `Healthy` against Azure SQL.
- Prod auth works (F-02 live); no leftover firewall rule; DB still Basic.
