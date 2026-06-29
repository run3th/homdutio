import { Component, OnInit, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DialogRef } from '@angular/cdk/dialog';

import { TaskService } from '../task.service';
import { TagInputComponent } from '../tag-input/tag-input.component';
import { mapValidationProblem } from '../../auth/validation-problem';

/**
 * The create-task dialog (S-03/S-11, FR-010), opened from the topbar's **+ Add task** CTA via
 * `@angular/cdk/dialog` — the same dialog language as editing. A reactive form with a required `title` and
 * optional `description`/`category`; on submit it calls {@link TaskService.create}, which posts the task and
 * then refetches the board so the new card appears in "To do", and the dialog **closes** on success (rather
 * than resetting inline). Mirrors {@link CreateHouseholdComponent}'s structure (signals for
 * `pending`/`errors`, `mapValidationProblem` for 400 bodies). Available to any member — admin or adult
 * member both create per FR-010. Closes on backdrop click / escape via CDK defaults.
 */
@Component({
  selector: 'app-create-task',
  imports: [ReactiveFormsModule, TagInputComponent],
  templateUrl: './create-task.component.html',
  styleUrl: './create-task.component.scss',
})
export class CreateTaskComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly tasks = inject(TaskService);
  private readonly dialogRef = inject<DialogRef<void>>(DialogRef);

  readonly form = this.fb.nonNullable.group({
    title: ['', [Validators.required]],
    description: [''],
    tags: [[] as string[]],
  });

  /** Mapped validation messages from a 400 (or a generic fallback). */
  readonly errors = signal<string[]>([]);
  readonly pending = signal(false);
  /** Household tag values for the chip-input autocomplete, fetched when the dialog opens. */
  readonly suggestions = signal<string[]>([]);

  ngOnInit(): void {
    // A failed suggestion fetch is non-fatal — the field still works as free-text entry.
    this.tasks.getTagSuggestions().subscribe({ next: (tags) => this.suggestions.set(tags), error: () => {} });
  }

  submit(): void {
    if (this.form.invalid || this.pending()) {
      this.form.markAllAsTouched();
      return;
    }

    this.errors.set([]);
    this.pending.set(true);

    const { title, description, tags } = this.form.getRawValue();
    this.tasks
      .create({
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
