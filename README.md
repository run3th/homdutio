# Homdutio

Współdzielona tablica kanban zadań domowych z **pętlą odpowiedzialności**: każde
działanie w gospodarstwie niesie nazwisko, znacznik czasu i potwierdzenie
administratora, dzięki czemu wkład i zaległości stają się społecznie widoczne.
Członkowie zgłaszają zadania, przejmują je („Biorę to"), oznaczają jako wykonane,
a administrator je potwierdza — zadanie zamyka się i znika z tablicy, ale trwały
ślad audytowy (kto utworzył / przejął / potwierdził, kiedy) pozostaje.

Pełny opis produktu, persony i wymagania znajdują się w
[`context/foundation/prd.md`](context/foundation/prd.md); strategia testów w
[`context/foundation/test-plan.md`](context/foundation/test-plan.md).

## Stos technologiczny

| Warstwa | Technologia |
|---|---|
| Backend API | .NET 9 — Minimal API (`src/Homdutio.Api`) |
| Dostęp do danych | EF Core + ASP.NET Core Identity, SQL Server / LocalDB (`src/Homdutio.Data`) |
| Frontend | Angular 21 SPA (`web/`) |
| Autentykacja | JWT (access + rotowany refresh token), RBAC Admin/Member |
| Powiadomienia | Web Push (VAPID) oraz e-mail przez Azure Communication Services |
| Testy | xUnit + `WebApplicationFactory` (backend), Vitest (frontend), Playwright (E2E) |
| CI/CD | GitHub Actions (`.github/workflows/deploy.yml`) → Azure App Service + Azure SQL |

## Struktura repozytorium

```
src/Homdutio.Api/     # REST API (Minimal API): auth, households, tasks, push, email
src/Homdutio.Data/    # DbContext, encje, migracje EF Core
tests/                # testy backendu (xUnit, integracyjne vs LocalDB)
web/                  # aplikacja Angular (SPA) + testy Vitest i Playwright (web/e2e)
context/foundation/   # fundament projektu 10x: PRD, roadmap, tech-stack, plan testów
context/changes/      # historia zmian (plany, research, przeglądy implementacji)
```

## Wymagania

- **.NET 9 SDK**
- **Node.js** (zgodny z Angular 21) + **npm 10**
- **SQL Server LocalDB** (`(localdb)\MSSQLLocalDB`) — dostępny z Visual Studio /
  SQL Server Express na Windows

## Konfiguracja (sekrety)

Sekrety **nie są commitowane** — dostarcz je przez user-secrets lokalnie
(lub zmienne środowiskowe w CI). API wymaga co najmniej:

```powershell
cd src/Homdutio.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\MSSQLLocalDB;Database=Homdutio;Trusted_Connection=True;MultipleActiveResultSets=true"
dotnet user-secrets set "Jwt:SigningKey" "<dowolny-długi-losowy-sekret>"
```

Niesekretna konfiguracja (issuer JWT, VAPID, rate limiting, ACS) znajduje się w
`src/Homdutio.Api/appsettings.json`. Wysyłka e-mail (ACS) i realny web-push są
opcjonalne w środowisku lokalnym — bez konfiguracji działają jako no-op.

## Uruchomienie lokalne

### 1. Baza danych (migracje stosowane ręcznie — nigdy przy starcie aplikacji)

```powershell
dotnet tool restore
dotnet ef database update --project src/Homdutio.Data --startup-project src/Homdutio.Api
```

Zasady i przepisy migracji: [`src/Homdutio.Data/MIGRATIONS.md`](src/Homdutio.Data/MIGRATIONS.md).

### 2. Backend API (`http://localhost:5252`)

```powershell
dotnet run --project src/Homdutio.Api --launch-profile http
```

Sonda zdrowia: `GET http://localhost:5252/health`.

### 3. Frontend (`http://localhost:4200`)

```powershell
cd web
npm install
npm start
```

`ng serve` proxuje `/api` → `http://localhost:5252` (patrz `web/proxy.conf.json`).

## Testy

```powershell
# Backend (xUnit, integracyjne vs LocalDB)
dotnet test

# Frontend (Vitest)
cd web && npm test

# E2E (Playwright — sam bootuje API + ng serve, migruje bazę out-of-band)
cd web && npm run e2e
```

E2E wymaga tych samych sekretów co API (`ConnectionStrings__DefaultConnection`,
`Jwt__SigningKey`); szczegóły i reguły pisania testów: [`web/e2e/CLAUDE.md`](web/e2e/CLAUDE.md).

## Budowanie

```powershell
dotnet build                 # backend
cd web && npm run build      # frontend (artefakty w web/dist/)
```
