import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

import { AuthService } from './auth.service';

/**
 * Auth endpoints whose 401s are normal results the forms handle (bad credentials),
 * not session-expiry signals. The interceptor must not discard-and-redirect on these.
 */
const AUTH_ENDPOINTS = ['/api/auth/login', '/api/auth/register'];

/**
 * Centralises session-expiry recovery: a `401` on a protected call discards auth state
 * and redirects to `/login`. 401s from the auth endpoints pass through untouched so the
 * login/register components can render their own messages.
 */
export const unauthorizedInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: unknown) => {
      const isAuthEndpoint = AUTH_ENDPOINTS.some((path) => req.url.includes(path));
      if (error instanceof HttpErrorResponse && error.status === 401 && !isAuthEndpoint) {
        auth.logout();
        void router.navigate(['/login']);
      }
      return throwError(() => error);
    }),
  );
};
