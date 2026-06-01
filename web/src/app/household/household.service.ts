import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map, of, tap } from 'rxjs';

/** The caller's household, mirroring the camelCase JSON of the C# HouseholdResponse. */
export interface Household {
  id: string;
  name: string;
  role: 'Admin' | 'Member';
}

/**
 * Single source of truth for the caller's household membership, mirroring {@link AuthService}.
 *
 * Holds the household in an in-memory signal and loads it once after login (the membership guard
 * triggers the first {@link loadMine}). A separate `_loaded` flag lets the guard distinguish
 * "not fetched yet" from "fetched, no household" so it never refetches on every navigation.
 */
@Injectable({ providedIn: 'root' })
export class HouseholdService {
  private readonly http = inject(HttpClient);

  /** The caller's household; `null` when they have none (or before the first load). */
  private readonly _household = signal<Household | null>(null);
  /** Whether membership has been fetched at least once this session. */
  private readonly _loaded = signal(false);

  /** Public read-only household state. */
  readonly current = this._household.asReadonly();
  /** Whether membership has been resolved (loaded once). */
  readonly loaded = this._loaded.asReadonly();

  /**
   * `GET /api/households/me` — the caller's household, or `null` (the backend's 204) when they have
   * none. Caches: once loaded, returns the cached value without re-hitting the network.
   */
  loadMine(): Observable<Household | null> {
    if (this._loaded()) {
      return of(this._household());
    }

    return this.http.get<Household | null>('/api/households/me').pipe(
      map((body) => body ?? null),
      tap((household) => {
        this._household.set(household);
        this._loaded.set(true);
      }),
    );
  }

  /** `POST /api/households` — creates a household and caches it as the caller's on success. */
  create(name: string): Observable<Household> {
    return this.http.post<Household>('/api/households', { name }).pipe(
      tap((household) => {
        this._household.set(household);
        this._loaded.set(true);
      }),
    );
  }

  /**
   * Resets both the household and the loaded flag. MUST reset `_loaded` too — otherwise a different
   * user logging in on the same page load is treated as "loaded, no household" and mis-routed to
   * `/create-household`, since the guard never refetches. Wired into {@link AuthService.logout}.
   */
  clearOnLogout(): void {
    this._household.set(null);
    this._loaded.set(false);
  }
}
