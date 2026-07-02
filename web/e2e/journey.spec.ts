import { test, expect, type Page } from '@playwright/test';

/**
 * risk: context/foundation/test-plan.md #4 — the full register → create-household →
 *       invite → join → claim → done → admin-confirm journey holds across the real
 *       running stack, with BOTH members observing the same board state at each
 *       transition (the auth hop + returnUrl, token storage, the 4 s polling refresh,
 *       and board re-render — seams every unit/integration test passes alone).
 * seed: web/e2e/seed.spec.ts
 * plan: context/changes/testing-e2e-journey/plan.md (Phase 2)
 *
 * Cross-member convergence rides the board's 4 s poll (board.component.ts) plus request
 * latency, so the observer's assertions use POLL_CONVERGENCE_MS as their web-first
 * ceiling — they pass the instant the poll converges, they are NOT fixed sleeps. The
 * observing page is brought to front before each such assertion, because a hidden page
 * suppresses the poll (document.hidden) and would never converge.
 */
const PASSWORD = 'P@ssw0rd!23';
const POLL_CONVERGENCE_MS = 15_000;

interface Account {
  email: string;
  name: string;
}

/** Fill + submit the /register form (page must be on /register); waits for the /login hand-off. */
async function submitRegister(page: Page, account: Account): Promise<void> {
  await page.getByLabel('Email', { exact: true }).fill(account.email);
  await page.getByLabel(/Display name/).fill(account.name);
  await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
  await page.getByRole('button', { name: 'Create account' }).click();
  await page.waitForURL(/\/login/);
}

/** Fill + submit the /login form (page must be on /login). Email may be prefilled; set it anyway. */
async function submitLogin(page: Page, account: Account): Promise<void> {
  await page.getByLabel('Email', { exact: true }).fill(account.email);
  await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
  await page.getByRole('button', { name: /Log in/ }).click();
}

test.describe('MVP two-member journey (Risk #4)', () => {
  test('both members converge on the same board state through the 8-step journey', async ({
    browser,
  }) => {
    const stamp = Date.now();
    const admin: Account = { email: `e2e-admin-${stamp}@homdutio.test`, name: `Admin ${stamp}` };
    const member: Account = { email: `e2e-member-${stamp}@homdutio.test`, name: `Member ${stamp}` };
    const householdName = `E2E House ${stamp}`;
    const taskTitle = `E2E chore ${stamp}`;

    // Two contexts = clean localStorage + refresh-token isolation per member.
    const adminContext = await browser.newContext();
    const memberContext = await browser.newContext();
    const adminPage = await adminContext.newPage();
    const memberPage = await memberContext.newPage();

    try {
      // Step 1 — Admin: register → login → create the household.
      await adminPage.goto('/register');
      await submitRegister(adminPage, admin);
      await submitLogin(adminPage, admin);
      // No household yet → the guard lands the admin on create-household.
      await adminPage.waitForURL(/\/create-household/);
      await adminPage.getByLabel('Household name', { exact: true }).fill(householdName);
      await adminPage.getByRole('button', { name: 'Create household' }).click();
      await adminPage.waitForURL(/\/board/);
      await expect(adminPage.getByRole('heading', { name: 'Task board' })).toBeVisible();

      // Step 2 — Admin: open the Invite dialog and capture the /join/<token> link. The token is read
      // from the invite-generation response: the <code id="invite-link"> box carries no role/accessible
      // name to target, and the API returns only the token to the client (invite.service.ts).
      const invitePromise = adminPage.waitForResponse(
        (r) => /\/api\/households\/invites$/.test(r.url()) && r.request().method() === 'POST',
      );
      await adminPage.getByRole('button', { name: 'Invite' }).click();
      await expect(adminPage.getByRole('heading', { name: /Invite to/ })).toBeVisible();
      const { token } = (await (await invitePromise).json()) as { token: string };
      expect(token).toBeTruthy();
      await adminPage.getByRole('button', { name: 'Close' }).click();

      // Step 3 — Member (2nd context): cross the invite auth hop. Logged-out landing → register
      // (returnUrl preserved) → redirected to login (returnUrl preserved) → back on /join → Accept & join.
      await memberPage.goto(`/join/${token}`);
      await expect(
        memberPage.getByRole('heading', { name: `Join ${householdName}` }),
      ).toBeVisible();
      await memberPage.getByRole('link', { name: 'Create one' }).click();
      await memberPage.waitForURL(/\/register\?returnUrl=/);
      await submitRegister(memberPage, member);
      // register forwarded the returnUrl to /login; logging in returns the member to /join/<token>.
      await expect(memberPage).toHaveURL(/returnUrl=/);
      await submitLogin(memberPage, member);
      await memberPage.waitForURL((url) => url.pathname.startsWith('/join/'));
      await memberPage.getByRole('button', { name: /Accept & join/ }).click();
      await memberPage.waitForURL(/\/board/);
      await expect(memberPage.getByRole('heading', { name: 'Task board' })).toBeVisible();

      // Step 4 — Admin: create a task via the "New task" dialog.
      await adminPage.bringToFront();
      await adminPage.getByRole('button', { name: 'New task' }).click();
      await adminPage.getByLabel('Title', { exact: true }).fill(taskTitle);
      await adminPage.getByRole('button', { name: 'Add task' }).click();
      await expect(adminPage.getByRole('button', { name: taskTitle })).toBeVisible();

      // Step 5 — Member: the task converges onto the member's board (poll), member claims it; the
      // admin's board then converges to show the member as the claimer (cross-stack, poll-driven).
      await memberPage.bringToFront();
      await expect(memberPage.getByRole('button', { name: taskTitle })).toBeVisible({
        timeout: POLL_CONVERGENCE_MS,
      });
      await memberPage.getByRole('button', { name: 'Claim' }).click();
      await expect(memberPage.getByText('Claimed by')).toBeVisible();

      await adminPage.bringToFront();
      await expect(adminPage.getByText('Claimed by')).toBeVisible({ timeout: POLL_CONVERGENCE_MS });
      await expect(adminPage.getByText(member.name)).toBeVisible({ timeout: POLL_CONVERGENCE_MS });

      // Step 6 — Member: mark the claimed task done. The member (non-admin) sees "Awaiting
      // confirmation"; the admin's board converges to show the admin's Confirm affordance on the
      // now-Done task (the admin, not the claimer, may confirm — task-card.component.html).
      await memberPage.bringToFront();
      await memberPage.getByRole('button', { name: 'Mark done' }).click();
      await expect(memberPage.getByText('Awaiting confirmation')).toBeVisible();

      await adminPage.bringToFront();
      await expect(adminPage.getByRole('button', { name: /Confirm/ })).toBeVisible({
        timeout: POLL_CONVERGENCE_MS,
      });

      // Step 7 — Admin: confirm the task. It closes and drops off BOTH boards — immediately for the
      // admin, on the next poll for the member. The shared end state: neither member sees the task.
      await adminPage.getByRole('button', { name: /Confirm/ }).click();
      await expect(adminPage.getByRole('button', { name: taskTitle })).toBeHidden();

      await memberPage.bringToFront();
      await expect(memberPage.getByRole('button', { name: taskTitle })).toBeHidden({
        timeout: POLL_CONVERGENCE_MS,
      });
    } finally {
      // No account/household deletion API exists — isolation is by the unique per-run ids above.
      // The task ends confirmed/closed (already off the board), so there is no open data to remove;
      // dispose both contexts to release their storage.
      await adminContext.close();
      await memberContext.close();
    }
  });
});
