# CI Auto-Deploy — GitHub Actions Build+Test Gate → OIDC Deploy Implementation Plan

## Overview

Replace the manual Windows `dotnet publish` → `tar.exe` zip → `az webapp deploy` ritual with a single
GitHub Actions pipeline (`.github/workflows/deploy.yml`). Pull requests and pushes to `main` run a
**build + test gate** (build, xUnit tests against LocalDB, Angular vitest, Release publish). Merges to
`main` then run a **gated deploy**: behind a human-approved `production` environment, the job
authenticates via **OIDC**, applies EF Core migrations to Azure SQL **before** swapping code
(expand pattern), deploys the single artifact via `azure/webapps-deploy@v3`, and smoke-tests
`/health`. This is the foundation enabler **F-04** (`tech-stack.md` → `ci_default_flow:
auto-deploy-on-merge`); it gates no slice but removes the botched-manual-deploy footgun and clears
three inherited carry-overs (prod `Jwt__SigningKey`, the `AddIdentity` migration on Azure SQL, and
F-01's deferred deployed-`/health` verification 4.2/4.6).

## Current State Analysis

- **No CI exists** — there is no `.github/` directory and no `global.json`. Greenfield pipeline.
- **Single portable artifact**: `dotnet publish src/Homdutio.Api/Homdutio.Api.csproj -c Release` triggers the `BuildAngularSpa` MSBuild target (`Homdutio.Api.csproj:24-28` → `npm ci` + `npm run build`), bundling the Angular SPA into `wwwroot`. The CI runner therefore needs **both .NET 9 and Node** (Angular 21 ⇒ Node ≥20.19/22.12). The published framework-dependent artifact is OS-portable and runs on the Linux App Service even if built on Windows.
- **Live target** (`deploy-plan.md`): App Service `homdutio`, RG `homdutio-rg`, plan `homdutio-plan` (B1 Linux, Poland Central), HTTPS-only, `SCM_DO_BUILD_DURING_DEPLOYMENT=false`, subscription id held locally (not committed). **No deployment slots on B1** → rollback = redeploy the prior artifact by hand.
- **Tests are LocalDB-backed**: `tests/Homdutio.Data.Tests/PersistenceSmokeTests.cs` and `tests/Homdutio.Api.Tests/AuthApiFactory.cs` both hardcode `Server=(localdb)\MSSQLLocalDB`. LocalDB is **Windows-only** → the test job runs on `windows-latest` (which ships MSSQLLocalDB) so no test code changes are needed.
- **Migration discipline** (`src/Homdutio.Data/MIGRATIONS.md`, `infrastructure.md` risk register): migrations are backward-compatible and were applied out-of-band; auto-migrate-on-startup was explicitly rejected (no rollback on B1). This plan auto-applies migrations **in the pipeline** (user decision) but mitigates the risk via migrate-before-deploy ordering + the human approval gate.
- **The live artifact is stale**: it predates `/health`, the DbContext, and all auth code — it's the original scaffold with `/weatherforecast`. So the first run of this pipeline deploys a large jump (F-01 + F-02) and applies `AddIdentity` to a DB that still only has `InitialCreate`/`SchemaProbes`. Because the live code touches no tables, applying `AddIdentity` first (which drops `SchemaProbes` and adds `AspNet*`) is safe.
- **Azure SQL firewall** currently has only `AllowAzureServices` (0.0.0.0). A GitHub-hosted runner cannot reliably reach Azure SQL through it, so the migration step opens a temporary IP rule and removes it.

### Key Discoveries:

- **OIDC is the chosen, infra-preferred credential** (`infrastructure.md` Operational Story + risk register) — no long-lived secret in the repo. Requires a one-time Entra app registration + federated credentials + role assignments, done out-of-band (Phase 1).
- **Migration auth ≠ ARM auth**: OIDC authenticates the workflow to Azure ARM (deploy, firewall-rule mgmt). `dotnet ef database update` connects to **SQL** and needs a connection string — supplied as a `production`-environment GitHub secret, used only by the migrate step.
- **`windows-latest` preserves LocalDB parity** — zero changes to the two test projects; `sqllocaldb` is preinstalled.
- **`BuildAngularSpa` runs only in Release** (`Condition=" '$(Configuration)' == 'Release' "`) — the gate's `dotnet publish -c Release` is what compiles+bundles the SPA; a broken `ng build` fails the gate there.

## Desired End State

A merge to `main` automatically: runs the build+test gate; pauses for one-click `production` approval;
then migrates Azure SQL, deploys the current build to `homdutio`, and confirms
`https://homdutio.azurewebsites.net/health` returns `Healthy`. PRs run the same gate without deploying.
No long-lived deploy credential lives in the repo. The manual `tar.exe`/`az webapp deploy` ritual is
retired (kept only as the documented break-glass rollback).

**Verification:** a PR shows the `build-test` check (green on a clean branch); a merge to `main` runs
`build-test` → (approved) `deploy`, applies `AddIdentity` to Azure SQL, deploys, and the post-deploy
`/health` smoke returns `Healthy`; no secret values appear in tracked files.

## What We're NOT Doing

- **No staging environment** — production-only (decision: Env scope). A `homdutio-staging` App Service stays a documented later option per `infrastructure.md`.
- **No automatic rollback** — on smoke failure the run fails loudly; rollback remains a manual redeploy of the prior artifact (no slots on B1). (decision: Smoke test)
- **No deployment slots / Standard-tier upgrade** — out of scope (cost; `infrastructure.md`).
- **No self-hosted runners** — GitHub-hosted `windows-latest` only.
- **No app behavior changes** — this slice ships no API/UI features; it only automates build/test/deploy. (The code it deploys — F-01 + F-02 — already exists and is tested.)
- **No secret values in the repo** — all credentials/connection strings live in GitHub secrets / App Service settings, authored out-of-band.
- **No Key Vault** — App Service settings + GitHub secrets per `infrastructure.md`; Key Vault is a later option.

## Implementation Approach

Land the pieces in increasing blast radius. Phase 1 is the out-of-band Azure/GitHub setup that the
workflow depends on, captured as a reproducible committed doc — nothing in the repo runs yet. Phase 2
adds the build+test gate, which is fully verifiable on a PR **without touching prod**. Phase 3 adds the
gated deploy job (migrate-first → deploy → smoke), the only part that contacts Azure, behind the
human-approved `production` environment. This sequencing lets the gate be proven safe before the deploy
path is armed.

## Critical Implementation Details

- **Migrate-before-deploy (expand pattern).** The `deploy` job applies `dotnet ef database update` **before** `azure/webapps-deploy`. If the migration fails, the job stops before swapping code, leaving the (working) prior build live. This only stays safe while migrations are backward-compatible (`MIGRATIONS.md`) — the standing discipline this plan must not break.
- **Temporary SQL firewall rule must be removed even on failure.** The step that adds the runner-IP rule must be paired with a removal step that runs `if: always()`, so a failed migration never leaves the runner IP allowlisted.
- **OIDC needs `permissions: id-token: write`** at the workflow/job level, plus `contents: read`. Without the `id-token` permission, `azure/login` cannot request the federated token.
- **Deploy job must check out source for `dotnet ef`.** The build artifact is the published app, not the EF tooling; the deploy job checks out the repo and runs `dotnet ef database update --project src/Homdutio.Data --startup-project src/Homdutio.Api` with the prod connection string from the environment secret.
- **Concurrency guard.** Use a `concurrency` group on the deploy so two rapid merges can't race a migrate+deploy against the same single-instance app.

## Phase 1: Azure OIDC + GitHub Prerequisites (Setup + Doc)

### Overview

Establish, out-of-band, everything the workflow depends on: an Entra app with federated credentials,
role assignments for deploy + SQL firewall management, GitHub secrets, the protected `production`
environment, and the prod `Jwt__SigningKey` app setting (F-02 carry-over). Capture every step as a
reproducible committed doc so the setup is auditable and repeatable.

### Changes Required:

#### 1. Setup runbook doc

**File**: `.github/DEPLOY_SETUP.md` (new)

**Intent**: Record the exact, reproducible `az` + `gh` commands and portal steps to provision the OIDC identity, roles, secrets, environment, and app setting — the human executes these once; the doc is the audit trail.

**Contract**: A runbook covering: (a) `az ad app create` + service principal + `az ad app federated-credential create` for subjects `repo:run3th/homdutio:ref:refs/heads/main`, `repo:run3th/homdutio:pull_request`, and `repo:run3th/homdutio:environment:production`; (b) role assignments — **Website Contributor** (or Contributor) on the `homdutio` web app for deploy, and a role permitting SQL firewall-rule management on `homdutio-sql` (e.g. **SQL Server Contributor** / Contributor on the server); (c) GitHub repo secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`; (d) a `production` GitHub **Environment** with a required reviewer, holding an environment secret `AZURE_SQL_CONNECTION_STRING` (admin connection used only by the migrate step); (e) set the prod app setting `Jwt__SigningKey` via `az webapp config appsettings set` (32+ char value, never committed). No secret values appear in the doc — only placeholders and commands.

#### 2. Reference the setup from the deploy record

**File**: `context/deployment/deploy-plan.md`

**Intent**: Point the deployment audit trail at the new runbook and note that automated deploy is being introduced.

**Contract**: Append a short dated entry under the existing record referencing `.github/DEPLOY_SETUP.md` and the move from manual zip to GitHub Actions. No secrets.

### Success Criteria:

#### Automated Verification:

- Setup doc exists: `.github/DEPLOY_SETUP.md` is present
- No secret values committed: `git grep -iE "SigningKey|password|AccountKey|BEGIN .*PRIVATE KEY" -- .github/ context/` shows only placeholders/keys names, no real values

#### Manual Verification:

- Entra app + federated credentials exist for the main, pull_request, and production-environment subjects.
- The SP has deploy rights on `homdutio` and firewall-rule rights on `homdutio-sql`.
- GitHub repo secrets (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`) and the `production` environment (required reviewer + `AZURE_SQL_CONNECTION_STRING`) are configured.
- The prod `Jwt__SigningKey` App Service setting is set (≥32 chars), confirmed via `az webapp config appsettings list` (name present; value not echoed to logs).

**Implementation Note**: This phase performs cloud identity/credential setup (human-gated per `infrastructure.md` approval policy). Confirm all manual items are done before proceeding — Phase 3 cannot succeed without them.

---

## Phase 2: Build + Test Gate (`build-test` job)

### Overview

Author the workflow with a `build-test` job that runs on pull requests and pushes to `main`: set up
.NET 9 + Node, build, run the xUnit suites against LocalDB, run the Angular vitest suite, produce the
Release artifact, and upload it. No contact with Azure — fully verifiable on a PR.

### Changes Required:

#### 1. Workflow file + build-test job

**File**: `.github/workflows/deploy.yml` (new)

**Intent**: Establish the gate that every PR and main push must pass, producing the deployable artifact.

**Contract**: Triggers `on: pull_request` (branches: main) and `on: push` (branches: main). Top-level `permissions: { contents: read, id-token: write }`. Job `build-test` runs on `windows-latest` and: checks out; `actions/setup-dotnet` (9.0.x); `actions/setup-node` (Node 22) with npm cache on `web/package-lock.json`; ensures LocalDB is started (`sqllocaldb start MSSQLLocalDB`); `dotnet restore`/`dotnet build -c Release Homdutio.sln`; `dotnet test Homdutio.sln -c Release` (LocalDB, no overrides); `npm ci` + `npm test` in `web/` (vitest/jsdom, headless); `dotnet publish src/Homdutio.Api/Homdutio.Api.csproj -c Release -o ./publish`; `actions/upload-artifact` of `./publish` for the deploy job. Pin action major versions.

### Success Criteria:

#### Automated Verification:

- Workflow is valid and runs: opening a PR (or pushing a branch) triggers `build-test` and it completes green.
- The gate actually gates: `dotnet test` and `npm test` both execute and pass in the run logs.
- The Release publish runs `BuildAngularSpa` (SPA bundled into the published `wwwroot`) and uploads the artifact.

#### Manual Verification:

- A deliberately failing test (tried locally or on a scratch branch) fails the `build-test` check — confirming the gate blocks bad builds.
- The job completes in an acceptable time on `windows-latest`.

**Implementation Note**: After the gate is green on a PR, pause for confirmation that it behaves as a real gate before arming the deploy path in Phase 3.

---

## Phase 3: Deploy + Migrate + Smoke (`deploy` job, gated)

### Overview

Add the `deploy` job: gated on the `production` environment (human approval), running only on pushes to
`main` after `build-test`. It logs in via OIDC, opens a temporary SQL firewall rule, applies migrations
(before code), removes the rule, deploys the artifact to `homdutio`, and smoke-tests `/health`.

### Changes Required:

#### 1. Deploy job

**File**: `.github/workflows/deploy.yml`

**Intent**: Perform the gated, migrate-first production deploy with post-deploy verification.

**Contract**: Job `deploy` with `needs: build-test`, `if: github.event_name == 'push' && github.ref == 'refs/heads/main'`, `environment: production`, `runs-on: windows-latest`, a `concurrency` group (e.g. `deploy-prod`, cancel-in-progress false). Steps in order: checkout; `actions/setup-dotnet` (9.0.x); `azure/login@v2` (OIDC: client/tenant/subscription from secrets); **add temp firewall rule** for the runner's public IP on `homdutio-sql` via `az sql server firewall-rule create`; `dotnet ef database update --project src/Homdutio.Data --startup-project src/Homdutio.Api` using `ConnectionStrings__DefaultConnection` from the `AZURE_SQL_CONNECTION_STRING` environment secret; **remove the firewall rule** in a step with `if: always()`; download the `build-test` artifact; `azure/webapps-deploy@v3` (`app-name: homdutio`, the published package); **post-deploy smoke**: poll `GET https://homdutio.azurewebsites.net/health` expecting `200`/`Healthy`, failing the job otherwise.

#### 2. Update deploy record

**File**: `context/deployment/deploy-plan.md`

**Intent**: Record that auto-deploy is live and which carry-overs it closed.

**Contract**: Append a dated entry: pipeline now owns deploy; OIDC identity in use; `AddIdentity` applied to Azure SQL via the pipeline; prod `Jwt__SigningKey` set; deployed `/health` verified (closes F-01 4.2/4.6). No secrets.

### Success Criteria:

#### Automated Verification:

- On a push to `main`, `deploy` runs after `build-test` and (post-approval) completes green.
- The migrate step applies cleanly (`database update` exits 0); the firewall-rule removal step runs even on failure (`if: always()`).
- The post-deploy smoke step gets `Healthy` from `https://homdutio.azurewebsites.net/health` (200).

#### Manual Verification:

- The `production` environment paused for approval before the deploy ran (human gate works).
- After the run, `https://homdutio.azurewebsites.net/health` returns `Healthy` against real Azure SQL (closes F-01 4.2/4.6), and `POST /api/auth/register` + `/login` work in prod (F-02 live, `Jwt__SigningKey` effective).
- The Azure SQL firewall has **no** leftover runner-IP rule after the run.
- `az sql db show` still reports Basic; no unexpected resources/cost.

**Implementation Note**: The first successful run performs the real production migration + first auto-deploy and is human-gated. Confirm the manual items (approval pause, deployed `/health`, prod auth, firewall cleanup) before closing the slice.

---

## Testing Strategy

### Unit / Integration Tests:

- No new app tests — the deployed code (F-01 + F-02) is already covered. The pipeline's job is to *run* those suites (`dotnet test`, `npm test`) as the gate.

### Pipeline verification:

- PR triggers `build-test` only (no deploy); push to main triggers `build-test` → gated `deploy`.
- A failing test fails the gate and blocks deploy.
- Migrate-first ordering: a migration failure aborts before code swap; the firewall rule is always removed.
- Post-deploy `/health` smoke gates run success.

### Manual Testing Steps:

1. Open a PR with a trivial change → confirm `build-test` runs and passes; no deploy job runs.
2. Merge to main → confirm the run pauses at the `production` approval gate.
3. Approve → confirm migrate → deploy → smoke all succeed; `https://homdutio.azurewebsites.net/health` is `Healthy`.
4. Hit prod `POST /api/auth/register` + `/login` → confirm F-02 works live (signing key set).
5. `az sql server firewall-rule list --server homdutio-sql -g homdutio-rg` → confirm no leftover runner-IP rule.
6. `git grep` for secret values across tracked files → none.

## Performance Considerations

`windows-latest` runs are slower than Linux, acceptable for a low-frequency solo cadence. The gate runs
the full build + both test suites + a Release publish (Angular build) on every PR/push — a few minutes;
fine at this volume. The deploy adds an approval wait (human) + migrate + deploy + smoke poll.

## Migration Notes

This pipeline **auto-applies** EF migrations (user decision), departing from `MIGRATIONS.md`'s pure
out-of-band stance — mitigated by: (a) migrate **before** code deploy (expand pattern), (b) the
`production` human approval gate, (c) the standing backward-compatible-migration discipline (never
couple a breaking change to a deploy), and (d) no auto-rollback (a failed migrate aborts before code
swap; recovery stays manual). The first run applies `AddIdentity` (drops `SchemaProbes`, adds `AspNet*`)
to a prod DB whose live code touches no tables, so it is safe. Future breaking changes must still be
sequenced as backward-compatible steps. `MIGRATIONS.md` should later be updated to describe the
pipeline path (out of scope here unless quick).

## References

- Roadmap item: `context/foundation/roadmap.md` → F-04 (`ci-auto-deploy`)
- Deploy baseline + manual ritual + `Compress-Archive` gotcha: `context/deployment/deploy-plan.md`
- Credential/OIDC, approval policy, no-slots rollback: `context/foundation/infrastructure.md`
- Build pipeline / SPA bundling: `src/Homdutio.Api/Homdutio.Api.csproj` (`BuildAngularSpa`)
- Migration discipline: `src/Homdutio.Data/MIGRATIONS.md`
- Carry-overs closed: F-01 plan items 4.2/4.6 (`context/changes/persistence-baseline/plan.md`); F-02 prod `Jwt__SigningKey` + `AddIdentity` (`context/changes/auth-identity-plumbing/plan.md`)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Azure OIDC + GitHub Prerequisites

#### Automated

- [x] 1.1 Setup doc exists: `.github/DEPLOY_SETUP.md` present — de7aca4
- [x] 1.2 No secret values committed (`git grep` placeholders only) — de7aca4

#### Manual

- [ ] 1.3 Entra app + federated credentials (main, pull_request, production) exist
- [ ] 1.4 SP has deploy rights on `homdutio` + firewall-rule rights on `homdutio-sql`
- [ ] 1.5 GitHub secrets + `production` environment (reviewer + `AZURE_SQL_CONNECTION_STRING`) configured
- [ ] 1.6 Prod `Jwt__SigningKey` App Service setting set (≥32 chars)

### Phase 2: Build + Test Gate

#### Automated

- [ ] 2.1 PR/branch push triggers `build-test` and it completes green
- [x] 2.2 Gate runs `dotnet test` and `npm test` (both execute + pass in logs) — 8e13f4c
- [x] 2.3 Release publish runs `BuildAngularSpa` and uploads the artifact — 8e13f4c

#### Manual

- [ ] 2.4 A deliberately failing test fails the `build-test` check (gate blocks bad builds)
- [ ] 2.5 Job completes in acceptable time on `windows-latest`

### Phase 3: Deploy + Migrate + Smoke

#### Automated

- [ ] 3.1 Push to main runs `deploy` after `build-test` and completes green (post-approval)
- [ ] 3.2 Migrate applies cleanly (exit 0); firewall-rule removal runs `if: always()`
- [ ] 3.3 Post-deploy smoke gets `Healthy` from deployed `/health` (200)

#### Manual

- [ ] 3.4 `production` environment paused for approval before deploy
- [ ] 3.5 Deployed `/health` Healthy vs Azure SQL + prod `register`/`login` work (closes F-01 4.2/4.6; F-02 live)
- [ ] 3.6 No leftover runner-IP firewall rule on `homdutio-sql`
- [ ] 3.7 `az sql db show` still Basic; no unexpected resources/cost
