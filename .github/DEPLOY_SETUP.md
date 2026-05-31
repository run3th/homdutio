# Deploy Setup Runbook — OIDC + GitHub Prerequisites (F-04 `ci-auto-deploy`)

> **Audit trail, run once.** This is the reproducible record of the out-of-band Azure +
> GitHub setup that `.github/workflows/deploy.yml` depends on. A human runs these steps once;
> the workflow then deploys automatically on merge to `main`.
>
> **No secret values live in this file.** Every credential, password, and connection string
> below is a `<PLACEHOLDER>`. Real values go only into Azure App Service settings and GitHub
> Secrets — never into a tracked file.

## What this provisions

1. An Entra (Azure AD) **app registration + service principal** with **federated credentials**
   (OIDC) — no long-lived secret in the repo.
2. **Role assignments** so the workflow can deploy the web app and manage the SQL server's
   firewall rules.
3. **GitHub repo secrets** (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`).
4. A protected **`production` GitHub Environment** (required reviewer) holding the
   `AZURE_SQL_CONNECTION_STRING` environment secret used only by the migrate step.
5. The prod **`Jwt__SigningKey`** App Service setting (F-02 carry-over).

## Fixed identifiers (from `context/deployment/deploy-plan.md`)

| Thing | Value |
|---|---|
| GitHub repo | `run3th/homdutio` |
| Subscription id | `<SUBSCRIPTION_ID>` (see `az account show`; not committed) |
| Resource group | `homdutio-rg` (region `polandcentral`) |
| Web app | `homdutio` |
| SQL logical server | `homdutio-sql` |
| SQL database | `homdutio-db` |

Set shell variables once (PowerShell or bash — examples below use bash):

```bash
SUBSCRIPTION_ID="<your-subscription-id>"   # az account show --query id -o tsv
RG="homdutio-rg"
WEBAPP="homdutio"
SQL_SERVER="homdutio-sql"
REPO="run3th/homdutio"
APP_NAME="homdutio-github-oidc"   # Entra app display name (arbitrary)

az account set --subscription "$SUBSCRIPTION_ID"
```

---

## Step 1 — Entra app registration + service principal

```bash
# Create the app registration; capture the appId (this becomes AZURE_CLIENT_ID).
APP_ID=$(az ad app create --display-name "$APP_NAME" --query appId -o tsv)

# Create the service principal for that app (the identity role assignments attach to).
az ad sp create --id "$APP_ID"

# Tenant id (becomes AZURE_TENANT_ID).
TENANT_ID=$(az account show --query tenantId -o tsv)

echo "AZURE_CLIENT_ID=$APP_ID"
echo "AZURE_TENANT_ID=$TENANT_ID"
echo "AZURE_SUBSCRIPTION_ID=$SUBSCRIPTION_ID"
```

## Step 2 — Federated credentials (OIDC subjects)

Three subjects, matching how the workflow triggers. The `production` subject is what lets the
gated `deploy` job (which runs under `environment: production`) mint a token.

```bash
# Push / migrate context: main branch
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:run3th/homdutio:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'

# Pull-request context (build-test gate only; no Azure contact, but kept for parity)
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-pr",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:run3th/homdutio:pull_request",
  "audiences": ["api://AzureADTokenExchange"]
}'

# Production environment context (the gated deploy job authenticates under this subject)
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-env-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:run3th/homdutio:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

## Step 3 — Role assignments

Two grants: deploy rights on the web app, and firewall-rule management on the SQL server.
Scope each as narrowly as practical.

```bash
SP_OBJECT_ID=$(az ad sp show --id "$APP_ID" --query id -o tsv)

WEBAPP_SCOPE="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RG/providers/Microsoft.Web/sites/$WEBAPP"
SQL_SCOPE="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RG/providers/Microsoft.Sql/servers/$SQL_SERVER"

# Deploy the web app. "Website Contributor" is the least-privilege built-in for app deploy.
az role assignment create \
  --assignee-object-id "$SP_OBJECT_ID" --assignee-principal-type ServicePrincipal \
  --role "Website Contributor" --scope "$WEBAPP_SCOPE"

# Manage SQL firewall rules (the migrate step opens/removes a temp runner-IP rule).
# "SQL Server Contributor" permits firewall-rule create/delete on the server.
az role assignment create \
  --assignee-object-id "$SP_OBJECT_ID" --assignee-principal-type ServicePrincipal \
  --role "SQL Server Contributor" --scope "$SQL_SCOPE"
```

> If "Website Contributor" proves insufficient for `azure/webapps-deploy@v3` in practice, fall
> back to "Contributor" on `$WEBAPP_SCOPE` (broader, still resource-scoped). Note the choice
> here when you do.

## Step 4 — GitHub repo secrets

The OIDC identifiers are not secret-sensitive but are stored as secrets by convention so the
workflow references them uniformly. Use the `gh` CLI (or the repo Settings UI).

```bash
gh secret set AZURE_CLIENT_ID       --repo "$REPO" --body "$APP_ID"
gh secret set AZURE_TENANT_ID       --repo "$REPO" --body "$TENANT_ID"
gh secret set AZURE_SUBSCRIPTION_ID --repo "$REPO" --body "$SUBSCRIPTION_ID"
```

## Step 5 — `production` GitHub Environment + migrate secret

The gated deploy job runs under `environment: production`. Configure it with a **required
reviewer** (the human approval gate) and the **admin SQL connection string** the migrate step
uses. The connection string is an *environment* secret (scoped to `production`), not a repo
secret, so only the gated job can read it.

Create + protect the environment (UI: **Settings → Environments → New environment →
`production` → Required reviewers → add yourself**), then set the environment secret:

```bash
# Environment secret — value is the admin connection string to homdutio-db.
# Format (do NOT commit a real one):
#   Server=tcp:homdutio-sql.database.windows.net,1433;Database=homdutio-db;User ID=<ADMIN_USER>;Password=<ADMIN_PWD>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
gh secret set AZURE_SQL_CONNECTION_STRING --repo "$REPO" --env production --body "<ADMIN_CONNECTION_STRING>"
```

> The workflow's migrate step maps this into `ConnectionStrings__DefaultConnection` so
> `dotnet ef database update` connects to Azure SQL. This is **SQL auth**, separate from the
> OIDC/ARM auth above.

## Step 6 — Prod `Jwt__SigningKey` App Service setting (F-02 carry-over)

Set the signing key the deployed API uses to sign/validate JWTs. **≥32 chars, never committed.**

```bash
# Generate a strong key locally and set it; the value is never echoed into a tracked file.
az webapp config appsettings set --name "$WEBAPP" --resource-group "$RG" \
  --settings Jwt__SigningKey="<32+_CHAR_RANDOM_SECRET>"
```

> `__` (double underscore) is the .NET config nesting separator, so `Jwt__SigningKey` binds to
> `Jwt:SigningKey`. Confirm the name is present (without echoing the value):
> `az webapp config appsettings list --name "$WEBAPP" --resource-group "$RG" --query "[?name=='Jwt__SigningKey'].name" -o tsv`

---

## Verification checklist (manual gate — Phase 1.3–1.6)

- [ ] **1.3** Entra app + federated credentials exist for `main`, `pull_request`, and
      `environment:production`:
      `az ad app federated-credential list --id "$APP_ID" --query "[].subject" -o tsv`
- [ ] **1.4** SP has deploy rights on `homdutio` and firewall-rule rights on `homdutio-sql`:
      `az role assignment list --assignee "$APP_ID" --all -o table`
- [ ] **1.5** GitHub repo secrets (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
      `AZURE_SUBSCRIPTION_ID`) set, and the `production` environment has a required reviewer +
      `AZURE_SQL_CONNECTION_STRING`: `gh secret list --repo "$REPO"` and
      `gh secret list --repo "$REPO" --env production`.
- [ ] **1.6** Prod `Jwt__SigningKey` App Service setting present (≥32 chars), name confirmed
      via `az webapp config appsettings list` (value not echoed to logs).

Once all four are checked, Phase 3's deploy job has everything it needs. Phase 2 (the
build-test gate) does **not** depend on any of this — it contacts no Azure resource.

## Provisioning notes — actual run (2026-05-31)

The out-of-band setup was executed against the live subscription. What deviated from the
idealized CLI steps above, and why:

- **App registration + the 3 federated credentials were created in the Azure Portal**, not the
  CLI. The tenant's **Conditional Access policy blocks Microsoft Graph token issuance for the
  Azure CLI** (`AADSTS53003`), and interactive device-code / browser re-auth would not complete
  on the build machine. ARM operations from the CLI are unaffected. (App registration display
  name: `homdutio-github-oidc`; client id stored only in the `AZURE_CLIENT_ID` GitHub secret.)
- **Role assignments were done via the Portal IAM blade** (`homdutio` → Website Contributor;
  `homdutio-sql` → SQL Server Contributor), targeting the app's **service principal**. CLI role
  assignment was not viable: the service-principal lookup is a Graph call (blocked), and this
  CLI build's `az role assignment create` returns a spurious `MissingSubscription` (an `az rest`
  PUT works but needs the SP object id, which Graph would have to resolve).
- **GitHub repo secrets and the prod `Jwt__SigningKey`** were set via `gh` / `az` (ARM + GitHub
  APIs are not Graph-gated).
- **Approval gate limitation.** This is a **private repo on the GitHub Free plan**, where
  environment **required-reviewer** protection rules are unavailable (HTTP 422). The
  `production` environment exists and still scopes the `AZURE_SQL_CONNECTION_STRING` secret to
  deploy-only and satisfies the `environment:production` OIDC subject — but it does **not**
  enforce a human approval pause. Decision (2026-05-31): **proceed without the environment
  approval gate.** On a solo repo, the deliberate `merge/push to main` is itself the deploy
  decision; remaining safety = deploy-only-on-main, migrate-before-deploy, backward-compatible
  migrations, and the `/health` smoke. To restore a hard gate later: make the repo public, or
  upgrade to GitHub Pro/Team, then add the required reviewer to the `production` environment.

## Rotation / teardown notes

- **Rotate `Jwt__SigningKey`**: regenerate, `az webapp config appsettings set` again; in-flight
  tokens invalidate.
- **Rotate the SQL admin password**: update `AZURE_SQL_CONNECTION_STRING` env secret to match.
- **Revoke deploy access**: delete the federated credentials or the role assignments; or
  `az ad app delete --id "$APP_ID"`.
