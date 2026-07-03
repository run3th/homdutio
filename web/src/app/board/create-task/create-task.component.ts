import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DialogRef } from '@angular/cdk/dialog';

import { TaskService } from '../task.service';
import { TagInputComponent } from '../tag-input/tag-input.component';
import { AssigneePickerComponent } from '../assignee-picker/assignee-picker.component';
import { mapValidationProblem } from '../../auth/validation-problem';
import { Member, MemberService } from '../../household/member.service';
import { HouseholdService } from '../../household/household.service';
import { FlashService } from '../../shared/flash/flash.service';

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
  imports: [ReactiveFormsModule, TagInputComponent, AssigneePickerComponent],
  templateUrl: './create-task.component.html',
  styleUrl: './create-task.component.scss',
})
export class CreateTaskComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly tasks = inject(TaskService);
  private readonly dialogRef = inject<DialogRef<void>>(DialogRef);
  private readonly members = inject(MemberService);
  private readonly household = inject(HouseholdService);
  private readonly flash = inject(FlashService);

  readonly form = this.fb.nonNullable.group({
    title: ['', [Validators.required]],
    description: [''],
    tags: [[] as string[]],
    // '' = "Anyone" (unassigned). Only meaningful when the picker is shown (admin); ignored otherwise.
    assigneeId: [''],
  });

  /** Mapped validation messages from a 400 (or a generic fallback). */
  readonly errors = signal<string[]>([]);
  readonly pending = signal(false);
  /** Household tag values for the chip-input autocomplete, fetched when the dialog opens. */
  readonly suggestions = signal<string[]>([]);
  /** The household roster, loaded when the dialog opens (only used to render the admin-only picker). */
  readonly roster = signal<Member[]>([]);
  /** The picker is admin-only (assignment is admin-only, enforced server-side); non-admins never see it. */
  readonly isAdmin = computed(() => this.household.current()?.role === 'Admin');

  ngOnInit(): void {
    // A failed suggestion fetch is non-fatal — the field still works as free-text entry.
    this.tasks.getTagSuggestions().subscribe({ next: (tags) => this.suggestions.set(tags), error: () => {} });
    if (this.isAdmin()) {
      this.members.list().subscribe({ next: (list) => this.roster.set(list), error: () => {} });
    }
  }

  submit(): void {
    if (this.form.invalid || this.pending()) {
      this.form.markAllAsTouched();
      return;
    }

    this.errors.set([]);
    this.pending.set(true);

    const { title, description, tags, assigneeId } = this.form.getRawValue();
    // Only send an assignee when an admin actually picked a member (not "Anyone").
    const assignee = this.isAdmin() && assigneeId ? assigneeId : undefined;
    this.tasks
      .create({
        title: title.trim(),
        description: description.trim() || undefined,
        tags,
        assigneeId: assignee,
      })
      .subscribe({
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
   * Assignment feedback (real-web-push). Assigning to someone else flashes a per-device reminder; real
   * delivery to the assignee's registered devices happens server-side (Phase 3). Self-assignment shows no
   * client flash — the server push (Phase 3) reaches this user's own enabled devices instead.
   */
  private notifyAssignment(assigneeId: string | undefined): void {
    if (!assigneeId) {
      return;
    }
    const assignee = this.roster().find((m) => m.userId === assigneeId);
    if (!assignee || assignee.isSelf) {
      return;
    }
    this.flash.show(
      `${assignee.displayName} will be notified on any device where they've turned notifications on.`,
    );
  }

  close(): void {
    this.dialogRef.close();
  }
}
