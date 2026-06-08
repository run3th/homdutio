import { inject } from '@angular/core';
import { catchError, firstValueFrom, of, timeout } from 'rxjs';

import { AuthService } from './auth.service';

/**
 * How long the blocking startup restore waits for the silent refresh before giving up. Bounds
 * bootstrap so a slow/down API falls through to a logged-out render (`/login`) rather than an
 * infinite spinner.
 */
export const SESSION_RESTORE_TIMEOUT_MS = 5000;

/**
 * Runs once before the router/guards, so the synchronous `authGuard` sees an authenticated user
 * after a reload/reopen/restart. If a refresh token is stored, it attempts one silent refresh
 * (bounded by {@link SESSION_RESTORE_TIMEOUT_MS}); resolves regardless of outcome — success puts the
 * access token in memory, failure/timeout leaves the app logged-out. No stored token → resolves
 * immediately. Wired via `provideAppInitializer`, which runs this in an injection context.
 */
export function restoreSession(): Promise<void> {
  const auth = inject(AuthService);

  if (!auth.hasRefreshToken) {
    return Promise.resolve();
  }

  return firstValueFrom(
    auth.refresh().pipe(
      timeout(SESSION_RESTORE_TIMEOUT_MS),
      catchError(() => of(false)),
    ),
  ).then(() => undefined);
}
