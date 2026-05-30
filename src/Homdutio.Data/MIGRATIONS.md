# EF Core Migrations — Policy & Recipes

This project runs on **provisioned Basic Azure SQL** (2 GB / 5 DTU) on a single-instance B1 App
Service with **no deployment slots** and **no automatic migration on startup**. EF Core migrations
do **not** auto-roll-back. The rules below exist to keep a botched migration from taking the single
coupled artifact (API + Angular SPA) down with no safe revert.

## Rules

1. **Migrations are backward-compatible.** A migration must not break code that is already running.
   Prefer additive changes (new tables/columns, nullable or defaulted columns). Never couple a
   breaking schema change with an irreversible deploy. Split a breaking change into expand → migrate
   data → contract across separate deploys.
2. **Migrations are applied manually / out-of-band**, never on app startup. Apply before (or
   decoupled from) deploying code that depends on the new schema.
3. **No committed down-scripts.** Reversal is on-demand via the recipe below, not a per-migration
   artifact in the repo.

## Project layout

The DbContext lives in `Homdutio.Data`; the host/configuration lives in `Homdutio.Api`. Every
`dotnet ef` command therefore needs both project flags:

```
--project src/Homdutio.Data --startup-project src/Homdutio.Api
```

`Microsoft.EntityFrameworkCore.Design` is referenced by the startup project (`Homdutio.Api`); the
EF CLI is pinned as a repo-local tool in `.config/dotnet-tools.json` (run `dotnet tool restore` once).

## Recipes

Add a migration:

```
dotnet ef migrations add <Name> --project src/Homdutio.Data --startup-project src/Homdutio.Api
```

Apply pending migrations (dev — uses the user-secrets connection string):

```
dotnet ef database update --project src/Homdutio.Data --startup-project src/Homdutio.Api
```

Apply against Azure SQL (prod — connection string supplied out-of-band, never committed):

```
$env:ConnectionStrings__DefaultConnection="<azure sql string>"
dotnet ef database update --project src/Homdutio.Data --startup-project src/Homdutio.Api
```

Revert recipe (no auto-rollback): generate and review the down SQL, then apply it out-of-band:

```
dotnet ef migrations script <ToMigration> <FromMigration> --project src/Homdutio.Data --startup-project src/Homdutio.Api --output revert.sql
```

`<ToMigration>` is the current (newer) migration, `<FromMigration>` is the target (older) state —
EF emits the down operations between them. Review `revert.sql` before applying it to any database.
