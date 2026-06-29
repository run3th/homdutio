import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { EMPTY, Observable, Subscription, catchError, interval, switchMap, tap } from 'rxjs';

/** The three lifecycle columns; mirrors the C# HouseholdTaskStatus enum (string-serialised). */
export type TaskStatus = 'ToDo' | 'InProgress' | 'Done';

/** A board task, mirroring the camelCase JSON of the C# TaskResponse (S-03). */
export interface Task {
  id: string;
  title: string;
  description?: string | null;
  /** Free-text tags (S-12) — the multi-value generalization of the old single `category`. */
  tags: string[];
  status: TaskStatus;
  createdByName: string;
  claimerName?: string | null;
  createdAtUtc: string;
  /** Server-computed affordance flags — the single source of truth for which actions the caller may take. */
  canClaim: boolean;
  canMarkDone: boolean;
  canConfirm: boolean;
  willSelfAttest: boolean;
  /** Edit is admin-only/any-column; delete stays To-do-only (FR-011/012, S-05). */
  canEdit: boolean;
  canDelete: boolean;
  /** Loop-recovery affordances (S-05): the claimer/admin may unclaim in-progress; an admin may send a Done task back. */
  canUnclaim: boolean;
  canSendBack: boolean;
  /** Per-task comment count for the card's 💬 badge (full thread loads lazily in the detail dialog). */
  commentCount: number;
}

/** A task comment, mirroring the camelCase JSON of the C# CommentResponse (S-05). */
export interface Comment {
  id: string;
  body: string;
  /** `Member` is a free-form note; `SendBack` is the reason an admin attached when returning a Done task. */
  kind: 'Member' | 'SendBack';
  authorName: string;
  createdAtUtc: string;
}

/** Body for `POST /api/tasks` (matches the C# CreateTaskRequest). */
export interface CreateTaskRequest {
  title: string;
  description?: string;
  tags: string[];
}

/** Body for `PUT /api/tasks/{id}` (matches the C# UpdateTaskRequest). */
export interface UpdateTaskRequest {
  title: string;
  description?: string;
  tags: string[];
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

  /** The live-poll subscription (S-06, F-03); `undefined` when polling is stopped. */
  private pollSub?: Subscription;
  /**
   * When set, an interval tick is suppressed even on a visible tab — the board raises it during a drag or
   * while the task dialog is open so a refetch never yanks the board out from under an in-progress action.
   */
  private readonly _paused = signal(false);

  /** `GET /api/tasks` — the caller's open board; replaces the signal. */
  load(): Observable<Task[]> {
    return this.http.get<Task[]>('/api/tasks').pipe(tap((tasks) => this._tasks.set(tasks)));
  }

  /**
   * Start refetching the board every `intervalMs` so a change another member makes appears here within
   * NFR-1's 5s (F-03 polling — the MVP transport ahead of a possible SignalR upgrade). A tick is skipped
   * while the tab is hidden (`document.hidden`, bounding idle server load) or while `paused` is set. A failed
   * poll is swallowed (`catchError`) so the stream survives to the next tick and never throws into the UI.
   * Idempotent: a second call tears down the prior subscription first.
   */
  startPolling(intervalMs: number): void {
    this.stopPolling();
    this.pollSub = interval(intervalMs)
      .pipe(
        switchMap(() =>
          document.hidden || this._paused() ? EMPTY : this.load().pipe(catchError(() => EMPTY)),
        ),
      )
      .subscribe();
  }

  /** Tear the poll subscription down (board destroy / logout) so no interval leaks after navigation. */
  stopPolling(): void {
    this.pollSub?.unsubscribe();
    this.pollSub = undefined;
  }

  /** Raise/clear the poll-suppression flag (board sets it during a drag and while the dialog is open). */
  setPaused(paused: boolean): void {
    this._paused.set(paused);
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

  /** `POST /api/tasks/{id}/unclaim` then refetch — an in-progress task returns to To do, unassigned (S-05). */
  unclaim(id: string): Observable<Task[]> {
    return this.http.post<Task>(`/api/tasks/${id}/unclaim`, {}).pipe(switchMap(() => this.load()));
  }

  /** `POST /api/tasks/{id}/sendback` then refetch — an admin returns a Done task to In progress (S-05). */
  sendBack(id: string, comment: string): Observable<Task[]> {
    return this.http
      .post<Task>(`/api/tasks/${id}/sendback`, { comment })
      .pipe(switchMap(() => this.load()));
  }

  /** `PUT /api/tasks/{id}` (edit, admin-only/any-column) then refetch so the card re-renders. */
  update(id: string, request: UpdateTaskRequest): Observable<Task[]> {
    return this.http.put<Task>(`/api/tasks/${id}`, request).pipe(switchMap(() => this.load()));
  }

  /** `DELETE /api/tasks/{id}` (To-do-only) then refetch so the card disappears. */
  delete(id: string): Observable<Task[]> {
    return this.http.delete<void>(`/api/tasks/${id}`).pipe(switchMap(() => this.load()));
  }

  /**
   * `PUT /api/tasks/order` then refetch — persist a column's new within-column order (FR-021) and
   * re-render from the server (last-write-wins; cross-member propagation is S-06 polling).
   */
  reorder(status: TaskStatus, orderedIds: string[]): Observable<Task[]> {
    return this.http
      .put<void>('/api/tasks/order', { status, orderedIds })
      .pipe(switchMap(() => this.load()));
  }

  /**
   * `GET /api/tasks/tags` — the household's distinct tag values (incl. closed-task tags), alphabetical, for
   * the create/edit chip-input autocomplete. Fetched fresh when a task dialog opens (no long-lived cache —
   * the set is small and a stale suggestion list is low-cost).
   */
  getTagSuggestions(): Observable<string[]> {
    return this.http.get<string[]>('/api/tasks/tags');
  }

  /** `GET /api/tasks/{id}/comments` — the task's full thread, lazy-loaded when the detail dialog opens. */
  getComments(id: string): Observable<Comment[]> {
    return this.http.get<Comment[]>(`/api/tasks/${id}/comments`);
  }

  /**
   * `POST /api/tasks/{id}/comments` — post a member comment. Returns the created comment; the dialog re-lists
   * the thread and the card's badge updates on the next board poll/refetch (no whole-board refetch needed).
   */
  addComment(id: string, body: string): Observable<Comment> {
    return this.http.post<Comment>(`/api/tasks/${id}/comments`, { body });
  }

  /** Resets the board state and halts polling. Wired into {@link AuthService.logout} alongside the household reset. */
  clearOnLogout(): void {
    this.stopPolling();
    this._paused.set(false);
    this._tasks.set([]);
  }
}
