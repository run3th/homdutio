import { Component, OnInit, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { switchMap } from 'rxjs';

import { Task, TaskService } from '../task.service';
import { TagInputComponent } from '../tag-input/tag-input.component';
import { tagColor } from '../tag-color';
import { mapValidationProblem } from '../../auth/validation-problem';
import { Member, MemberService } from '../../household/member.service';
import { FlashService } from '../../shared/flash/flash.service';

/**
 * The per-task detail panel (S-04/S-11), opened via `@angular/cdk/dialog`. One reusable component covers two
 * modes driven entirely by the server's affordance flags (the SPA stays authorization-dumb):
 *
 * - **Editable** (`task.canEdit` — only while the task is in "To do", FR-011): a **pure** reactive form over
 *   title/description/category — Cancel / Save, nothing else. Save calls {@link TaskService.update} (which
 *   refetches the board) and closes on success; a 400 maps via {@link mapValidationProblem} and the dialog
 *   stays open. Deletion (FR-012) lives on the card's ⋯ menu, not here, so destruction never sits beside Save.
 * - **Read-only** (a non-admin caller): the fields render as static text, no edit.
 *
 * The comment thread (S-05) lives in its own dialog ({@link CommentsComponent}), opened from the card's 💬
 * button — so this panel stays a pure, compact form. Closes on backdrop click / escape via CDK defaults.
 */
@Component({
  selector: 'app-task-detail',
  imports: [ReactiveFormsModule, DatePipe, TagInputComponent],
  templateUrl: './task-detail.component.html',
  styleUrl: './task-detail.component.scss',
})
export class TaskDetailComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly tasks = inject(TaskService);
  private readonly dialogRef = inject<DialogRef<void>>(DialogRef);
  private readonly members = inject(MemberService);
  private readonly flash = inject(FlashService);

  /** The task this panel describes, handed in by the opener via CDK `DIALOG_DATA`. */
  readonly task = inject<Task>(DIALOG_DATA);

  readonly tagColor = tagColor;

  readonly form = this.fb.nonNullable.group({
    title: [this.task.title, [Validators.required]],
    description: [this.task.description ?? ''],
    tags: [this.task.tags ?? []],
    // '' = "Anyone" (leave unassigned). Only shown/honoured while the task is assignable (admin + To-do).
    assigneeId: [''],
  });

  /** Mapped validation messages from a 400 (or a generic fallback). */
  readonly errors = signal<string[]>([]);
  readonly pending = signal(false);
  /** Household tag values for the chip-input autocomplete (editable mode only). */
  readonly suggestions = signal<string[]>([]);
  /** The household roster, loaded when the task is assignable (renders the admin-only picker). */
  readonly roster = signal<Member[]>([]);

  constructor() {
    // Read-only (non-admin) tasks present their fields as static text — the form is inert.
    if (!this.task.canEdit) {
      this.form.disable();
    }
  }

  ngOnInit(): void {
    if (this.task.canEdit) {
      this.tasks.getTagSuggestions().subscribe({ next: (tags) => this.suggestions.set(tags), error: () => {} });
    }
    // The picker only appears for an assignable (admin + To-do) task; skip the roster fetch otherwise.
    if (this.task.canAssign) {
      this.members.list().subscribe({ next: (list) => this.roster.set(list), error: () => {} });
    }
  }

  save(): void {
    if (!this.task.canEdit || this.form.invalid || this.pending()) {
      this.form.markAllAsTouched();
      return;
    }

    this.errors.set([]);
    this.pending.set(true);

    const { title, description, tags, assigneeId } = this.form.getRawValue();
    // Assign only on an assignable task where a member (not "Anyone") was picked.
    const assignee = this.task.canAssign && assigneeId ? assigneeId : undefined;

    const update$ = this.tasks.update(this.task.id, {
      title: title.trim(),
      description: description.trim() || undefined,
      tags,
    });
    // Persist the edits first, then start the task by assigning (To-do → In progress) when a member is chosen.
    const save$ = assignee
      ? update$.pipe(switchMap(() => this.tasks.assign(this.task.id, assignee)))
      : update$;

    save$.subscribe({
      next: () => {
        this.pending.set(false);
        this.notifyAssignment(assignee);
        this.dialogRef.close();
      },
      error: (error: HttpErrorResponse) => {
        this.pending.set(false);
        this.errors.set(
          error.status === 400
            ? mapValidationProblem(error)
            : ['Something went wrong. Please try again.'],
        );
      },
    });
  }

  /**
   * Assignment feedback (push-notifications): when the task was assigned to someone other than the current
   * user, flash the per-device reminder. Self-assignment fires a push toast instead — wired in Phase 4.
   */
  private notifyAssignment(assigneeId: string | undefined): void {
    if (!assigneeId) {
      return;
    }
    const assignee = this.roster().find((m) => m.userId === assigneeId);
    if (assignee && !assignee.isSelf) {
      this.flash.show(
        `${assignee.displayName} will be notified on any device where they've turned notifications on.`,
      );
    }
  }

  close(): void {
    this.dialogRef.close();
  }
}
