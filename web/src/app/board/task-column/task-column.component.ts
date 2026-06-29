import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CdkDragDrop, DragDropModule } from '@angular/cdk/drag-drop';

import { TaskCardComponent } from '../task-card/task-card.component';
import { Task, TaskStatus } from '../task.service';

/**
 * A white, soft-shadowed board column (S-11): its title, the CDK drop list of {@link TaskCardComponent}s,
 * and the empty-state. The column is the presentational container — it hosts the `cdkDropList` and passes
 * the drop event straight up to the board, which still owns the reorder/lifecycle/polling logic. Each card
 * is a `cdkDrag`; its handle (inside the card) is the only drag initiator. Card events are re-emitted up to
 * the board unchanged. One independent drop list per column (not connected), so a card can't be dragged
 * across columns — moving columns is the lifecycle (claim/done/confirm), not a drag.
 */
@Component({
  selector: 'app-task-column',
  imports: [DragDropModule, TaskCardComponent],
  templateUrl: './task-column.component.html',
  styleUrl: './task-column.component.scss',
})
export class TaskColumnComponent {
  @Input({ required: true }) label!: string;
  @Input({ required: true }) status!: TaskStatus;
  @Input({ required: true }) tasks!: Task[];
  /** The column's status accent (mockup): drives the header dot color. */
  @Input({ required: true }) accent!: string;

  /** A card was dropped within this column — the board computes + persists the new order. */
  @Output() readonly dropped = new EventEmitter<CdkDragDrop<Task[]>>();
  @Output() readonly dragStart = new EventEmitter<void>();
  @Output() readonly dragEnd = new EventEmitter<void>();
  @Output() readonly openDetail = new EventEmitter<Task>();
  @Output() readonly openComments = new EventEmitter<Task>();
  @Output() readonly claim = new EventEmitter<Task>();
  @Output() readonly markDone = new EventEmitter<Task>();
  @Output() readonly confirm = new EventEmitter<Task>();
  @Output() readonly edit = new EventEmitter<Task>();
  @Output() readonly delete = new EventEmitter<Task>();
  @Output() readonly unclaim = new EventEmitter<Task>();
  @Output() readonly sendBack = new EventEmitter<Task>();
}
