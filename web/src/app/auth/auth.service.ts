import { computed, Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { catchError, finalize, map, Observable, of, shareReplay, tap } from 'rxjs';

import { HouseholdService } from '../household/household.service';
import { TaskService } from '../board/task.service';

/** Request body shared by register and login (matches the F-02 endpoints). */
export interface AuthRequest {
  email: string;
  password: string;
}

/** Success body of `POST /api/auth/login` and `/api/auth/refresh` (camelCase JSON of the C# LoginResponse). */
export interface LoginResponse {
  accessToken: string;
  expiresAtUtc: string;
  refreshToken: string;
}

/** Body of `GET /api/auth/me` (camelCase JSON of the C# MeResponse). `avatarUrl` is null until S-09 Phase 3. */
export interface MeResponse {
  sub: string | null;
  email: string | null;
  displayName: string | null;
  avatarUrl: string | null;
}

/** The single `localStorage` key holding the current (rotating) refresh token. */
export const REFRESH_TOKEN_KEY = 'homdutio.refreshToken';

/**
 * Single source of truth for auth state.
 *
 * The short-lived access token lives in an in-memory signal only — never persisted (XSS exposure).
 * The longer-lived refresh token is persisted in `localStorage` so a reload/reopen/restart can
 * silently re-mint an access token (S-10). The refresh token rotates on every use and is revoked
 * server-side on logout.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly households = inject(HouseholdService);
  private readonly tasks = inject(TaskService);

  /** The in-memory access token; `null` when logged out. */
  private readonly _token = signal<string | null>(null);
  /** Email of the signed-in user, captured at login for display on the home page. */
  private readonly _email = signal<string | null>(null);
  /** Display name of the signed-in user, resolved from `/api/auth/me`; `null` until it loads. */
  private readonly _displayName = signal<string | null>(null);
  /** Avatar URL of the signed-in user, resolved from `/api/auth/me`; `null` when they have no photo. */
  private readonly _avatarUrl = signal<string | null>(null);

  /** Shared in-flight refresh so concurrent 401s (and a double-fired startup) collapse to one call. */
  private refreshInFlight: Observable<boolean> | null = null;

  /** Public read-only auth state derived from the token signal. */
  readonly isAuthenticated = computed(() => this._token() !== null);
  /** Public read-only email of the signed-in user (or `null`). */
  readonly email = this._email.asReadonly();
  /** Public read-only display name of the signed-in user (or `null` until `/me` resolves). */
  readonly displayName = this._displayName.asReadonly();
  /** Public read-only avatar URL of the signed-in user (or `null` when they have no photo). */
  readonly avatarUrl = this._avatarUrl.asReadonly();

  /** Raw token accessor for the bearer interceptor. */
  get token(): string | null {
    return this._token();
  }

  /** Whether a refresh token is persisted — the startup initializer uses this to decide on a restore. */
  get hasRefreshToken(): boolean {
    return this.readRefreshToken() !== null;
  }

  /**
   * `POST /api/auth/register` — 200 (empty body) on success; 400 ValidationProblem on failure.
   * `displayName` is optional; the backend falls back to the email local-part when it is blank.
   */
  register(email: string, password: string, displayName?: string): Observable<void> {
    return this.http.post<void>('/api/auth/register', { email, password, displayName });
  }

  /**
   * `POST /api/auth/forgot-password` — requests a reset email (S-08). Always 200 with a generic body
   * regardless of whether the account exists (anti-enumeration), so the caller just shows the same
   * confirmation. Touches no auth state — the user is logged out during this flow.
   */
  requestPasswordReset(email: string): Observable<void> {
    return this.http.post<void>('/api/auth/forgot-password', { email });
  }

  /**
   * `POST /api/auth/reset-password` — sets a new password from the emailed link's token (S-08). 200 on
   * success; 400 ValidationProblem for a weak password or an invalid/expired token. Touches no auth
   * state; the caller routes to `/login` on success so the new credential is exercised explicitly.
   */
  resetPassword(email: string, token: string, newPassword: string): Observable<void> {
    return this.http.post<void>('/api/auth/reset-password', { email, token, newPassword });
  }

  /**
   * `POST /api/auth/login` — stores the access token + email and persists the refresh token on
   * success; 401 on bad credentials.
   */
  login(email: string, password: string): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>('/api/auth/login', { email, password } satisfies AuthRequest)
      .pipe(
        tap((response) => {
          this._token.set(response.accessToken);
          this._email.set(email);
          this.writeRefreshToken(response.refreshToken);
          // Pull the real display name + avatar into state now that the bearer is set (S-09).
          this.loadMe();
        }),
      );
  }

  /**
   * `POST /api/auth/refresh` — exchanges the stored refresh token for a fresh access token and a
   * rotated refresh token. Resolves `true` on success (access token in memory, rotated token stored)
   * and `false` otherwise, clearing the dead credentials. No stored token → `false` without a call.
   * Concurrent callers share one in-flight request so a burst of 401s triggers a single refresh.
   */
  refresh(): Observable<boolean> {
    if (this.refreshInFlight) {
      return this.refreshInFlight;
    }

    const refreshToken = this.readRefreshToken();
    if (!refreshToken) {
      return of(false);
    }

    this.refreshInFlight = this.http
      .post<LoginResponse>('/api/auth/refresh', { refreshToken })
      .pipe(
        map((response) => {
          this._token.set(response.accessToken);
          // Refresh carries no email field, but the access token's `email` claim is the same source
          // of truth login uses — recover it so the topbar identity survives a reload (S-10).
          this._email.set(emailFromAccessToken(response.accessToken));
          this.writeRefreshToken(response.refreshToken);
          // Restore the display name + avatar after a silent refresh/reopen (S-09 / S-10).
          this.loadMe();
          return true;
        }),
        catchError(() => {
          // Refresh rejected (expired/revoked/replayed) — drop the dead credentials. The caller
          // decides whether to also clear household/task state and redirect.
          this._token.set(null);
          this.clearRefreshToken();
          return of(false);
        }),
        finalize(() => {
          this.refreshInFlight = null;
        }),
        shareReplay(1),
      );

    return this.refreshInFlight;
  }

  /**
   * Revokes the session server-side (fire-and-forget — never blocks the UI, ignores failures) and
   * clears all client auth state: in-memory token + email, the stored refresh token, and household/
   * task state so a re-login as a different user on the same page load can't leak the prior household.
   */
  logout(): void {
    const refreshToken = this.readRefreshToken();
    if (refreshToken) {
      this.http.post('/api/auth/logout', { refreshToken }).subscribe({ error: () => {} });
    }

    this._token.set(null);
    this._email.set(null);
    this._displayName.set(null);
    this._avatarUrl.set(null);
    this.clearRefreshToken();
    this.households.clearOnLogout();
    this.tasks.clearOnLogout();
  }

  /**
   * `GET /api/auth/me` — pulls the signed-in user's display name + avatar into state. Fire-and-forget:
   * the header/menu update reactively when it resolves, and a failure leaves prior values intact (the
   * bearer is already set by the caller, so this carries it). Called after login and after a silent
   * refresh so the identity survives a reload (S-09 / S-10).
   */
  loadMe(): void {
    this.http.get<MeResponse>('/api/auth/me').subscribe({
      next: (me) => {
        if (me.email) {
          this._email.set(me.email);
        }
        this._displayName.set(me.displayName ?? null);
        this._avatarUrl.set(me.avatarUrl ?? null);
      },
      error: () => {
        // Display-only state — a failed /me must not disrupt the session.
      },
    });
  }

  /**
   * Applies a profile change made via {@link ProfileService} so the header/menu update immediately,
   * without re-fetching `/me`. Only the provided fields are touched (pass `undefined` to leave one as-is).
   */
  setProfile(profile: { displayName?: string | null; avatarUrl?: string | null }): void {
    if (profile.displayName !== undefined) {
      this._displayName.set(profile.displayName);
    }
    if (profile.avatarUrl !== undefined) {
      this._avatarUrl.set(profile.avatarUrl);
    }
  }

  private readRefreshToken(): string | null {
    return localStorage.getItem(REFRESH_TOKEN_KEY);
  }

  private writeRefreshToken(token: string): void {
    localStorage.setItem(REFRESH_TOKEN_KEY, token);
  }

  private clearRefreshToken(): void {
    localStorage.removeItem(REFRESH_TOKEN_KEY);
  }
}

/**
 * Reads the `email` claim from a JWT access token without verifying its signature (the server already
 * vouched for it — this is display-only). Returns `null` for any malformed/non-JWT token so a bad value
 * never poisons the topbar identity.
 */
function emailFromAccessToken(accessToken: string): string | null {
  const payload = accessToken.split('.')[1];
  if (!payload) {
    return null;
  }

  try {
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
    const claims = JSON.parse(json) as { email?: unknown };
    return typeof claims.email === 'string' && claims.email.length > 0 ? claims.email : null;
  } catch {
    return null;
  }
}
