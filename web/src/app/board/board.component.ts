import { Component, computed, inject, OnDestroy, OnInit } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Dialog } from '@angular/cdk/dialog';
import { CdkDragDrop, DragDropModule, moveItemInArray } from '@angular/cdk/drag-drop';
import { Observable } from 'rxjs';

import { CreateTaskComponent } from './create-task/create-task.component';
import { TaskDetailComponent } from './task-detail/task-detail.component';
import { Task, TaskService, TaskStatus } from './task.service';

/**
 * The household board (S-03). Renders the create-task form and three live columns fed by
 * {@link TaskService} (the household identity + invite action now live in the shell topbar, S-11).
 * Tasks load on init and are grouped
 * by status into the columns; each card shows its title, creator, claimer (if any), and creation timestamp
 * (FR-018), plus exactly the lifecycle actions the server permits (the affordance flags). Each action
 * refetches the board on success (a confirmed task drops off); a stale affordance that the server rejects
 * (403/409) also triggers a refetch so the board self-heals to the true state.
 */
@Component({
  selector: 'app-board',
  imports: [DatePipe, CreateTaskComponent, DragDropModule],
  templateUrl: './board.component.html',
  styleUrl: './board.component.scss',
})
export class BoardComponent implements OnInit, OnDestroy {
  /** Poll cadence (F-03): comfortably under NFR-1's 5s once request latency is added. */
  private static readonly POLL_INTERVAL_MS = 4000;

  private readonly tasks = inject(TaskService);
  private readonly dialog = inject(Dialog);

  /** The three fixed board columns: display label + the status they hold (English labels; no i18n in v1). */
  readonly columns: readonly { label: string; status: TaskStatus }[] = [
    { label: 'To do', status: 'ToDo' },
    { label: 'In progress', status: 'InProgress' },
    { label: 'Done', status: 'Done' },
  ];

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
    this.tasks.load().subscribe();
    // F-03: keep the board live for the other member's changes within NFR-1's 5s.
    this.tasks.startPolling(BoardComponent.POLL_INTERVAL_MS);
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
