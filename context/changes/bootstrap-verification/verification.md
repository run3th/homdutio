---
bootstrapped_at: 2026-05-27T19:24:19Z
starter_id: dotnet
starter_name: ".NET (ASP.NET Core webapi)"
project_name: homdutio
language_family: dotnet
package_manager: dotnet
cwd_strategy: subdir-then-move (registry default; overridden mid-run — see Scaffold log)
bootstrapper_confidence: verified
phase_3_status: ok
audit_command: "dotnet list package --vulnerable"
---

# Bootstrap verification log

## Hand-off

Verbatim from `context/foundation/tech-stack.md`:

```yaml
starter_id: dotnet
package_manager: dotnet
project_name: homdutio
hints:
  language_family: dotnet
  team_size: solo
  deployment_target: azure-app-service
  ci_provider: github-actions
  ci_default_flow: auto-deploy-on-merge
  bootstrapper_confidence: verified
  path_taken: standard
  quality_override: false
  self_check_answers: null
  has_auth: true
  has_payments: false
  has_realtime: false
  has_ai: false
  has_background_jobs: false
```

### Why this stack (from hand-off)

A solo builder shipping a household-chore-tracking MVP in 3 weeks of after-hours work needs a strongly-typed, well-documented starter for a small two-account web app with auth. ASP.NET Core (the `dotnet` starter, webapi template) clears all four agent-friendly gates (typed by language; convention-based via the template; popular in training data; mature first-party documentation) and the card's bootstrapper confidence is verified. Auth is in scope (FR-001/002/006/020 cover registration, login, invite-link join, password reset) and can be implemented on top of ASP.NET Core Identity or a similar built-in primitive without external services. Deployment lands on Azure App Service — the .NET card's first default — which is the cheapest path to a first deploy for ASP.NET Core; single-region single-instance is acceptable for v1 per the no-multi-region non-goal. CI runs on GitHub Actions with auto-deploy on merge to main.

## Pre-scaffold verification

| Signal       | Value     | Severity | Notes                                                                                |
| ------------ | --------- | -------- | ------------------------------------------------------------------------------------ |
| npm package  | not run   | n/a      | `language_family: dotnet` — cmd_template does not invoke an npm create-* CLI         |
| GitHub repo  | not run   | n/a      | card `docs_url` is `learn.microsoft.com/aspnet/core`, not a GitHub repository        |

No staleness signals applicable for .NET starters under the v1 pre-scaffold check. Proceeded without staleness data.

## Scaffold log

**Registry-prescribed strategy**: `subdir-then-move` (default; `dotnet` not listed in `bootstrapper-config.yaml` `starters:`).

**First attempt (literal registry substitution)**:

- **Resolved invocation**: `dotnet new webapi -n .bootstrap-scaffold --no-restore`
- **Exit code**: 0
- **Outcome**: succeeded mechanically but produced unusable artifacts. `dotnet new` accepted the name `.bootstrap-scaffold` and sanitized only the `<RootNamespace>` (to `_bootstrap_scaffold`); the project file landed as `.bootstrap-scaffold.csproj`, so the resulting assembly name would be `.bootstrap-scaffold` — not a valid C# identifier. Files generated in `.bootstrap-scaffold/`:
  - `.bootstrap-scaffold.csproj` (broken name)
  - `.bootstrap-scaffold.http`
  - `Program.cs` (uses top-level statements; no namespace declaration — unaffected)
  - `appsettings.json`, `appsettings.Development.json` (config files; unaffected)
  - `Properties/` (launch profile; unaffected)

**Registry bug identified**: the `dotnet` card's `cmd_template` uses `{name}` as the .NET project/assembly identifier (which must be a valid C# identifier), but the v1 substitution rule for `subdir-then-move` forces `{name}` to the literal string `.bootstrap-scaffold`. The two contracts are incompatible. The same class of bug applies to the Django registry entry and (by inspection) the Spring entry.

**Recovery — user-approved override**:

- Deleted the first-attempt temp directory: `rm -rf .bootstrap-scaffold`.
- Re-ran with a corrected invocation that uses the project_name from the hand-off and forces output into cwd: `dotnet new webapi -n Homdutio -o . --no-restore`.
- **Exit code**: 0
- **Files written by CLI into cwd**: `Homdutio.csproj`, `Homdutio.http`, `Program.cs`, `appsettings.json`, `appsettings.Development.json`, `Properties/launchSettings.json`.
- **Pre-existing files preserved (no conflicts surfaced)**: `CLAUDE.md`, `idea-notes.md`, `skills-lock.json`, `.claude/`, `.agents/`, `context/`.
- **Effective strategy**: native-cwd (the override scaffolds directly into the current directory rather than into a temp subdir).

**.bootstrap-scaffold cleanup**: temp directory deleted before the corrected scaffold ran.

**.gitignore handling**: not applicable — `dotnet new webapi` does not emit a `.gitignore`, and cwd has none. The user may want to add one (`dotnet new gitignore` is a standard template).

### Post-run restructuring (user-requested)

After the first successful scaffold landed flat at the repository root, the user asked for a structured layout. With nothing yet committed, the following was applied:

- Deleted flat-root scaffold files: `Homdutio.csproj`, `Homdutio.http`, `Program.cs`, `Properties/`, `appsettings.json`, `appsettings.Development.json`, `bin/`, `obj/`.
- Preserved: `.gitignore`, `NuGet.Config`, `context/`, `CLAUDE.md`, `idea-notes.md`, `.claude/`, `.agents/`, `skills-lock.json`, `.git/`.
- Re-scaffolded under a structured layout: `dotnet new webapi -n Homdutio.Api -o src/Homdutio.Api --no-restore` (assembly name `Homdutio.Api`).
- Created `web/.gitkeep` as a placeholder for a future frontend (Razor Pages, Blazor, or a separate SPA).
- Verified: `dotnet build src/Homdutio.Api` — 0 warnings, 0 errors. The root `NuGet.Config` is picked up by the nested project as expected.
- Verified: nested `src/Homdutio.Api/bin` and `src/Homdutio.Api/obj` are matched by the .NET `.gitignore` patterns `[Bb]in/` and `[Oo]bj/`.
- Re-ran the vulnerability audit against the new project path: `dotnet list src/Homdutio.Api package --vulnerable` — 0 vulnerable packages.

**Final tree** (excluding `.git/`, `.claude/`, `.agents/`, `bin/`, `obj/`):

```
.gitignore
CLAUDE.md
NuGet.Config
context/
  archive/
  changes/
    bootstrap-verification/verification.md
  foundation/
    prd.md
    shape-notes.md
    tech-stack.md
idea-notes.md
skills-lock.json
src/
  Homdutio.Api/
    Homdutio.Api.csproj
    Homdutio.Api.http
    Program.cs
    Properties/launchSettings.json
    appsettings.json
    appsettings.Development.json
web/
  .gitkeep
```

## Post-scaffold audit

**Tool**: `dotnet list package --vulnerable`
**Status**: succeeded (after a NuGet feed isolation fix — see below)
**Summary**: 0 CRITICAL, 0 HIGH, 0 MODERATE, 0 LOW
**Direct vs transitive**: not distinguished by this tool (`dotnet list package --vulnerable` reports flat).
**Output**: "The given project `Homdutio` has no vulnerable packages given the current sources."

### NuGet feed isolation (applied during this run)

The first audit attempt failed because the user-level NuGet config at `C:\Users\<user>\AppData\Roaming\NuGet\NuGet.Config` included a private Azure Artifacts feed that returned HTTP 401 (Unauthorized) for the active credential context. `dotnet restore` would not complete and the audit had no resolved graph to query.

Resolution (user-approved): wrote a project-local `NuGet.Config` at the repository root that clears parent sources and enables only `nuget.org`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

The user-level config is unchanged; other projects on this machine continue to see the private feed normally. With the project-local file in place, `dotnet restore` succeeded in 723 ms, `dotnet build` succeeded (0 warnings, 0 errors), and the vulnerability audit ran cleanly.

### Build verification

Also ran `dotnet build` end-to-end as a sanity check:

```
Homdutio -> C:\Projects\Homdutio\bin\Debug\net9.0\Homdutio.dll
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

This added `bin/` and `obj/` directories to the repository root — they should land in `.gitignore` before the first commit.

## Hints recorded but not acted on

| Hint                       | Value                              |
| -------------------------- | ---------------------------------- |
| bootstrapper_confidence    | verified                           |
| quality_override           | false                              |
| path_taken                 | standard                           |
| self_check_answers         | null                               |
| team_size                  | solo                               |
| deployment_target          | azure-app-service                  |
| ci_provider                | github-actions                     |
| ci_default_flow            | auto-deploy-on-merge               |
| has_auth                   | true                               |
| has_payments               | false                              |
| has_realtime               | false                              |
| has_ai                     | false                              |
| has_background_jobs        | false                              |

v1 surfaces these but does not compensate. The deployment target, CI provider, CI flow, and feature flags will be picked up by a future skill that wires CI workflows and agent-context files.

## Next steps

Next: a future skill will set up agent context (`CLAUDE.md`, `AGENTS.md`). For now, your project is scaffolded and verified — happy hacking.

Useful manual steps in the meantime:

- `git init` (if you have not already) to start your own repo history.
- Add a `.gitignore` for .NET artifacts (`bin/`, `obj/`, `*.user`, etc.) — `dotnet new gitignore` lands a sensible default. The `dotnet build` run above already produced `bin/` and `obj/` under cwd.
- The PRD calls for auth (FR-001/002/006/020). Decide whether to use ASP.NET Core Identity (built-in, EF-backed) or another approach; the scaffold itself has no auth wired yet.
- The scaffold is a webapi template — it returns JSON, not HTML. If you want server-rendered pages instead of (or alongside) a JSON API, decide between Razor Pages, MVC views, or Blazor and adjust the project accordingly.
- File a toolkit issue for the `{name}` substitution bug on non-JS starters (.NET, Django, Spring) so the registry contract aligns with the actual CLIs.
