---
deployed_at: 2026-05-29
platform: Azure App Service (Linux)
status: live
scope: scaffold + persistence baseline (Azure SQL Basic provisioned 2026-05-30)
live_url: https://homdutio.azurewebsites.net
---

# Deploy Plan & Record — Homdutio first deployment

This is the audit trail of the first deployment (what was planned and what actually
happened), consumed downstream by milestone planning as ground truth for "what's already
deployed and which resources/secrets are wired." Decision context lives in
`context/foundation/infrastructure.md`.

## What was deployed

The single .NET 9 artifact (ASP.NET Core API + Angular SPA served from `wwwroot`), built
locally and zip-deployed. **Scaffold only** — no database, EF, Identity, or `/api`
controllers yet (only the template `/weatherforecast`). The SQL database from
`infrastructure.md` was deliberately **deferred to `/10x-implement`** to avoid paying for an
idle DB.

## Live resources (Azure)

| Resource | Name | Detail |
|---|---|---|
| Subscription | _redacted_ | id held locally (`az account show`); not committed |
| Resource group | `homdutio-rg` | region **Poland Central** (`polandcentral`) |
| App Service plan | `homdutio-plan` | Linux, **B1** |
| Web app | `homdutio` | runtime `DOTNETCORE:9.0`, **HTTPS-only**, `SCM_DO_BUILD_DURING_DEPLOYMENT=false` |
| **Live URL** | https://homdutio.azurewebsites.net | |

No secrets are wired yet (no DB connection string this round).

## Commands that were run (reproducible)

```bash
# Build (triggers Angular build into wwwroot via the BuildAngularSpa MSBuild target)
dotnet publish src/Homdutio.Api/Homdutio.Api.csproj -c Release -o ./publish

# Zip — MUST use forward-slash entries (see gotcha below)
tar.exe -a -c -f publish.zip -C publish .

# Provision
az group create --name homdutio-rg --location polandcentral
az appservice plan create --name homdutio-plan --resource-group homdutio-rg --is-linux --sku B1 --location polandcentral
az webapp create --name homdutio --resource-group homdutio-rg --plan homdutio-plan --runtime "DOTNETCORE:9.0"

# Harden + deploy
az webapp update --name homdutio --resource-group homdutio-rg --https-only true
az webapp config appsettings set --name homdutio --resource-group homdutio-rg --settings SCM_DO_BUILD_DURING_DEPLOYMENT=false
az webapp deploy --name homdutio --resource-group homdutio-rg --src-path ./publish.zip --type zip
```

## Verification (all passed 2026-05-29)

- `GET /` → 200, serves Angular shell (`<app-root>`)
- `GET /weatherforecast` → 200, JSON array (proves the .NET process is live)
- `GET /some/client/route` → 200, `index.html` fallback (Angular router works)
- `GET http://…` → 301 redirect to HTTPS

## Gotcha encountered (carry forward to CI/CD)

**`Compress-Archive` (PowerShell) produces ZIP entries with backslash separators**, which
Azure's Linux Kudu `rsync` rejects (`failed to stat ".../wwwroot\index.html": Invalid argument
(22)`) — the first deploy failed with HTTP 400 for this reason. Fix: zip with `tar.exe`
(bsdtar, spec-compliant forward slashes) or let the CLI/GitHub Action build the zip. When the
GitHub Actions auto-deploy workflow is added, this is a non-issue (Linux runners zip correctly),
but any manual Windows deploy must avoid `Compress-Archive`.

## Operational quick-reference

- **Logs**: `az webapp log tail --name homdutio --resource-group homdutio-rg`
- **Restart**: `az webapp restart --name homdutio --resource-group homdutio-rg`
- **Rollback** (no slots on B1): redeploy the previous `publish.zip`
- **Tear down everything**: `az group delete --name homdutio-rg --yes`

## Next steps (not done this round)

1. `/10x-implement` — build the data layer (Identity, households, tasks, board); then provision
   the **provisioned Basic Azure SQL** DB per `infrastructure.md` and wire the connection string
   into App Service settings / Key Vault.
2. ✅ Resolved (2026-05-30): the **WebSockets-vs-polling** contract question landed on **polling**
   (short-interval client refresh meets the NFR-1 ≤5s freshness contract on the single-instance B1
   MVP without a stateful connection or scale-out backplane; aligns with `tech-stack.md`
   `has_realtime: false`). SignalR is the reversible post-validation upgrade. See `roadmap.md` F-03.
3. Add **GitHub Actions auto-deploy on merge** (`azure/webapps-deploy@v3` + OIDC) per the
   stack's `ci_default_flow`, replacing manual deploys.
4. Add a **budget alert** on the personal subscription (infra risk-register mitigation).

## Update 2026-05-30 — Persistence baseline (F-01) provisioned

Change `persistence-baseline` wired EF Core 9 + `ApplicationDbContext` and provisioned the database
(the DB deliberately deferred at first deploy is now live).

### Resources added (Azure)

| Resource | Name | Detail |
|---|---|---|
| SQL logical server | `homdutio-sql` | `homdutio-sql.database.windows.net`, Poland Central, SQL auth (admin `homdutioadmin`) |
| SQL database | `homdutio-db` | **Basic** (provisioned, non-serverless), 2 GB / 5 DTU, ~$5/mo flat, status **Online** |
| Firewall rule | `AllowAzureServices` | `0.0.0.0` — lets the App Service reach the server |

- **Connection string**: wired as App Service connection-string `DefaultConnection` (type `SQLAzure`),
  read by the app as `ConnectionStrings:DefaultConnection`. Never committed (admin password held by the
  builder; locally it lives in user-secrets).
- **Migration**: `InitialCreate` (creates `__EFMigrationsHistory` + the throwaway `SchemaProbes` table)
  applied to Azure SQL via `dotnet ef database update`, out-of-band and backward-compatible per
  `src/Homdutio.Data/MIGRATIONS.md`. A temporary local-IP firewall rule was used for the apply and
  removed afterward.

### Verified
- `az sql db show` → service objective **Basic**, status **Online**, max size 2 GB.
- Azure SQL migration history present (`database update` reports already up to date).
- No connection string / admin password in any tracked repo file.

### Deferred / follow-ups
- **Deployed `/health` not yet verified against Azure SQL** (F-01 plan items 4.2/4.6): the live
  artifact predates the `/health` + DbContext code. Verify after the next deploy — via F-04 CI
  auto-deploy or a manual `dotnet publish` → `tar.exe` zip → `az webapp deploy` (mind the
  `Compress-Archive` backslash gotcha above).
- **Budget alert still not configured** — now recommended, since a flat ~$5/mo DB cost is live.
  Combined run-rate ≈ ~$18/mo (B1 compute + Basic SQL).
- **Watch the Basic 2 GB / 5 DTU ceiling** — plan the Standard S0 step before audit-trail growth
  bites (NFR-3 keeps records for the lifetime of the household).

## Update 2026-05-31 — CI auto-deploy introduced (F-04, in progress)

Change `ci-auto-deploy` replaces the manual Windows `dotnet publish` → `tar.exe` zip →
`az webapp deploy` ritual with a GitHub Actions pipeline (`.github/workflows/deploy.yml`): PRs
and pushes to `main` run a build + test gate; merges to `main` then (behind a human-approved
`production` environment) migrate Azure SQL, deploy the single artifact to `homdutio` via
**OIDC** (no long-lived secret in the repo), and smoke-test `/health`. The manual ritual above
is retained only as the documented break-glass rollback (no slots on B1).

The reproducible out-of-band setup (Entra app + federated credentials, role assignments, GitHub
secrets, the protected `production` environment, and the prod `Jwt__SigningKey` app setting) is
recorded in **`.github/DEPLOY_SETUP.md`** — run once, no secret values committed.

### Pipeline now owns deploy (gated `deploy` job authored)

`.github/workflows/deploy.yml` carries a `build-test` gate (PR + push to `main`) and a gated
`deploy` job (push to `main` only, behind the human-approved `production` environment). The
deploy job: logs in via **OIDC**; opens a temporary `homdutio-sql` firewall rule for the
runner IP (removed `if: always()`); applies EF migrations **before** the code swap (expand
pattern); deploys the single artifact to `homdutio` via `azure/webapps-deploy@v3`; and
smoke-tests `/health`.

**First approved run will close these carry-overs** (verify after that run; pending until
then): applies `AddIdentity` to Azure SQL via the pipeline; the prod `Jwt__SigningKey` setting
takes effect for live `register`/`login` (F-02); and deployed `/health` is verified against
Azure SQL (closes F-01 plan items 4.2/4.6). The live artifact before this run is the original
scaffold, so `AddIdentity` applies safely (live code touches no tables). No secrets in the repo.
