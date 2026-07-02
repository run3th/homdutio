import { test, expect } from '@playwright/test';

/**
 * Seed exemplar — the pattern every generated E2E spec in this project follows.
 * Seed quality is test quality: whatever this shows, generated tests reproduce.
 *
 * The four levers it demonstrates for this app:
 *   - Role/label/text locators ONLY. The app has zero data-testid; everything is
 *     reachable by getByRole / getByLabel / getByText.
 *   - A self-contained cycle: setup (register → login → create household),
 *     action (create a task), assertion (it renders on the board), cleanup
 *     (delete it). No dependence on any other test.
 *   - Wait on state, never on time — waitForURL / web-first toBeVisible /
 *     waitForResponse(/\/api\/tasks$/). Never page.waitForTimeout().
 *   - Unique per-run data (timestamp-suffixed email + household + task) so parallel
 *     runs and re-runs never collide.
 *
 * NOTE — auth is driven through the UI on purpose. The access token is an in-memory
 * signal (auth.service.ts), so storageState cannot carry it; UI login is both the
 * only reliable path AND part of the token/refresh seam Risk #4 protects. Do NOT
 * "optimize" generated specs to storageState — it removes real coverage. See
 * web/e2e/CLAUDE.md.
 *
 * getByLabel uses { exact: true } for "Email"/"Password" because the register page
 * also carries an aria-label="Password requirements" list — a non-exact match would
 * hit two elements (strict-mode violation).
 */
const PASSWORD = 'P@ssw0rd!23';

test('a created task renders on the board and can be deleted', async ({ page }) => {
  const stamp = Date.now();
  const email = `e2e-seed-${stamp}@homdutio.test`;
  const householdName = `Seed House ${stamp}`;
  const taskTitle = `Seed chore ${stamp}`;

  // Setup — register, then log in (no auto-login: register hands off to /login).
  await page.goto('/register');
  await page.getByLabel('Email', { exact: true }).fill(email);
  await page.getByLabel(/Display name/).fill(`Seed ${stamp}`);
  await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
  await page.getByRole('button', { name: 'Create account' }).click();

  await page.waitForURL(/\/login/);
  await page.getByLabel('Email', { exact: true }).fill(email);
  await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
  await page.getByRole('button', { name: /Log in/ }).click();

  // A first-time user has no household — the guard lands them on create-household.
  await page.waitForURL(/\/create-household/);
  await page.getByLabel('Household name', { exact: true }).fill(householdName);
  await page.getByRole('button', { name: 'Create household' }).click();

  await page.waitForURL(/\/board/);
  await expect(page.getByRole('heading', { name: 'Task board' })).toBeVisible();

  // Action — create a task via the topbar "New task" dialog.
  await page.getByRole('button', { name: 'New task' }).click();
  await page.getByLabel('Title', { exact: true }).fill(taskTitle);
  await page.getByRole('button', { name: 'Add task' }).click();

  // Assertion — the card renders on the board (its title is a button opening detail).
  await expect(page.getByRole('button', { name: taskTitle })).toBeVisible();

  // Cleanup — delete the task through its ⋯ menu + confirm dialog.
  await page.getByRole('button', { name: 'Task actions' }).click();
  await page.getByRole('menuitem', { name: 'Delete' }).click();
  await page.getByRole('button', { name: 'Delete' }).click();
  await expect(page.getByRole('button', { name: taskTitle })).toBeHidden();
});
