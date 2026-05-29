# Repository Guidelines

Homdutio is a household chore-tracking web app: ASP.NET Core 9 Web API serving a same-origin Angular 21 SPA from one Azure App Service. The contracts under `context/foundation/` are the source of truth.

## Hard rules

- `context/archive/**` is immutable — refuse writes; use `/10x-new`. See `@CLAUDE.md`.
- Do not delete or edit `@NuGet.Config`. It pins `nuget.org` only; without it, `dotnet restore` inherits a 401-returning private feed from the machine's user-level NuGet config.
- `src/Homdutio.Api/wwwroot/` is `ng build` output, gitignored — never edit by hand.
- `context/foundation/*.md` is skill-owned. Re-run the owning skill (`/10x-prd`, `/10x-tech-stack-selector`, `/10x-shape`) instead of hand-editing.

## Project structure

- `src/Homdutio.Api/` — ASP.NET Core 9 Web API; csproj orchestrates the Angular build on Release.
- `web/` — Angular 21 SPA source (standalone components, no NgModule).
- `src/Homdutio.Api/wwwroot/` — Angular build output; served via `UseStaticFiles` + `MapFallbackToFile`.
- `context/foundation/` — `prd.md`, `tech-stack.md`, `shape-notes.md`.
- `context/changes/bootstrap-verification/verification.md` — bootstrap audit trail.

## Build, test, dev

- `dotnet run --project src/Homdutio.Api` — start the API in Debug.
- `cd web && npm start` — Angular dev server on `:4200` (HMR); run alongside the API.
- `dotnet build src/Homdutio.Api -c Release` — runs the `BuildAngularSpa` MSBuild target (`npm ci` if needed, then `ng build` into `wwwroot/`).
- `cd web && npm test` — Vitest (Angular 21+ default, replaces Karma).

No `.github/workflows/` yet; `tech-stack.md` records `github-actions` as the intended CI.

## Coding style

- .NET: `Nullable` and `ImplicitUsings` enabled (`@src/Homdutio.Api/Homdutio.Api.csproj`), target `net9.0`.
- Angular: standalone components, Prettier (`@web/.prettierrc`), SCSS, TypeScript 5.9. Component files colocate as `<name>.ts`/`.html`/`.scss`/`.spec.ts` — see `web/src/app/`.
- `web/angular.json` sets `outputPath: { base: "../src/Homdutio.Api/wwwroot", browser: "" }` — keep flat.

## Testing

Frontend: Vitest via `@angular/build:unit-test` (see `@web/angular.json`); tests colocate as `*.spec.ts`. No backend test project yet; if added, place it at `src/Homdutio.Api.Tests/`.

## Commit & PR

Single commit so far (`ca455de`); convention not yet established. Use descriptive imperative subjects. Remote: `https://github.com/run3th/homdutio.git`.

## Architecture

`Program.cs` middleware order: `UseHttpsRedirection` → `UseDefaultFiles` → `UseStaticFiles` → API routes → `MapFallbackToFile("index.html")`. `/api/**` reaches controllers; everything else falls through to the Angular router. Rationale: `@context/foundation/tech-stack.md`.
