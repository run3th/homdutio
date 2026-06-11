import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';

import { Comment, Task, TaskService } from '../task.service';
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
 * Below the fields, **every** member sees the task's immutable comment thread (S-05) — author + timestamp,
 * with send-back reasons distinguished — and an add-comment input, regardless of edit rights. The thread is
 * lazy-loaded on open (`getComments`) so the board poll never carries comment bodies; a post re-lists the
 * thread (the card badge catches up on the next board poll). Closes on backdrop click / escape via CDK defaults.
 */
@Component({
  selector: 'app-task-detail',
  imports: [ReactiveFormsModule, DatePipe],
  templateUrl: './task-detail.component.html',
  styleUrl: './task-detail.component.scss',
})
export class TaskDetailComponent {
  private readonly fb = inject(FormBuilder);
  private readonly tasks = inject(TaskService);
  private readonly dialogRef = inject<DialogRef<void>>(DialogRef);

  /** The task this panel describes, handed in by the opener via CDK `DIALOG_DATA`. */
  readonly task = inject<Task>(DIALOG_DATA);

  readonly form = this.fb.nonNullable.group({
    title: [this.task.title, [Validators.required]],
    description: [this.task.description ?? ''],
    category: [this.task.category ?? ''],
  });

  /** Mapped validation messages from a 400 (or a generic fallback). */
  readonly errors = signal<string[]>([]);
  readonly pending = signal(false);

  /** The lazily-loaded comment thread (S-05) and whether the initial fetch has resolved (for the empty state). */
  readonly comments = signal<Comment[]>([]);
  readonly commentsLoaded = signal(false);

  /** The add-comment control — required + bounded to the server's 280-char limit. */
  readonly newComment = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(280)],
  });
  readonly postingComment = signal(false);
  readonly commentError = signal<string | null>(null);

  constructor() {
    // Read-only (non-admin) tasks present their fields as static text — the form is inert.
    if (!this.task.canEdit) {
      this.form.disable();
    }

    this.loadComments();
  }

  /** Lazy-load the thread on open; on failure leave it empty but mark loaded so the empty state shows. */
  private loadComments(): void {
    this.tasks.getComments(this.task.id).subscribe({
      next: (comments) => {
        this.comments.set(comments);
        this.commentsLoaded.set(true);
      },
      error: () => this.commentsLoaded.set(true),
    });
  }

  /**
   * Post a member comment, then re-list the thread (the card badge updates on the next board poll).
   *
   * Called from the composer's native `submit` (button/Enter) and the textarea's `keydown.enter`. We
   * `preventDefault` so the bare `<form>` — which has no `[formGroup]`, hence no Angular submit directive
   * to intercept it — never triggers a full-page navigation that would tear the dialog down. Shift+Enter
   * doesn't match `keydown.enter`, so it still inserts a newline for multi-line drafts.
   */
  postComment(event?: Event): void {
    event?.preventDefault();

    const body = this.newComment.value.trim();
    if (!body || this.newComment.invalid || this.postingComment()) {
      this.newComment.markAsTouched();
      return;
    }

    this.commentError.set(null);
    this.postingComment.set(true);
    this.tasks.addComment(this.task.id, body).subscribe({
      next: () => {
        this.postingComment.set(false);
        this.newComment.reset();
        this.loadComments();
      },
      error: (error: HttpErrorResponse) => {
        this.postingComment.set(false);
        this.commentError.set(
          error.status === 400
            ? 'A comment must be between 1 and 280 characters.'
            : 'Something went wrong. Please try again.',
        );
      },
    });
  }

  save(): void {
    if (!this.task.canEdit || this.form.invalid || this.pending()) {
      this.form.markAllAsTouched();
      return;
    }

    this.errors.set([]);
    this.pending.set(true);

    const { title, description, category } = this.form.getRawValue();
    this.tasks
      .update(this.task.id, {
        title: title.trim(),
        description: description.trim() || undefined,
        category: category.trim() || undefined,
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
