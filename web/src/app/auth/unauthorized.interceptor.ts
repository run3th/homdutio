import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';

import { AuthService } from './auth.service';

/**
 * Auth endpoints whose 4xx are normal results, not session-expiry signals: login/register (bad
 * credentials, handled by the forms), refresh/logout (their own 401 must never trigger another
 * refresh — that would recurse), and forgot/reset-password (their 400/401/429 render inline on the
 * reset forms; the user is logged out during that flow, so a refresh attempt is meaningless).
 */
const AUTH_ENDPOINTS = [
  '/api/auth/login',
  '/api/auth/register',
  '/api/auth/refresh',
  '/api/auth/logout',
  '/api/auth/forgot-password',
  '/api/auth/reset-password',
];

/**
 * Session-expiry recovery. On a protected `401`, attempt one silent refresh and replay the original
 * request once with the new access token; if the refresh fails (or the replay 401s again), discard
 * auth state and redirect to `/login`. 401s from the auth endpoints pass through untouched so the
 * login/register components render their own messages and refresh failures don't recurse.
 */
export const unauthorizedInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const giveUp = (error: unknown) => {
    auth.logout();
    void router.navigate(['/login']);
    return throwError(() => error);
  };

  return next(req).pipe(
    catchError((error: unknown) => {
      const isAuthEndpoint = AUTH_ENDPOINTS.some((path) => req.url.includes(path));
      const is401 = error instanceof HttpErrorResponse && error.status === 401;

      if (!is401 || isAuthEndpoint) {
        return throwError(() => error);
      }

      // First 401: a single silent refresh (shared in-flight), then replay the original request once.
      return auth.refresh().pipe(
        switchMap((refreshed) => {
          if (!refreshed) {
            return giveUp(error);
          }

          const retried = req.clone({
            setHeaders: { Authorization: `Bearer ${auth.token}` },
          });
          // The replay is NOT re-entered by this catchError (next() goes downstream), so its own
          // 401 is handled inline here — one retry only, then give up. No refresh loop.
          return next(retried).pipe(
            catchError((retryError: unknown) =>
              retryError instanceof HttpErrorResponse && retryError.status === 401
                ? giveUp(retryError)
                : throwError(() => retryError),
            ),
          );
        }),
      );
    }),
  );
};
