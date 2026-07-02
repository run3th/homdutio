import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright E2E harness for the running Homdutio stack.
 *
 * baseURL is the SPA (ng serve on :4200), which proxies /api -> the API on :5252
 * (web/proxy.conf.json). `webServer` boots both servers and gates readiness on the
 * API's /health probe before any spec runs. `globalSetup` applies EF migrations
 * out-of-band first (migrations are NEVER applied on app startup).
 *
 * Discipline this config encodes for every spec (see /10x-e2e rules):
 *   - Wait on state, never on time — no fixed timeouts in specs.
 *   - Role/label/text locators only — the app has zero data-testid.
 *   - Trace/screenshot on failure are debugging aids, NOT visual assertions.
 *
 * The API needs ConnectionStrings__DefaultConnection (LocalDB) and Jwt:SigningKey
 * supplied out-of-band — via user-secrets locally (Development env) or env vars in
 * CI. Any env var present in this process is forwarded to the API server below.
 */

// Forward the out-of-band secrets to the spawned API only when set in this process,
// so local runs can rely on user-secrets while CI supplies them via the environment.
const apiEnv: Record<string, string> = {};
for (const key of ['ConnectionStrings__DefaultConnection', 'Jwt__SigningKey']) {
  const value = process.env[key];
  if (value) apiEnv[key] = value;
}

export default defineConfig({
  testDir: './e2e',
  globalSetup: './e2e/global-setup.ts',
  // Fail the run rather than let a hung server or missing state stall CI.
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['html', { open: 'never' }], ['list']] : 'list',
  use: {
    baseURL: 'http://localhost:4200',
    // Debugging aids only — not pixel/visual assertions (excluded by the test plan).
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: [
    {
      // API on :5252 (http launch profile => ASPNETCORE_ENVIRONMENT=Development).
      command: 'dotnet run --project ../src/Homdutio.Api --launch-profile http',
      url: 'http://localhost:5252/health',
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      stdout: 'pipe',
      stderr: 'pipe',
      env: apiEnv,
    },
    {
      // SPA on :4200 (ng serve, proxies /api -> :5252 via proxy.conf.json).
      command: 'npm run start',
      url: 'http://localhost:4200',
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
  ],
});
