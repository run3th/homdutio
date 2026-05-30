---
deployed_at: 2026-05-29
platform: Azure App Service (Linux)
status: live
scope: scaffold-only (no database yet)
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
| Subscription | `<user>` (<user>) | id `<SUBSCRIPTION_ID>` |
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
