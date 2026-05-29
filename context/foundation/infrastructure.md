---
project: Homdutio
researched_at: 2026-05-29
recommended_platform: Azure App Service
runner_up: Railway
context_type: mvp
tech_stack:
  language: C# (.NET 9)
  framework: ASP.NET Core (webapi) + Angular SPA served from wwwroot
  runtime: .NET 9 (CLR, long-running server process)
---

## Recommendation

**Deploy on Azure App Service (Linux, .NET 9), with a co-located managed database (Azure SQL serverless free tier).**

Homdutio is a single .NET artifact — the Angular SPA is built into ASP.NET's `wwwroot/` and served by the same process — so the only real question is where one long-running ASP.NET Core process lives. Azure App Service scores Pass on all five agent-friendly criteria, is the platform the stack already named (`tech-stack.md` → `deployment_target: azure-app-service`), and is the one you already know (interview Q3), which is the decisive tie-break for a solo, after-hours, 3-week MVP. Co-location (Q5) is satisfied by same-vendor managed SQL, and single-region (Q4) matches the PRD's explicit no-multi-region non-goal. The one genuine tension is cost (Q2): the F1 free tier is unusable for a daily-use app, so the realistic floor is **B1 (~$13/mo)** compute plus a free serverless database — cheap, but not zero. That tension is recorded in full in the risk register below rather than hidden.

## Platform Comparison

A hard runtime filter is applied before scoring: ASP.NET Core needs a long-running .NET server process, which removes three of the six default candidates.

### Dropped by the hard runtime filter (not scored)

| Platform | Reason (checked 2026-05-29) | Status |
|---|---|---|
| Cloudflare Workers | V8-isolate runtime; JS/TS/Wasm only — cannot host a .NET server process. | GA, but incompatible |
| Vercel | Serverless functions are Node/Python/Go/Ruby; no first-class .NET server runtime. | GA, but incompatible |
| Netlify | JAMstack + JS serverless functions; no .NET runtime. | GA, but incompatible |

Because the Angular SPA is bundled into `wwwroot/` and served by ASP.NET, there is no separate static-frontend workload to place on an edge host — the edge trio is irrelevant to this architecture regardless of the runtime filter.

### Scoring matrix (surviving candidates)

| Platform | CLI-first | Managed/Serverless | Agent-readable docs | Stable deploy API | MCP / Integration | Total |
|---|---|---|---|---|---|---|
| **Azure App Service** | Pass | Pass | Pass | Pass | Pass | 5 Pass |
| **Railway** | Pass | Pass | Partial | Pass | Partial | 3 Pass / 2 Partial |
| **Render** | Partial | Pass | Partial | Pass | Partial | 2 Pass / 3 Partial |
| **Fly.io** | Pass | Pass | Pass | Pass | Partial | 4 Pass / 1 Partial |

**Per-platform notes:**

- **Azure App Service** — `az webapp` covers the full lifecycle (create, deploy, `log tail`, restart, config) — *Pass*. Fully managed PaaS: TLS, scaling, OS patching handled — *Pass*. Docs live in GitHub (MicrosoftDocs) and there is an official Azure MCP Server plus the Microsoft Learn MCP Server — *Pass* on both docs and integration. Deployment is deterministic via `az webapp deploy` / `azure/webapps-deploy` GitHub Action — *Pass*.
- **Railway** — `railway` CLI is strong (`railway up`, logs, variables) — *Pass*. Managed PaaS with managed Postgres (auto backups, connection vars) — *Pass*; note Railpack does **not** support .NET, so a Dockerfile is mandatory for every deploy. Docs are good HTML guides (incl. an ASP.NET Core guide) but not a published markdown/`llms.txt` corpus — *Partial*. Deterministic deploy via CLI/GitHub — *Pass*. MCP exists but is less established than Azure's — *Partial*.
- **Render** — Docker-native (any .NET image runs) and managed Postgres exist — *Pass* on managed. CLI is newer and many flows remain dashboard/`render.yaml`-centric — *Partial*. Docs are decent HTML, no famous agent-readable corpus — *Partial*. Deploy via deploy hooks/API/blueprint — *Pass*. MCP server exists (2025) — *Partial*. **Key caveat: the free Postgres expires after 90 days and free web services cold-start 30–60s** — both undercut a persistent, daily-use household app and the free PG directly conflicts with NFR-3 (audit durability "for the lifetime of the household").
- **Fly.io** — `flyctl` is excellent and docs are MDX on GitHub — *Pass* on CLI and docs. Managed VMs/machines with TLS — *Pass*. `fly deploy` is deterministic — *Pass*. No first-class MCP, GitHub Action available — *Partial*. **Dropped from the shortlist despite strong criteria** because its Managed Postgres base plan starts ~$38/mo, which poisons the cost-minimize (Q2) + co-location (Q5) combination; the cheaper unmanaged Fly Postgres fails the *managed* criterion by putting DB operations on a solo builder.

### Shortlisted Platforms

#### 1. Azure App Service (Recommended)

Only platform at 5/5 on the criteria. Decisive factors for *this* project: you already know it (Q3), it is the target the stack contract already committed to (no foundation drift), it offers same-vendor co-located managed SQL (Q5), and single-region operation matches the PRD non-goal (Q4). A forever-free Azure SQL serverless database (100k vCore-seconds/mo, 32 GB) keeps the data layer at $0 indefinitely, so the only unavoidable spend is ~$13/mo B1 compute.

#### 2. Railway

The strongest runner-up on developer experience for a .NET container: clean `railway` CLI, managed Postgres with EF Core migrations on startup, scale-to-usage billing (no paying for idle), and predictable low spend (~$5–10/mo). It loses to Azure only on familiarity, foundation alignment, and the slightly weaker docs/MCP signal — and it requires a hand-written Dockerfile (Railpack has no .NET path). It is the natural swap target if Azure's cost or rollback story proves unacceptable.

#### 3. Render

Cheapest nominal entry and Docker-native, but its free tier has two disqualifying caveats for this app: web services cold-start 30–60s after idle (bad for a daily board) and **free Postgres expires after 90 days** — which would silently violate NFR-3's lifetime-of-the-household durability requirement. On a paid basis (~$14/mo: $7 web + managed PG) it is comparable to the alternatives but with a weaker CLI than Railway, so it lands third.

## Anti-Bias Cross-Check: Azure App Service

### Devil's Advocate — Weaknesses

1. **The F1 free tier is a trap for this app.** 60 CPU-minutes/day, no Always On (cold starts), and no custom-domain SSL. A daily-use household board hits the quota and feels broken — the realistic floor is **B1 (~$13/mo)** from day one, so the "free Azure" framing collapses.
2. **The polling design fights the free database.** Polling (chosen to meet NFR-1's 5s freshness without WebSockets) generates steady query load; Azure SQL serverless *free* is metered in vCore-seconds (~100k/mo ≈ 28h of 1 vCore) with auto-pause, so steady polling can exhaust the grant before month-end and silently tip into billing, while auto-pause adds multi-second cold starts on the first action after idle.
3. **No safe rollback on the cheap tier.** Deployment slots (blue-green swap) require **Standard (~$70/mo)**, not Basic. On B1, rollback is "redeploy the previous build by hand."
4. **`az` CLI defaults are not the cheap ones.** `az postgres flexible-server create` / Azure SQL create default to *paid* SKUs; the free path requires explicit flags. An agent provisioning resources can easily create a silently-billed resource.
5. **Single coupled artifact.** Angular-in-`wwwroot` means one bad `ng build` takes down API + UI together — there is no independent frontend rollback.

### Pre-Mortem — How This Could Fail

The build started on F1 free, hit the 60-min/day CPU quota within days once the household actually used the board, got throttled, and felt broken. It moved to B1 ($13/mo, no slots). Azure SQL serverless "free forever" looked perfect — but the SPA's 5-second polling generated steady query load that, with serverless auto-resume churn, chewed through the 100k vCore-second monthly grant by the third week of each month; the DB began billing and auto-pausing mid-day, causing 10-second stalls on the first action after lunch. With no deployment slots on Basic, a botched EF migration during an 11pm after-hours deploy took the whole single-artifact app down, and recovery meant manually redeploying the prior build. Worse, an `az` command the agent ran months earlier had defaulted one resource to a paid SKU that nobody caught until the invoice arrived. The "cheap Azure" plan quietly became ~$40/mo of half-understood resources — the opposite of the cost-minimizing intent.

### Unknown Unknowns

- The Azure SQL "free serverless" math assumes *idle-heavy* usage; a polling frontend changes it fundamentally — not obvious from the "free forever" headline.
- Deployment slots are Standard-tier only; the cheap tier you will actually run has **no blue-green rollback**.
- `az ... create` commands default to **non-free SKUs** — "free" requires explicit flags, an easy agent miss that produces a silently-billed resource.
- Basic App Service has **no scale-to-zero**: you pay ~$13/mo even while the mostly-idle single-household app sits unused, unlike Railway/Render usage-based models.
- ASP.NET Identity cookie auth depends on the **data-protection key ring**; the day you scale past one instance, keys must persist to Blob/Key Vault or every restart logs everyone out — invisible at single-instance MVP, a footgun later.

## Operational Story

- **Preview deploys**: App Service does not auto-create per-PR preview URLs on Basic (slots are Standard-only). For MVP, use a second cheap App Service (e.g. `homdutio-staging`) deployed from a `staging` branch via GitHub Actions; treat its URL as the preview. No fork-PR previews — secrets would leak. Production publish stays a deliberate, separate action.
- **Secrets**: Connection strings and the data-protection/Identity config live in **App Service Application Settings** (env vars) and/or **Azure Key Vault** referenced from settings — never in the repo. The GitHub Actions deploy uses a scoped publish profile or an OIDC federated credential stored in **GitHub Secrets**; the agent never sees the raw secret in conversation. Rotation: regenerate in Azure, update the GitHub Secret, redeploy.
- **Rollback**: On B1 (no slots), rollback = redeploy the previous artifact: `az webapp deploy --src-path <prev.zip>` or re-run the prior successful GitHub Actions run. Time-to-revert ≈ one deploy (~1–3 min). **Caveat: EF Core migrations do not auto-roll-back** — a forward migration must be reversed with an explicit `dotnet ef migrations` down/script. Plan migrations to be backward-compatible.
- **Approval**: Human-only — first production publish, rotating the primary DB credential or Identity data-protection key, deleting the App Service or database, and any tier/SKU change (cost impact). Agent-unattended — read-only log tailing, staging deploys, listing resources, reading config (not secret values).
- **Logs**: `az webapp log tail --name homdutio --resource-group <rg>` for live stream; `az webapp log download` for archives. Deployment/runtime status via `az webapp show` and the `azure/webapps-deploy` action output. Optionally the Azure MCP Server for structured queries once log/diff questions recur.

## Risk Register

| Risk | Source | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| F1 free tier throttles a daily-use app (60 CPU-min/day, cold starts, no SSL) | Devil's advocate | H | M | Provision **B1 (~$13/mo) from day one** with Always On; never run production on F1. |
| Polling exhausts Azure SQL serverless free vCore-second grant → silent billing / auto-pause stalls | Pre-mortem | M | M | Set a budget alert; tune polling interval to the full 5s (NFR-1 ceiling); monitor vCore-seconds; consider a min-compute floor or moving to a small fixed-price DB if the grant is consistently blown. |
| No blue-green rollback on Basic (slots are Standard-only) | Unknown unknowns | M | M | Keep prior artifact retrievable; make EF migrations backward-compatible; rollback = redeploy prior build. Do not upgrade to Standard just for slots at MVP. |
| `az` CLI defaults to paid SKUs → silently-billed resource | Devil's advocate | M | H | Always pass explicit free/burstable flags (`--sku B1`, DB `--tier Burstable --sku-name Standard_B1ms` or the SQL free-offer flag); set a subscription **budget alert** as a backstop; review the resource group SKUs after any agent provisioning. |
| Coupled single artifact — bad `ng build` breaks API + UI together | Devil's advocate | M | M | Validate `dotnet publish -c Release` (which runs the Angular build) in CI before deploy; gate deploy on a successful build + smoke test. |
| Data-protection key ring not persisted → cookie auth breaks if scaled out / on restart | Unknown unknowns | L (now) / M (later) | H | Single-instance is fine for MVP; before any scale-out or if restarts log users out, persist keys to Blob/Key Vault via `PersistKeysToAzureBlobStorage` + `ProtectKeysWithAzureKeyVault`. |
| EF migrations do not auto-roll-back on revert | Research finding | M | M | Author backward-compatible migrations; keep a tested down-script; never couple a breaking schema change with an irreversible deploy. |
| Render-style trap avoided, but Azure idle cost on Basic (no scale-to-zero) | Unknown unknowns | H | L | Accept ~$13/mo as the cost of an always-warm single-household app; if cost becomes the dominant concern, swap to runner-up Railway (scale-to-usage). |

## Getting Started

Versions confirmed against the repo: **.NET 9** (`Homdutio.Api.csproj` → `net9.0`), with a Release-time MSBuild target (`BuildAngularSpa`) that runs `npm ci` + `npm run build` so the Angular SPA is bundled into `wwwroot` automatically. The whole app publishes as one artifact.

1. **Build the single artifact locally** (this triggers the Angular build):
   `dotnet publish src/Homdutio.Api/Homdutio.Api.csproj -c Release -o ./publish`
2. **Log in and create the app + plan in one shot** on Linux, .NET 9, Basic B1:
   `az login`
   `az group create --name homdutio-rg --location <region>`
   `az webapp up --name homdutio --resource-group homdutio-rg --runtime "DOTNETCORE:9.0" --sku B1 --os-type Linux`
   (Run from the project so it picks up the published output, or follow with `az webapp deploy --src-path ./publish.zip`.)
3. **Provision the co-located free database** (pick one; Azure SQL serverless free is the forever-free path):
   - Azure SQL serverless free: create via the free-offer flag, max 32 GB, 100k vCore-sec/mo. Set the connection string in App Service settings.
   - *or* Azure Database for PostgreSQL flexible server, **`--tier Burstable --sku-name Standard_B1ms` ≤ 32 GB** (free 12 months on a free account).
   `az webapp config connection-string set` (or an Application Setting) to wire it; never commit the string.
4. **Apply EF Core migrations** against the provisioned DB (backward-compatible only): `dotnet ef database update` with the production connection string supplied out-of-band.
5. **Wire GitHub Actions auto-deploy on merge to main** (matches the stack's `ci_default_flow`): use `azure/webapps-deploy@v3` with an OIDC federated credential (preferred) or a publish profile stored in GitHub Secrets. Add a **budget alert** on the subscription before the first deploy.

## Out of Scope

The following were not evaluated in this research:
- Docker image configuration (App Service runs the .NET artifact directly; no Dockerfile needed for the recommended path).
- CI/CD pipeline implementation detail (the GitHub Actions workflow is named as a step, not authored here).
- Production-scale architecture: multi-region, HA/DR, deployment slots/blue-green, scale-out + key-ring persistence (flagged in the risk register as a later concern, not designed here).
