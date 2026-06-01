import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, switchMap, tap } from 'rxjs';

/** The three lifecycle columns; mirrors the C# HouseholdTaskStatus enum (string-serialised). */
export type TaskStatus = 'ToDo' | 'InProgress' | 'Done';

/** A board task, mirroring the camelCase JSON of the C# TaskResponse (S-03). */
export interface Task {
  id: string;
  title: string;
  description?: string | null;
  category?: string | null;
  status: TaskStatus;
  createdByName: string;
  claimerName?: string | null;
  createdAtUtc: string;
  /** Server-computed affordance flags — the single source of truth for which actions the caller may take. */
  canClaim: boolean;
  canMarkDone: boolean;
  canConfirm: boolean;
  willSelfAttest: boolean;
}

/** Body for `POST /api/tasks` (matches the C# CreateTaskRequest). */
export interface CreateTaskRequest {
  title: string;
  description?: string;
  category?: string;
}

/**
 * Single source of truth for the board's tasks, mirroring {@link HouseholdService}.
 *
 * Holds the open tasks in an in-memory signal. Each mutation POSTs its action route and then refetches
 * via {@link load} so the acting client re-renders — there is no cross-member push in S-03 (polling is
 * S-06). A confirmed task simply stops coming back from `GET /api/tasks`, so it disappears with no client
 * bookkeeping. The client never compares identities: it renders exactly the affordance flags the DTO carries.
 */
@Injectable({ providedIn: 'root' })
export class TaskService {
  private readonly http = inject(HttpClient);

  private readonly _tasks = signal<Task[]>([]);

  /** Public read-only board state (open tasks for the caller's household). */
  readonly current = this._tasks.asReadonly();

  /** `GET /api/tasks` — the caller's open board; replaces the signal. */
  load(): Observable<Task[]> {
    return this.http.get<Task[]>('/api/tasks').pipe(tap((tasks) => this._tasks.set(tasks)));
  }

  /** `POST /api/tasks` then refetch so the new card appears in "To do". */
  create(request: CreateTaskRequest): Observable<Task[]> {
    return this.http.post<Task>('/api/tasks', request).pipe(switchMap(() => this.load()));
  }

  /** `POST /api/tasks/{id}/claim` then refetch. */
  claim(id: string): Observable<Task[]> {
    return this.http.post<Task>(`/api/tasks/${id}/claim`, {}).pipe(switchMap(() => this.load()));
  }

  /** `POST /api/tasks/{id}/done` then refetch. */
  markDone(id: string): Observable<Task[]> {
    return this.http.post<Task>(`/api/tasks/${id}/done`, {}).pipe(switchMap(() => this.load()));
  }

  /** `POST /api/tasks/{id}/confirm` then refetch (the confirmed task drops off the board). */
  confirm(id: string): Observable<Task[]> {
    return this.http.post<Task>(`/api/tasks/${id}/confirm`, {}).pipe(switchMap(() => this.load()));
  }

  /** Resets the board state. Wired into {@link AuthService.logout} alongside the household reset. */
  clearOnLogout(): void {
    this._tasks.set([]);
  }
}
