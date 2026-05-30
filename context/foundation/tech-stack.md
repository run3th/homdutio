---
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
---

## Why this stack

A solo builder shipping a household-chore-tracking MVP in 3 weeks of after-hours work needs a strongly-typed backend plus a kanban-shaped SPA with drag-reorder (FR-021) and mobile responsiveness at ≤400px (NFR-2). ASP.NET Core (the `dotnet` starter, webapi template) clears all four agent-friendly gates and is bootstrapper-verified; Angular is the chosen frontend, scaffolded with the Angular CLI and built into ASP.NET's `wwwroot/` so a single Azure App Service serves both. Routing splits cleanly: `/api/**` to controllers, everything else falls back to `index.html` for the Angular router. Auth (FR-001/002/006/020) maps to ASP.NET Core Identity primitives for the user store, with stateless JWT bearer tokens issued to the SPA (roadmap decision 2026-05-30, superseding the original cookie-auth note — JWT keeps the API stateless and sidesteps the data-protection key-ring scale-out footgun, at the cost of SPA-side token storage/refresh). Deployment lands on Azure App Service — the .NET card's first default — with GitHub Actions auto-deploying on merge to main; single-region single-instance is acceptable per the no-multi-region non-goal. Bootstrapper covers the .NET webapi scaffold (verified); the Angular hybrid layer (`ng new`, `Program.cs` UseStaticFiles + SPA fallback, csproj build target running `ng build`) is post-bootstrap manual work. Payments, realtime, AI, and background jobs are out of scope per PRD non-goals.
