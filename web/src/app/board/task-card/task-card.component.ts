import {
  Component,
  ElementRef,
  EventEmitter,
  HostListener,
  Input,
  Output,
  inject,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { DragDropModule } from '@angular/cdk/drag-drop';

import { Task } from '../task.service';

/**
 * A presentational, Claude-style pastel task card (S-11). Renders the title (an explicit open-detail
 * affordance kept distinct from the drag handle so a drag never opens the dialog), the optional category
 * chip + description, the metadata (Created by / Claimed by / Created), and exactly the **primary**
 * lifecycle action the server permits (Claim / Mark done / Confirm). Per-task management actions live in a
 * kebab (⋯) overflow menu beside the drag handle: Edit + Delete (gated by `canEdit`/`canDelete`, To-do
 * only) plus reserved S-05 loop-recovery slots (Unclaim / Send back — disabled, not wired yet). The card
 * is pure presentation: every action is emitted up to the board, which owns the service calls and refetch.
 * The card host carries `cdkDrag` (applied by {@link TaskColumnComponent}); the handle inside is the only
 * drag initiator. The menu is single-open and closes on outside-click / Escape (`aria-expanded`).
 */
@Component({
  selector: 'app-task-card',
  imports: [DatePipe, DragDropModule],
  templateUrl: './task-card.component.html',
  styleUrl: './task-card.component.scss',
  host: { class: 'task-card' },
})
export class TaskCardComponent {
  @Input({ required: true }) task!: Task;

  /** Open the detail dialog (S-04) — the title is the affordance, distinct from the drag handle. */
  @Output() readonly openDetail = new EventEmitter<Task>();
  @Output() readonly claim = new EventEmitter<Task>();
  @Output() readonly markDone = new EventEmitter<Task>();
  @Output() readonly confirm = new EventEmitter<Task>();
  /** Edit opens the (now delete-free) detail form; Delete asks the board to confirm + delete. */
  @Output() readonly edit = new EventEmitter<Task>();
  @Output() readonly delete = new EventEmitter<Task>();

  private readonly host = inject(ElementRef<HTMLElement>);

  /** Whether the per-task ⋯ overflow menu is open. */
  readonly menuOpen = signal(false);

  toggleMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  onEdit(): void {
    this.menuOpen.set(false);
    this.edit.emit(this.task);
  }

  onDelete(): void {
    this.menuOpen.set(false);
    this.delete.emit(this.task);
  }

  /** Close the menu on a click anywhere outside this card (the kebab's own click toggles first). */
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (this.menuOpen() && !this.host.nativeElement.contains(event.target as Node)) {
      this.menuOpen.set(false);
    }
  }

  /** Close the menu on Escape so keyboard users aren't trapped in it. */
  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.menuOpen()) {
      this.menuOpen.set(false);
    }
  }
}
