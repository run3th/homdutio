import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';

import { Comment, Task, TaskService } from '../task.service';

/**
 * The per-task comment thread (S-05), opened from the card's 💬 button via `@angular/cdk/dialog`. Split out
 * of the task-detail dialog so the edit form stays a pure, compact form: **every** member can read and post
 * here regardless of edit rights. The thread is lazy-loaded on open (`getComments`) so the board poll never
 * carries comment bodies; a post re-lists the thread (the card badge catches up on the next board poll).
 * Closes on backdrop click / escape via CDK defaults.
 */
@Component({
  selector: 'app-comments',
  imports: [ReactiveFormsModule, DatePipe],
  templateUrl: './comments.component.html',
  styleUrl: './comments.component.scss',
})
export class CommentsComponent {
  private readonly tasks = inject(TaskService);
  private readonly dialogRef = inject<DialogRef<void>>(DialogRef);

  /** The task whose thread this dialog shows, handed in by the opener via CDK `DIALOG_DATA`. */
  readonly task = inject<Task>(DIALOG_DATA);

  /** The lazily-loaded comment thread and whether the initial fetch has resolved (for the empty state). */
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

  close(): void {
    this.dialogRef.close();
  }
}
