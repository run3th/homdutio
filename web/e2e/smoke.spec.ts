import { test, expect } from '@playwright/test';

/**
 * Harness sanity check — NOT a risk test.
 *
 * Proves the whole harness boots end-to-end: both servers started, migrations
 * applied, and the SPA (baseURL) is reachable and rendering. Uses a role-based
 * locator and state-waiting (toBeVisible) only — the pattern every generated spec
 * follows. Login and Register share "Email"/"Password" labels, so the login control
 * is matched by role + accessible name (scoped per research.md §F).
 */
test('smoke: login page renders its Log in control', async ({ page }) => {
  await page.goto('/login');
  await expect(page.getByRole('button', { name: /Log in/ })).toBeVisible();
});
