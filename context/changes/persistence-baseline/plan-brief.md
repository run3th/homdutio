# Persistence Baseline (F-01) — Plan Brief

> Full plan: `context/changes/persistence-baseline/plan.md`

## What & Why

Wire EF Core 9 + an `ApplicationDbContext` to a provisioned Azure SQL Basic database with a
runnable migration workflow, so **data persists** — the NFR-3 durability precondition. This is a
bounded foundation enabler (roadmap **F-01**) that unlocks F-02 (auth), S-01, and every
data-bearing slice. It deliberately adds **no real domain schema**.

## Starting Point

The data layer is absent: the API (`src/Homdutio.Api`) is the stock minimal-API template (OpenAPI +
`/weatherforecast` + Angular SPA fallback), with no EF packages, no DbContext, no connection string,
no `.sln`, and no test project. The Azure App Service (B1, Poland Central) is live but scaffold-only —
the SQL database was deliberately deferred at first deploy to avoid idle cost.

## Desired End State

EF Core runs through a new `Homdutio.Data` class library; `dotnet ef database update` applies an
`InitialCreate` migration to both LocalDB (dev) and provisioned Azure SQL (prod); the deployed
`GET /health` reports `Healthy` against real Azure SQL; an xUnit smoke test proves a `SchemaProbe`
write/read round-trip; and the connection string lives only in user-secrets and App Service config —
never the repo.

## Key Decisions Made

| Decision                  | Choice                                             | Why (1 sentence)                                                                 | Source |
| ------------------------- | -------------------------------------------------- | -------------------------------------------------------------------------------- | ------ |
| Local dev DB              | LocalDB / SQL Server Express                       | T-SQL parity with Azure SQL, zero containers, native on the solo Windows builder. | Plan   |
| Azure provisioning timing | Wire now, provision Azure SQL at end of this change | F-01 is genuinely "done" — verified end-to-end against real Azure SQL.            | Plan   |
| Migration application     | Manual / out-of-band `dotnet ef database update`   | No auto-rollback and no deploy slots on B1 — a startup migration could take the app down. | Plan   |
| First migration content   | Tiny throwaway `SchemaProbe` entity                | Non-empty migration proves a real write/read round-trip; removable later.        | Plan   |
| Verification surface      | `/health` with `AddDbContextCheck`                 | One endpoint proves DB connectivity locally and in Azure for the F-04 smoke gate. | Plan   |
| Tests                     | xUnit project + DbContext smoke test (LocalDB)     | Establishes the reusable harness and a real automated success criterion.         | Plan   |
| Secret storage            | user-secrets (local) + App Service connection-string (Azure) | Matches infra "never the repo"; `appsettings.Development.json` is git-tracked.   | Plan   |
| Project structure         | Split a `Homdutio.Data` class library              | Clean separation of data from web concerns; migrations isolated.                 | Plan   |
| Migration safety          | Backward-compatible discipline + documented revert recipe | Mitigates the infra register's top footgun cheaply, before real schema exists.   | Plan   |
| Connection resiliency     | Enable `EnableRetryOnFailure` now                  | Azure SQL transient faults under the 5-DTU cap are expected; best set at wiring time. | Plan   |
| Test DB provider          | LocalDB (real SqlServer provider)                  | True parity catches SqlServer-specific migration issues a divergent provider hides. | Plan   |

## Scope

**In scope:** solution + `Homdutio.Data` library, EF Core 9 SqlServer wiring, DI registration with
retry, `/health` DB check, `InitialCreate` migration (+ `SchemaProbe`), migration-safety doc, xUnit
smoke test, Azure SQL Basic provisioning + connection-string wiring + prod migration apply.

**Out of scope:** real domain entities (Households/Tasks/audit record), ASP.NET Identity (F-02),
auto-migrate-on-startup, CI/CD migration step (F-04), Key Vault, deployment slots / Standard tier,
committed down-scripts.

## Architecture / Approach

`Homdutio.Api` (host, config, `/health`, SPA) → references → `Homdutio.Data` (`ApplicationDbContext`,
`SchemaProbe`, migrations). EF CLI runs with `--project src/Homdutio.Data --startup-project
src/Homdutio.Api`; `Microsoft.EntityFrameworkCore.Design` sits on the API. Connection string flows
from `ConnectionStrings:DefaultConnection` (user-secrets locally, App Service connection-strings in
Azure). Build inside-out and local-first; all cost/prod actions isolated in the final phase.

## Phases at a Glance

| Phase                                | What it delivers                                              | Key risk                                                     |
| ------------------------------------ | ------------------------------------------------------------ | ------------------------------------------------------------ |
| 1. Solution + Data skeleton          | Compiling `.sln` + `Homdutio.Data` lib + DbContext/probe      | Disturbing the `BuildAngularSpa` target / single-artifact publish |
| 2. DI, config, `/health`, migration  | Local round-trip + `Healthy` health check + `InitialCreate`   | Health route shadowed by SPA fallback; secret leaking into tracked config |
| 3. xUnit smoke test                  | Automated migration + round-trip regression net               | Test-DB isolation/teardown leaving residue                   |
| 4. Provision Azure SQL + wire + apply | Provisioned Basic DB, wired secret, prod migration, `Healthy` | `az` defaulting to a pricier/serverless SKU; first prod migration with no rollback |

**Prerequisites:** Azure CLI logged into the `<user>` subscription; LocalDB available on the dev
machine; .NET 9 SDK + `dotnet-ef` tool. No upstream change blocks this (F-01 has no prerequisites).
**Estimated effort:** ~2–3 after-hours sessions across 4 phases; Phase 4 gated on human approval for cost.

## Open Risks & Assumptions

- **Cost clock starts now** — provisioning Azure SQL Basic begins the flat ~$5/mo charge this change (accepted to make F-01 truly "done"); a budget alert is a recorded follow-up, not built here.
- **No auto-rollback / no slots on B1** — relies on backward-compatible-migration discipline; a non-compliant future migration could still cause a hard prod incident.
- **`EnableRetryOnFailure` constraint** — later slices needing explicit multi-statement transactions must use `CreateExecutionStrategy().Execute(...)`.
- **2 GB / 5 DTU ceiling** — fine now; plan the Standard S0 step before audit data growth bites.

## Success Criteria (Summary)

- `dotnet build` + `dotnet test` green; `InitialCreate` applies cleanly to both LocalDB and Azure SQL.
- `GET /health` returns `Healthy` locally and at `https://homdutio.azurewebsites.net/health` against real Azure SQL.
- No connection string or credential in any tracked repo file; `homdutio-db` provisioned at Basic tier.
