import { execFileSync } from 'node:child_process';

/**
 * Apply EF Core migrations to the E2E database BEFORE Playwright's webServer boots
 * the API. Migrations are NEVER applied on app startup in this project
 * (src/Homdutio.Data/MIGRATIONS.md), so the harness must do it out-of-band — both
 * locally (LocalDB, via user-secrets) and in CI (run-scoped DB, via env vars).
 *
 * The connection string is read by the startup project from the ambient environment
 * (user-secrets in Development, or ConnectionStrings__DefaultConnection env var), so
 * it is inherited from this process — nothing to pass explicitly here.
 *
 * Fails fast (execFileSync throws on non-zero exit), aborting the run if migrations
 * cannot be applied rather than letting specs race an unmigrated schema.
 */
async function globalSetup(): Promise<void> {
  execFileSync(
    'dotnet',
    [
      'ef',
      'database',
      'update',
      '--project',
      'src/Homdutio.Data',
      '--startup-project',
      'src/Homdutio.Api',
    ],
    { cwd: '..', stdio: 'inherit' },
  );
}

export default globalSetup;
