# E2E Testing Rules (Playwright)

These rules govern every spec in `web/e2e/`. They keep generated tests stable by
default. The exemplar is `seed.spec.ts` — model new specs on it.

- Use `getByRole`, `getByLabel`, `getByText` as primary locators. This app has
  **zero `data-testid`**; everything is reachable by role/label/text. Fall back to
  `getByTestId` only when accessibility attributes are genuinely ambiguous.
- Never use CSS selectors, XPath, or DOM structure to locate elements.
- Each test must be independently runnable — its own setup, action, assertion, and
  cleanup. No shared state between tests.
- Never use `page.waitForTimeout()`. Wait for state: `toBeVisible()`,
  `waitForURL()`, `waitForResponse()`.
- Assert the observable business outcome, not implementation details. Control
  question for every assertion: *would it fail if the `test-plan.md` risk came true?*
- Use unique identifiers (timestamp suffix) for all test data — emails, household
  names, task titles — so parallel runs and re-runs never collide.

## App-specific rules (read before touching a spec)

- **Log in through the UI — do NOT use `storageState`.** The access token is an
  in-memory signal (`auth.service.ts`); `storageState` cannot carry it, and the
  UI-login → token/refresh seam is exactly what Risk #4 exists to protect. This
  reverses the usual Playwright "auth via storageState" guidance *for this app on
  purpose*. Register → login is the setup path (register does **not** auto-login; it
  hands off to `/login`).
- **Cross-member convergence rides a 4 s poll**, not a push (`board.component.ts`,
  `POLL_INTERVAL_MS = 4000`). To assert one member sees another's change, wait it out
  with a web-first assertion whose timeout comfortably exceeds the poll (e.g.
  `toBeVisible({ timeout: 15_000 })`). That is waiting for *state* — it passes the
  instant the poll converges — not a fixed sleep.
- **Keep the observing page visible.** A backgrounded page has `document.hidden ===
  true`, which suppresses the poll, so the observer never converges. Call
  `page.bringToFront()` on a page before asserting its poll-driven convergence.
- **Two members = two `browser.newContext()`** for clean `localStorage` / refresh-token
  isolation.
- **No account/household deletion API exists.** Isolate by unique per-run ids
  (above); tear down only the data a spec can remove (e.g. a still-open task).
- Migrations are applied out-of-band in `global-setup.ts` (never on app startup); the
  `webServer` gates on the API's `/health`. Don't add startup-migration assumptions.
- Trace/screenshot are debugging aids (config), **not** visual assertions — no
  pixel/styling assertions (excluded by `test-plan.md` §7).
