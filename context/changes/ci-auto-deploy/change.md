---
change_id: ci-auto-deploy
roadmap_id: F-04
title: CI auto-deploy (GitHub Actions build+test gate → OIDC deploy)
status: planned
created: 2026-05-31
updated: 2026-05-31
prerequisites: []
prd_refs: [tech-stack ci_default_flow]
---

# Change: ci-auto-deploy

Replace the manual Windows zip deploy with a GitHub Actions pipeline: PRs and pushes to main run a
build + test gate; merges to main then (behind a human-approved `production` environment) migrate the
Azure SQL DB, deploy the single .NET+Angular artifact to App Service via OIDC, and smoke-test `/health`.

Standalone infra (gates no slice). Also clears inherited carry-overs: sets the prod `Jwt__SigningKey`
(F-02), applies the `AddIdentity` migration to Azure SQL, and verifies deployed `/health` vs Azure SQL
(F-01 items 4.2/4.6).

See `plan.md` for the implementation contract and `plan-brief.md` for the two-page summary.
