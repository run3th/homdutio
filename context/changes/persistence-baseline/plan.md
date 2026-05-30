# Persistence Baseline — EF Core + Provisioned Azure SQL Implementation Plan

## Overview

Wire EF Core 9 and an `ApplicationDbContext` (housed in a new `Homdutio.Data` class library) to
a **provisioned Azure SQL Basic** database, with a manual migration workflow, a `/health`
DB-connectivity check, and an xUnit smoke test. A single **throwaway probe entity** proves an
end-to-end write/read round-trip against the real provider. This is the bounded foundation
enabler **F-01**: it satisfies NFR-3's durability precondition and unlocks F-02 (auth), S-01, and
every data-bearing slice — **without** introducing any real domain schema (Households, Tasks,
Identity arrive with their own slices).

## Current State Analysis

The data layer is genuinely absent and was deliberately deferred at first deploy:

- `src/Homdutio.Api/Homdutio.Api.csproj:11` references only `Microsoft.AspNetCore.OpenApi 9.0.9` — **no EF Core packages**.
- `src/Homdutio.Api/Program.cs` is the stock minimal-API template: `AddOpenApi()`, the `/weatherforecast` demo endpoint, `UseDefaultFiles()`/`UseStaticFiles()`, and `MapFallbackToFile("index.html")` (`Program.cs:39`). **No DbContext registration.**
- Neither `appsettings.json` nor `appsettings.Development.json` contains a `ConnectionStrings` section. `appsettings.Development.json` **is committed** to the repo (so it must not hold a real secret).
- No DbContext, no migrations, **no `.sln`** (single project under `src/`), **no test project** (`tests/` does not exist).
- `deploy-plan.md` shipped scaffold-only "to avoid paying for an idle DB" and hands the data layer to this step. The App Service (`homdutio`, B1, Poland Central, HTTPS-only) is **live**, but **no SQL server/database is provisioned and no connection string is wired**.
- No `context/foundation/lessons.md` and no `docs/reference/contract-surfaces.md` exist yet.

### Key Discoveries:

- **Stock template, clean slate** — `Program.cs:1-41` has no DI beyond OpenAPI; adding the DbContext registration is additive, no existing pattern to conform to.
- **`appsettings.Development.json` is tracked by git** — the local connection string therefore goes in **.NET user-secrets**, never that file (decision: Secrets).
- **Single coupled artifact, no deploy slots on B1** — a bad migration on startup would take API + UI down with no auto-rollback (`infrastructure.md` risk register). This drives the manual, out-of-band migration strategy and the backward-compatible-migration discipline.
- **Provisioned Basic SQL only** — `az sql db create` must pass `--service-objective Basic` and must **not** pass `--compute-model Serverless`; `az` defaults to pricier SKUs (`infrastructure.md` Getting Started + risk register).
- **Release build runs the Angular SPA build** via the `BuildAngularSpa` MSBuild target (`Homdutio.Api.csproj:14-18`); adding a class library + test project must not disturb that target or the single-artifact publish.

## Desired End State

EF Core 9 is wired through a `Homdutio.Data` class library; a `dotnet ef migrations add` /
`dotnet ef database update` workflow runs cleanly against both LocalDB (dev) and the provisioned
Azure SQL Basic database (prod); the deployed app's `GET /health` reports `Healthy` after opening
a real connection to Azure SQL; an xUnit smoke test proves a `SchemaProbe` write/read round-trip
against LocalDB; and the connection string lives only in user-secrets (local) and App Service
connection-strings (Azure), never the repo.

**Verification:** `dotnet build` and `dotnet test` are green; `dotnet ef database update` applies
the `InitialCreate` migration to both LocalDB and Azure SQL; `GET /health` returns `Healthy`
locally and on `https://homdutio.azurewebsites.net`; no connection string appears in any tracked file.

## What We're NOT Doing

- **No real domain entities** — no Households, Tasks, board, or audit-record schema. Those belong to S-02/S-03 and their own plans. The only table beyond `__EFMigrationsHistory` is the throwaway `SchemaProbe`.
- **No ASP.NET Identity** — that is F-02 (`auth-identity-plumbing`). This slice only provides the EF store F-02 will later mount onto.
- **No automatic migration on startup** — explicitly rejected (no auto-rollback on B1).
- **No CI/CD migration step** — F-04 (`ci-auto-deploy`) is not built yet; migrations are applied out-of-band by hand this round.
- **No Azure Key Vault** — connection string lives in App Service connection-strings; Key Vault is a documented later option.
- **No deployment slots / Standard-tier upgrade / budget-alert automation** — out of scope (budget alert is noted as a manual follow-up, consistent with `deploy-plan.md`).
- **No down-script-per-migration tooling** — the policy is backward-compatible discipline + a documented revert recipe, not committed down-scripts.

## Implementation Approach

Build inside-out and local-first: scaffold the solution + data library and get it compiling
(Phase 1); wire DI, config, the health check, and the first migration against LocalDB (Phase 2);
lock in an automated regression net with an xUnit smoke test (Phase 3); then — and only then —
provision the paid Azure SQL resource, wire its secret, and apply the migration to prod (Phase 4).
Phases 1–3 cost nothing and are fully verifiable on the dev machine; Phase 4 isolates all
cost-incurring and human-gated cloud actions into the last step.

## Critical Implementation Details

- **`dotnet ef` with a split project** — the DbContext lives in `Homdutio.Data` but the host/config lives in `Homdutio.Api`. Every EF command needs both: `--project src/Homdutio.Data --startup-project src/Homdutio.Api`. `Microsoft.EntityFrameworkCore.Design` must be referenced by the **startup project** (`Homdutio.Api`) for the tooling to resolve.
- **Health endpoint vs SPA fallback ordering** — `MapHealthChecks("/health")` must be registered as a routed endpoint so `MapFallbackToFile("index.html")` (`Program.cs:39`, last-resort) does not serve the SPA shell for `/health`. `/health` is not a physical file, so `UseStaticFiles` won't intercept it; mapping the health route before the fallback keeps it returning the health payload.
- **Retry execution strategy constraint** — enabling `EnableRetryOnFailure()` activates a retrying `IExecutionStrategy`, which **disallows user-initiated transactions that span the retry boundary**. Later slices that need explicit multi-statement transactions must wrap them via `context.Database.CreateExecutionStrategy().Execute(...)`. Note this for downstream planners.

## Phase 1: Solution + Data Project Skeleton

### Overview

Create a solution, add a `Homdutio.Data` class library, reference it from the API, add EF Core 9
packages, and define the (empty) `ApplicationDbContext` plus the throwaway `SchemaProbe` entity.
No database is touched; the goal is a compiling solution.

### Changes Required:

#### 1. Solution file

**File**: `Homdutio.sln` (new, repo root)

**Intent**: Introduce a solution so the API, the new data library, and the later test project are built/restored together and `dotnet ef` startup/project flags resolve predictably.

**Contract**: `dotnet new sln`; add `src/Homdutio.Api/Homdutio.Api.csproj`. (Data + test projects are added in their own steps/phases.)

#### 2. Data class library

**File**: `src/Homdutio.Data/Homdutio.Data.csproj` (new)

**Intent**: House the `ApplicationDbContext`, entities, and migrations separately from web concerns (decision: split data library).

**Contract**: `net9.0` class library, `Nullable` + `ImplicitUsings` enabled (match the API csproj). PackageReference: `Microsoft.EntityFrameworkCore.SqlServer` (EF Core 9.0.x). Added to the solution.

#### 3. API → Data project reference + EF design tooling

**File**: `src/Homdutio.Api/Homdutio.Api.csproj`

**Intent**: Let the API consume the DbContext and let `dotnet ef` run with the API as startup project.

**Contract**: Add a `ProjectReference` to `Homdutio.Data`; add PackageReference `Microsoft.EntityFrameworkCore.Design` (EF Core 9.0.x, the design-time package the EF CLI needs on the startup project). Do not disturb the existing `BuildAngularSpa` target.

#### 4. ApplicationDbContext + probe entity

**File**: `src/Homdutio.Data/ApplicationDbContext.cs`, `src/Homdutio.Data/Entities/SchemaProbe.cs` (new)

**Intent**: Define the canonical DbContext the whole app will share, and a single throwaway entity so the first migration is non-empty and a real round-trip is provable. The probe is explicitly removable once real entities land.

**Contract**: `ApplicationDbContext : DbContext` with a public `DbContext(DbContextOptions<ApplicationDbContext>)` constructor and `DbSet<SchemaProbe> SchemaProbes`. `SchemaProbe` = identity PK (`int Id`), `DateTime CreatedAtUtc`, `string Note`. Mark `SchemaProbe` in a code comment as a throwaway to be removed when real domain entities arrive.

### Success Criteria:

#### Automated Verification:

- Solution restores: `dotnet restore Homdutio.sln`
- Solution builds clean (Debug): `dotnet build Homdutio.sln`
- `dotnet ef` resolves the context: `dotnet ef dbcontext info --project src/Homdutio.Data --startup-project src/Homdutio.Api`

#### Manual Verification:

- `Homdutio.Data` and the API both appear in the solution; the API references the data library.
- No change to the Angular `BuildAngularSpa` behavior (Release publish still triggers the SPA build).

---

## Phase 2: DI Wiring, Config, Health Check & First Migration (LocalDB)

### Overview

Register the DbContext in the API with the SqlServer provider + transient-fault retry, source the
connection string from configuration, store the local string in user-secrets pointing at LocalDB,
add a `/health` DB check, generate the `InitialCreate` migration, apply it to LocalDB, and document
the migration-safety policy. End state: a working local persistence round-trip.

### Changes Required:

#### 1. DbContext DI registration

**File**: `src/Homdutio.Api/Program.cs`

**Intent**: Register `ApplicationDbContext` with the SqlServer provider, reading the connection string from configuration, and enable Azure SQL transient-fault resiliency at wiring time (decision: EnableRetryOnFailure now).

**Contract**: `builder.Services.AddDbContext<ApplicationDbContext>(...)` using `UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())`; connection string read from `ConnectionStrings:DefaultConnection`. Registration added in the services section (before `builder.Build()`).

#### 2. Local connection string via user-secrets

**File**: API project user-secrets store (not a tracked file); `src/Homdutio.Api/Homdutio.Api.csproj` (add `UserSecretsId`)

**Intent**: Supply the LocalDB connection string for development without committing a secret (decision: user-secrets locally; `appsettings.Development.json` is git-tracked so must stay clean).

**Contract**: `dotnet user-secrets init` (adds `UserSecretsId` to the csproj) then `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<LocalDB string>"`. LocalDB target: `Server=(localdb)\MSSQLLocalDB;Database=Homdutio;Trusted_Connection=True;MultipleActiveResultSets=true`. Confirm no `ConnectionStrings` is added to either committed `appsettings*.json`.

#### 3. Health check endpoint

**File**: `src/Homdutio.Api/Program.cs`

**Intent**: Expose `GET /health` that verifies the DbContext can reach the database, giving F-04's future smoke gate and post-deploy checks a single connectivity signal.

**Contract**: `builder.Services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>();` and `app.MapHealthChecks("/health");`. **Must** be mapped before `app.MapFallbackToFile("index.html")` so the SPA fallback does not capture `/health` (see Critical Implementation Details).

#### 4. Initial migration + apply to LocalDB

**File**: `src/Homdutio.Data/Migrations/*` (generated)

**Intent**: Produce the first (non-empty) migration creating `__EFMigrationsHistory` + the `SchemaProbe` table, and apply it locally to prove the workflow.

**Contract**: `dotnet ef migrations add InitialCreate --project src/Homdutio.Data --startup-project src/Homdutio.Api`, then `dotnet ef database update --project src/Homdutio.Data --startup-project src/Homdutio.Api`. Migration files committed.

#### 5. Migration-safety policy doc

**File**: `src/Homdutio.Data/MIGRATIONS.md` (new)

**Intent**: Record the backward-compatible-migration rule and the revert recipe demanded by the infra risk register (no auto-rollback, no slots on B1) — decision: discipline + documented recipe, not committed down-scripts.

**Contract**: Short doc stating (a) every migration must be backward-compatible (additive; no breaking change coupled with an irreversible deploy), (b) the EF command flags for this split-project layout, and (c) the revert recipe: `dotnet ef migrations script <from> <to> --project src/Homdutio.Data --startup-project src/Homdutio.Api` reviewed and applied out-of-band.

### Success Criteria:

#### Automated Verification:

- Build still clean: `dotnet build Homdutio.sln`
- Migration applies to LocalDB: `dotnet ef database update --project src/Homdutio.Data --startup-project src/Homdutio.Api`
- No connection string in tracked files: `git grep -i "Server=" -- '*.json'` returns nothing
- App starts and `GET /health` returns `Healthy` (200) against LocalDB

#### Manual Verification:

- A `SchemaProbe` row can be written and read back (e.g. via a scratch insert + `/health` or a quick query), proving a real round-trip.
- `MIGRATIONS.md` accurately describes the revert recipe for this split-project layout.
- `/health` is reachable and is not shadowed by the Angular SPA fallback.

**Implementation Note**: After automated verification passes, pause for manual confirmation that the local round-trip and `/health` behave as expected before proceeding.

---

## Phase 3: xUnit Smoke Test

### Overview

Stand up the project's first automated test project and add a DbContext smoke test that applies
migrations against a throwaway LocalDB database and asserts a `SchemaProbe` write/read round-trip —
giving the persistence foundation a real automated regression net and a harness later slices reuse.

### Changes Required:

#### 1. Test project

**File**: `tests/Homdutio.Data.Tests/Homdutio.Data.Tests.csproj` (new)

**Intent**: Establish the xUnit test harness (decision: add tests now; test DB = LocalDB for provider parity).

**Contract**: `net9.0` xUnit test project referencing `Homdutio.Data`; added to `Homdutio.sln`. Uses the same `Microsoft.EntityFrameworkCore.SqlServer` provider so the test exercises the real engine family, not a divergent provider.

#### 2. DbContext migration + round-trip smoke test

**File**: `tests/Homdutio.Data.Tests/PersistenceSmokeTests.cs` (new)

**Intent**: Prove that migrations apply and a `SchemaProbe` survives a write then read, against LocalDB.

**Contract**: Test builds `DbContextOptions<ApplicationDbContext>` pointed at a uniquely-named throwaway LocalDB database, calls `Database.Migrate()`, inserts a `SchemaProbe`, reads it back on a fresh context instance, asserts equality, and tears the database down (`Database.EnsureDeleted()`) in disposal so runs are isolated and repeatable.

### Success Criteria:

#### Automated Verification:

- Tests pass: `dotnet test Homdutio.sln`
- Test run leaves no residual database (teardown verified by a clean re-run)

#### Manual Verification:

- The test reads as a clear template for how later slices test against the real provider.

**Implementation Note**: After `dotnet test` is green, pause for manual confirmation before proceeding to the cost-incurring Azure phase.

---

## Phase 4: Provision Azure SQL + Wire + Apply (Prod)

### Overview

Provision the **provisioned Basic** Azure SQL server/database, wire the connection string into App
Service connection-strings (out-of-band, never the repo), apply the `InitialCreate` migration to
Azure SQL, and verify the deployed app's `/health` is `Healthy` against real Azure SQL. All actions
here incur cost and/or touch prod, so they are human-gated.

### Changes Required:

#### 1. Provision Azure SQL (Basic, non-serverless)

**File**: (Azure resources — no repo file; commands recorded in `deploy-plan.md` follow-up)

**Intent**: Create the co-located managed database F-01 requires, at the deliberate Basic tier (flat ~$5/mo, always-on), avoiding the pricier/serverless defaults.

**Contract**: `az sql server create --name homdutio-sql --resource-group homdutio-rg --location polandcentral --admin-user <admin> --admin-password <pwd>`; `az sql db create --name homdutio-db --server homdutio-sql --resource-group homdutio-rg --service-objective Basic` (**explicit `Basic`; do NOT pass `--compute-model Serverless`**). Add firewall rules: allow Azure services, and a temporary local-IP rule for the out-of-band migration apply.

#### 2. Wire connection string into App Service

**File**: (App Service configuration — no repo file)

**Intent**: Supply the production connection string as an App Service connection-string env var (decision: App Service connection-strings, not the repo, not Key Vault).

**Contract**: `az webapp config connection-string set --name homdutio --resource-group homdutio-rg --connection-string-type SQLAzure --settings DefaultConnection="<azure sql string>"`. The string is supplied out-of-band and never committed.

#### 3. Apply migration to Azure SQL

**File**: (migration already in repo from Phase 2)

**Intent**: Bring the provisioned DB to the `InitialCreate` schema using the same manual workflow.

**Contract**: `dotnet ef database update --project src/Homdutio.Data --startup-project src/Homdutio.Api` with the Azure SQL connection string supplied out-of-band (env var / `--connection`). Backward-compatible per `MIGRATIONS.md`.

#### 4. Record deployment + follow-ups

**File**: `context/deployment/deploy-plan.md`

**Intent**: Update the audit trail to reflect that the DB is now provisioned and wired, and note the open follow-ups (budget alert; Standard S0 step before the 2 GB / 5 DTU cap bites).

**Contract**: Append a dated entry under the deploy record: SQL server/db names, tier, connection-string wiring location, and the budget-alert / tier-watch follow-ups. Do not write secret values.

### Success Criteria:

#### Automated Verification:

- Migration applies cleanly to Azure SQL: `dotnet ef database update ...` (against the prod connection) exits 0
- Deployed health check passes: `GET https://homdutio.azurewebsites.net/health` returns `Healthy` (200)

#### Manual Verification:

- Azure portal shows `homdutio-db` at **Basic** service objective (not Serverless, not a larger SKU).
- No connection string or admin password appears in any tracked repo file.
- A budget alert follow-up is recorded (consistent with `infrastructure.md` risk register).
- Deployed `/health` reflects real Azure SQL connectivity (verified after a deploy that carries the wiring).

**Implementation Note**: This phase performs cost-incurring and prod-affecting actions (resource creation, SKU selection, prod credential, first prod write) — all human-gated per `infrastructure.md` approval policy. Confirm with the human before running provisioning and before applying the migration to prod.

---

## Testing Strategy

### Unit Tests:

- DbContext can be constructed from `DbContextOptions` (validates DI shape).
- Migrations apply against a throwaway LocalDB database (validates the SqlServer-provider migration, not a divergent provider).
- `SchemaProbe` write → read round-trip returns the persisted values (validates real CRUD through the provider).

### Integration Tests:

- `GET /health` returns `Healthy` when the DB is reachable (local LocalDB and deployed Azure SQL).
- (Deferred) Full request-path integration tests arrive with the first behavior-bearing slice (S-01).

### Manual Testing Steps:

1. Run the API locally; hit `GET /health` → expect `Healthy`.
2. Insert a `SchemaProbe` row (scratch) and confirm it reads back across a fresh context.
3. After Phase 4 deploy, hit `https://homdutio.azurewebsites.net/health` → expect `Healthy`.
4. Confirm `az sql db show` reports the Basic service objective.
5. `git grep -i "Server="` over tracked files returns nothing.

## Performance Considerations

Basic Azure SQL caps at **2 GB / 5 DTU** with no scale-to-zero. This slice adds essentially no
load (one tiny table), but the `EnableRetryOnFailure` execution strategy is enabled now because
transient throttling under the 5-DTU cap is expected as real slices add traffic. Plan the Standard
S0 (~$15/mo) step before the 2 GB / 5 DTU ceiling bites — NFR-3 means audit data only grows.

## Migration Notes

No existing data to migrate (greenfield DB). The forward migration is `InitialCreate`. Reverting is
manual: generate and review a down-script (`dotnet ef migrations script <from> <to> ...`) and apply
it out-of-band — there is no auto-rollback and no deploy slots on B1. Every future migration must be
backward-compatible; never couple a breaking schema change with an irreversible deploy. See
`src/Homdutio.Data/MIGRATIONS.md`.

## References

- Roadmap item: `context/foundation/roadmap.md` → F-01 (`persistence-baseline`)
- PRD durability requirement: `context/foundation/prd.md` → NFR-3
- Infra constraints (Basic SQL, no slots, no auto-rollback, `az` SKU defaults, secrets): `context/foundation/infrastructure.md`
- Deploy baseline + DB-deferral history: `context/deployment/deploy-plan.md`
- Current host pipeline: `src/Homdutio.Api/Program.cs`, `src/Homdutio.Api/Homdutio.Api.csproj`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Solution + Data Project Skeleton

#### Automated

- [x] 1.1 Solution restores: `dotnet restore Homdutio.sln` — 73970be
- [x] 1.2 Solution builds clean (Debug): `dotnet build Homdutio.sln` — 73970be
- [x] 1.3 `dotnet ef dbcontext info` resolves the context — deferred to Phase 2 (DI-dependent; subsumed by 2.2 migration apply) — 73970be

#### Manual

- [x] 1.4 Both projects in the solution; API references the data library — 73970be
- [x] 1.5 No change to `BuildAngularSpa` Release behavior — 73970be

### Phase 2: DI Wiring, Config, Health Check & First Migration (LocalDB)

#### Automated

- [x] 2.1 Build still clean: `dotnet build Homdutio.sln`
- [x] 2.2 Migration applies to LocalDB: `dotnet ef database update ...`
- [x] 2.3 No connection string in tracked files: `git grep -i "Server=" -- '*.json'` empty
- [x] 2.4 `GET /health` returns `Healthy` against LocalDB

#### Manual

- [x] 2.5 `SchemaProbe` write/read round-trip verified
- [x] 2.6 `MIGRATIONS.md` revert recipe accurate for the split-project layout
- [x] 2.7 `/health` not shadowed by the SPA fallback

### Phase 3: xUnit Smoke Test

#### Automated

- [ ] 3.1 Tests pass: `dotnet test Homdutio.sln`
- [ ] 3.2 Test leaves no residual database (clean re-run)

#### Manual

- [ ] 3.3 Test reads as a clear template for later provider-real tests

### Phase 4: Provision Azure SQL + Wire + Apply (Prod)

#### Automated

- [ ] 4.1 Migration applies cleanly to Azure SQL (prod connection, exit 0)
- [ ] 4.2 `GET https://homdutio.azurewebsites.net/health` returns `Healthy`

#### Manual

- [ ] 4.3 Azure portal shows `homdutio-db` at Basic service objective
- [ ] 4.4 No connection string / admin password in any tracked file
- [ ] 4.5 Budget-alert follow-up recorded
- [ ] 4.6 Deployed `/health` reflects real Azure SQL connectivity
