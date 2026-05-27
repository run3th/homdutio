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

A solo builder shipping a household-chore-tracking MVP in 3 weeks of after-hours work needs a strongly-typed, well-documented starter for a small two-account web app with auth. ASP.NET Core (the `dotnet` starter, webapi template) clears all four agent-friendly gates (typed by language; convention-based via the template; popular in training data; mature first-party documentation) and the card's bootstrapper confidence is verified. Auth is in scope (FR-001/002/006/020 cover registration, login, invite-link join, password reset) and can be implemented on top of ASP.NET Core Identity or a similar built-in primitive without external services. Deployment lands on Azure App Service — the .NET card's first default — which is the cheapest path to a first deploy for ASP.NET Core; single-region single-instance is acceptable for v1 per the no-multi-region non-goal. CI runs on GitHub Actions with auto-deploy on merge to main. Payments, realtime, AI, and background jobs are out of scope per PRD non-goals; a frontend (Razor Pages, Blazor, or a separate SPA) is a follow-on decision after the API skeleton lands.
