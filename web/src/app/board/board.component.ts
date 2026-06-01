import { Component, computed, inject, OnInit } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';

import { HouseholdService } from '../household/household.service';
import { CreateTaskComponent } from './create-task/create-task.component';
import { Task, TaskService, TaskStatus } from './task.service';

/**
 * The household board (S-03). Renders the household identity (name + role badge, from S-02), the
 * create-task form, and three live columns fed by {@link TaskService}. Tasks load on init and are grouped
 * by status into the columns; each card shows its title, creator, claimer (if any), and creation timestamp
 * (FR-018), plus exactly the lifecycle actions the server permits (the affordance flags). Each action
 * refetches the board on success (a confirmed task drops off); a stale affordance that the server rejects
 * (403/409) also triggers a refetch so the board self-heals to the true state.
 */
@Component({
  selector: 'app-board',
  imports: [DatePipe, CreateTaskComponent],
  templateUrl: './board.component.html',
  styleUrl: './board.component.scss',
})
export class BoardComponent implements OnInit {
  private readonly households = inject(HouseholdService);
  private readonly tasks = inject(TaskService);

  readonly household = this.households.current;

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

  ngOnInit(): void {
    this.tasks.load().subscribe();
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
