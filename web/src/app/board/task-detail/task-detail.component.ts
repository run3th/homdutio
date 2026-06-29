import { Component, OnInit, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';

import { Task, TaskService } from '../task.service';
import { TagInputComponent } from '../tag-input/tag-input.component';
import { tagColor } from '../tag-color';
import { mapValidationProblem } from '../../auth/validation-problem';

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

  /** The task this panel describes, handed in by the opener via CDK `DIALOG_DATA`. */
  readonly task = inject<Task>(DIALOG_DATA);

  readonly tagColor = tagColor;

  readonly form = this.fb.nonNullable.group({
    title: [this.task.title, [Validators.required]],
    description: [this.task.description ?? ''],
    tags: [this.task.tags ?? []],
  });

  /** Mapped validation messages from a 400 (or a generic fallback). */
  readonly errors = signal<string[]>([]);
  readonly pending = signal(false);
  /** Household tag values for the chip-input autocomplete (editable mode only). */
  readonly suggestions = signal<string[]>([]);

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
  }

  save(): void {
    if (!this.task.canEdit || this.form.invalid || this.pending()) {
      this.form.markAllAsTouched();
      return;
    }

    this.errors.set([]);
    this.pending.set(true);

    const { title, description, tags } = this.form.getRawValue();
    this.tasks
      .update(this.task.id, {
        title: title.trim(),
        description: description.trim() || undefined,
        tags,
      })
      .subscribe({
        next: () => {
          this.pending.set(false);
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

  close(): void {
    this.dialogRef.close();
  }
}
