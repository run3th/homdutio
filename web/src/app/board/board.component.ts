import { Component, computed, inject, OnInit } from '@angular/core';
import { DatePipe } from '@angular/common';

import { HouseholdService } from '../household/household.service';
import { CreateTaskComponent } from './create-task/create-task.component';
import { Task, TaskService, TaskStatus } from './task.service';

/**
 * The household board (S-03). Renders the household identity (name + role badge, from S-02), the
 * create-task form, and three live columns fed by {@link TaskService}. Tasks load on init and are grouped
 * by status into the columns; each card shows its title, creator, claimer (if any), and creation timestamp
 * (FR-018). Closed tasks never come back from the API, so they simply don't appear. Action buttons land in
 * Phase 3.
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

  ngOnInit(): void {
    this.tasks.load().subscribe();
  }
}
