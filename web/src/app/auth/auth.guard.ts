import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthService } from './auth.service';

/**
 * Protects authenticated routes. Reads the in-memory auth signal synchronously (no async
 * race); returns `true` when authenticated, otherwise a `UrlTree` redirect to `/login`.
 * After a full page reload the token is gone, so the guard sends the user to `/login` — expected.
 */
export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.isAuthenticated() ? true : router.createUrlTree(['/login']);
};
