# Homdutio

A shared household task kanban board built around an **accountability loop**:
every household action carries a name, a timestamp, and an admin confirmation,
so contributions and gaps become socially visible. Members add tasks, claim them
("I've got this"), mark them done, and an admin confirms them — the task closes
and leaves the board, but a durable audit trail (who created / claimed /
confirmed, and when) is preserved.

Full product description, personas, and requirements live in
[`context/foundation/prd.md`](context/foundation/prd.md); the test strategy in
[`context/foundation/test-plan.md`](context/foundation/test-plan.md).

## Tech stack

| Layer | Technology |
|---|---|
| Backend API | .NET 9 — Minimal API (`src/Homdutio.Api`) |
| Data access | EF Core + ASP.NET Core Identity, SQL Server / LocalDB (`src/Homdutio.Data`) |
| Frontend | Angular 21 SPA (`web/`) |
| Authentication | JWT (access + rotating refresh token), Admin/Member RBAC |
| Notifications | Web Push (VAPID) and email via Azure Communication Services |
| Tests | xUnit + `WebApplicationFactory` (backend), Vitest (frontend), Playwright (E2E) |
| CI/CD | GitHub Actions (`.github/workflows/deploy.yml`) → Azure App Service + Azure SQL |

## Repository layout

```
src/Homdutio.Api/     # REST API (Minimal API): auth, households, tasks, push, email
src/Homdutio.Data/    # DbContext, entities, EF Core migrations
tests/                # backend tests (xUnit, integration vs LocalDB)
web/                  # Angular SPA + Vitest and Playwright tests (web/e2e)
context/foundation/   # 10x project foundation: PRD, roadmap, tech-stack, test plan
context/changes/      # change history (plans, research, implementation reviews)
```

## Prerequisites

- **.NET 9 SDK**
- **Node.js** (compatible with Angular 21) + **npm 10**
- **SQL Server LocalDB** (`(localdb)\MSSQLLocalDB`) — available with Visual Studio /
  SQL Server Express on Windows

## Configuration (secrets)

Secrets are **not committed** — provide them via user-secrets locally
(or environment variables in CI). The API requires at minimum:

```powershell
cd src/Homdutio.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\MSSQLLocalDB;Database=Homdutio;Trusted_Connection=True;MultipleActiveResultSets=true"
dotnet user-secrets set "Jwt:SigningKey" "<any-long-random-secret>"
```

Non-secret configuration (JWT issuer, VAPID, rate limiting, ACS) lives in
`src/Homdutio.Api/appsettings.json`. Email (ACS) and real web push are optional
locally — without configuration they run as no-ops.

## Running locally

### 1. Database (migrations applied manually — never on app startup)

```powershell
dotnet tool restore
dotnet ef database update --project src/Homdutio.Data --startup-project src/Homdutio.Api
```

Migration policy and recipes: [`src/Homdutio.Data/MIGRATIONS.md`](src/Homdutio.Data/MIGRATIONS.md).

### 2. Backend API (`http://localhost:5252`)

```powershell
dotnet run --project src/Homdutio.Api --launch-profile http
```

Health probe: `GET http://localhost:5252/health`.

### 3. Frontend (`http://localhost:4200`)

```powershell
cd web
npm install
npm start
```

`ng serve` proxies `/api` → `http://localhost:5252` (see `web/proxy.conf.json`).

## Tests

```powershell
# Backend (xUnit, integration vs LocalDB)
dotnet test

# Frontend (Vitest)
cd web && npm test

# E2E (Playwright — boots the API + ng serve itself, migrates the DB out-of-band)
cd web && npm run e2e
```

E2E requires the same secrets as the API (`ConnectionStrings__DefaultConnection`,
`Jwt__SigningKey`); details and test-authoring rules: [`web/e2e/CLAUDE.md`](web/e2e/CLAUDE.md).

## Building

```powershell
dotnet build                 # backend
cd web && npm run build      # frontend (artifacts in web/dist/)
```
