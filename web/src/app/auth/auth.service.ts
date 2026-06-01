import { computed, Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';

import { HouseholdService } from '../household/household.service';

/** Request body shared by register and login (matches the F-02 endpoints). */
export interface AuthRequest {
  email: string;
  password: string;
}

/** Success body of `POST /api/auth/login` (camelCase JSON of the C# LoginResponse). */
export interface LoginResponse {
  accessToken: string;
  expiresAtUtc: string;
}

/**
 * Single source of truth for auth state and the only place the JWT lives.
 *
 * The token is held in an in-memory signal only — never localStorage/sessionStorage
 * (XSS exposure the roadmap warns about). A full page reload therefore logs the user
 * out, which is accepted for v1 (no refresh tokens in F-02).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly households = inject(HouseholdService);

  /** The in-memory access token; `null` when logged out. */
  private readonly _token = signal<string | null>(null);
  /** Email of the signed-in user, captured at login for display on the home page. */
  private readonly _email = signal<string | null>(null);

  /** Public read-only auth state derived from the token signal. */
  readonly isAuthenticated = computed(() => this._token() !== null);
  /** Public read-only email of the signed-in user (or `null`). */
  readonly email = this._email.asReadonly();

  /** Raw token accessor for the bearer interceptor. */
  get token(): string | null {
    return this._token();
  }

  /** `POST /api/auth/register` — 200 (empty body) on success; 400 ValidationProblem on failure. */
  register(email: string, password: string): Observable<void> {
    return this.http.post<void>('/api/auth/register', { email, password } satisfies AuthRequest);
  }

  /** `POST /api/auth/login` — stores the token + email on success; 401 on bad credentials. */
  login(email: string, password: string): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>('/api/auth/login', { email, password } satisfies AuthRequest)
      .pipe(
        tap((response) => {
          this._token.set(response.accessToken);
          this._email.set(email);
        }),
      );
  }

  /**
   * Clears the in-memory token + email and resets household state so a logout/login as a different
   * user on the same page load doesn't leak the prior household. No API call — the caller navigates.
   */
  logout(): void {
    this._token.set(null);
    this._email.set(null);
    this.households.clearOnLogout();
  }
}
