import { Component, computed, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { Dialog } from '@angular/cdk/dialog';
import { CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { Observable } from 'rxjs';

import { NotifBannerComponent } from '../notifications/notif-banner/notif-banner.component';
import { TaskColumnComponent } from './task-column/task-column.component';
import { TaskDetailComponent } from './task-detail/task-detail.component';
import { CommentsComponent } from './comments/comments.component';
import { DeleteConfirmComponent } from './delete-confirm/delete-confirm.component';
import { SendBackComponent } from './send-back/send-back.component';
import { Task, TaskService, TaskStatus } from './task.service';

/**
 * The household board (S-03/S-11). Composes three {@link TaskColumnComponent}s fed by {@link TaskService}
 * (the household identity, invite action, and **+ Add task** CTA now live in the shell topbar). The board
 * keeps owning the orchestration: drag/drop reorder, polling pause/resume, and the lifecycle actions. Tasks
 * load on init and are grouped by status into the columns; each card shows its title, creator, claimer (if
 * any), and creation timestamp (FR-018), plus exactly the lifecycle actions the server permits (the
 * affordance flags). Editing opens the detail dialog (a pure form); deleting opens a small confirm dialog.
 * Each action refetches the board on success (a confirmed task drops off); a stale affordance that the
 * server rejects (403/409) also triggers a refetch so the board self-heals to the true state.
 */
@Component({
  selector: 'app-board',
  imports: [TaskColumnComponent, NotifBannerComponent],
  templateUrl: './board.component.html',
  styleUrl: './board.component.scss',
})
export class BoardComponent implements OnInit, OnDestroy {
  /** Poll cadence (F-03): comfortably under NFR-1's 5s once request latency is added. */
  private static readonly POLL_INTERVAL_MS = 4000;

  private readonly tasks = inject(TaskService);
  private readonly dialog = inject(Dialog);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  /** The three fixed board columns: label, the status they hold, and the mockup status accent (dot color). */
  readonly columns: readonly { label: string; status: TaskStatus; accent: string }[] = [
    { label: 'To do', status: 'ToDo', accent: '#b5852f' },
    { label: 'In progress', status: 'InProgress', accent: '#2f6b8f' },
    { label: 'Done', status: 'Done', accent: '#3a7d52' },
  ];

  /** Which column the mobile tab switcher shows (desktop renders all three side by side). */
  readonly mobileCol = signal<TaskStatus>('ToDo');

  setMobileCol(status: TaskStatus): void {
    this.mobileCol.set(status);
  }

  /** The caller's open tasks grouped by status, derived from the service signal. */
  private readonly grouped = computed(() => {
    const buckets: Record<TaskStatus, Task[]> = { ToDo: [], InProgress: [], Done: [] };
    for (const task of this.tasks.current()) {
      buckets[task.status].push(task);
    }
    return buckets;
  });

  /** Tasks for a given column's status. */
  tasksFor(status: TaskStatus): Task[] {
    return this.grouped()[status];
  }

  /** Claim a To-do task. */
  claim(task: Task): void {
    this.run(this.tasks.claim(task.id));
  }

  /** Mark an In-progress task (that the caller claimed) done. */
  markDone(task: Task): void {
    this.run(this.tasks.markDone(task.id));
  }

  /** Confirm a Done task (admin), closing it off the board. */
  confirm(task: Task): void {
    this.run(this.tasks.confirm(task.id));
  }

  /**
   * Unclaim an in-progress task (S-05, FR-022) from the card's ⋯ menu — the claimer frees their own, or an
   * admin frees an absent member's. A direct action (no dialog); {@link run} self-heals on a stale 403/409.
   */
  unclaim(task: Task): void {
    this.run(this.tasks.unclaim(task.id));
  }

  /**
   * Send a Done task back (S-05, FR-023) from the card's ⋯ menu (admin). Opens {@link SendBackComponent} to
   * collect the required reason; on a returned comment, calls {@link TaskService.sendBack} (which refetches).
   * Polling is paused while the dialog is open and resumed on close so a tick can't refetch mid-entry (F-03);
   * the not-admin / not-Done guards are server-side, so {@link run} self-heals if the affordance was stale.
   */
  sendBack(task: Task): void {
    this.tasks.setPaused(true);
    const ref = this.dialog.open<string>(SendBackComponent, { data: task });
    ref.closed.subscribe((comment) => {
      this.tasks.setPaused(false);
      if (comment) {
        this.run(this.tasks.sendBack(task.id, comment));
      }
    });
  }

  /**
   * Open the per-task detail panel (S-04). An explicit control distinct from the drag handle, so a drag
   * never opens the dialog. The dialog's own mutations refetch the board, so no extra wiring on close.
   * Polling is paused while the dialog is open and resumed on close, so a tick never refetches the board
   * out from under an in-progress edit (F-03).
   */
  openDetail(task: Task): void {
    this.tasks.setPaused(true);
    const ref = this.dialog.open(TaskDetailComponent, { data: task });
    ref.closed.subscribe(() => this.tasks.setPaused(false));
  }

  /**
   * Open the standalone comment thread (S-05) from the card's 💬 button — available to every member
   * regardless of edit rights. Polling is paused while the dialog is open and resumed on close so a tick
   * can't refetch mid-read/mid-post (F-03); the dialog re-lists its own thread, and the card's badge catches
   * up on the next board poll, so no wiring is needed on close.
   */
  openComments(task: Task): void {
    this.tasks.setPaused(true);
    const ref = this.dialog.open(CommentsComponent, { data: task });
    ref.closed.subscribe(() => this.tasks.setPaused(false));
  }

  /**
   * Delete a task (FR-012) from the card's ⋯ menu. Opens a small confirm dialog; on confirm, calls
   * {@link TaskService.delete} (which refetches so the card disappears). Polling is paused while the confirm
   * is open so a tick can't refetch underneath it. The To-do-only + 409 guards are server-side: a stale
   * affordance that the server rejects (403/409) refetches via {@link run} so the board self-heals.
   */
  requestDelete(task: Task): void {
    this.tasks.setPaused(true);
    const ref = this.dialog.open<boolean>(DeleteConfirmComponent, { data: task });
    ref.closed.subscribe((confirmed) => {
      this.tasks.setPaused(false);
      if (confirmed) {
        this.run(this.tasks.delete(task.id));
      }
    });
  }

  /** A drag started — pause polling so a tick can't refetch mid-reorder (F-03). */
  onDragStart(): void {
    this.tasks.setPaused(true);
  }

  /** A drag ended (dropped or cancelled) — resume polling. */
  onDragEnd(): void {
    this.tasks.setPaused(false);
  }

  /**
   * A card was dropped within its column (S-04, FR-021). Compute the new within-column order from the drag
   * indices and persist it; {@link TaskService.reorder} refetches so the board re-renders the shared order.
   * If the persist fails (e.g. a concurrent transition moved a card out of the column), a `load()` self-heals
   * back to the server's truth — mirroring the S-03 stale-affordance pattern. Lists are independent per
   * column, so a drop is always within one status — cross-column moves are the lifecycle, not a drag.
   */
  drop(status: TaskStatus, event: CdkDragDrop<Task[]>): void {
    if (event.previousIndex === event.currentIndex) {
      return;
    }

    const orderedIds = this.tasksFor(status).map((task) => task.id);
    moveItemInArray(orderedIds, event.previousIndex, event.currentIndex);

    this.tasks.reorder(status, orderedIds).subscribe({
      error: () => this.tasks.load().subscribe(),
    });
  }

  ngOnInit(): void {
    // Open the deep-linked task once the initial board load resolves (real-web-push): a push
    // notification click lands on `/board?task=<id>`.
    this.tasks.load().subscribe((tasks) => this.openDeepLinkedTask(tasks));
    // F-03: keep the board live for the other member's changes within NFR-1's 5s.
    this.tasks.startPolling(BoardComponent.POLL_INTERVAL_MS);
  }

  /**
   * Deep-link entry (real-web-push): a notification click opens the app at `/board?task=<id>`. If that task
   * is on the caller's freshly-loaded board, open its detail panel; then strip the `task` param (a replaced
   * history entry) so a later poll/refetch or a manual refresh can't reopen it. A task that isn't found —
   * already closed, or not the caller's — falls through silently to the plain board (no error).
   */
  private openDeepLinkedTask(tasks: Task[]): void {
    const taskId = this.route.snapshot.queryParamMap.get('task');
    if (!taskId) {
      return;
    }

    const task = tasks.find((t) => t.id === taskId);
    if (task) {
      this.openDetail(task);
    }

    void this.router.navigate([], {
      queryParams: { task: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  ngOnDestroy(): void {
    // Tear down the poll so no interval leaks after navigating away from the board.
    this.tasks.stopPolling();
  }

  /**
   * Runs a mutation (which refetches on success). If the server rejects a stale affordance (403/409 —
   * e.g. someone else acted first), refetch anyway so the board self-heals to the real state.
   */
  private run(action$: Observable<Task[]>): void {
    action$.subscribe({
      error: (error: HttpErrorResponse) => {
        if (error.status === 403 || error.status === 409) {
          this.tasks.load().subscribe();
        }
      },
    });
  }
}
