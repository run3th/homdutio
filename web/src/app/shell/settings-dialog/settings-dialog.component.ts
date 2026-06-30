import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { DialogRef } from '@angular/cdk/dialog';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';

import { AuthService } from '../../auth/auth.service';
import { ProfileService } from '../../profile/profile.service';
import { mapValidationProblem } from '../../auth/validation-problem';

/** Display-name length cap — mirrors the backend's `ProfileEndpoints.MaxDisplayNameLength`. */
export const MAX_DISPLAY_NAME_LENGTH = 60;

/**
 * Fails as `required` when the value is blank once trimmed — Angular's `Validators.required` accepts a
 * whitespace-only string, but the backend trims and rejects it, so guard it client-side too.
 */
function nonBlank(control: AbstractControl): ValidationErrors | null {
  return (control.value ?? '').trim().length > 0 ? null : { required: true };
}

/**
 * The Settings modal (S-09), opened from the avatar menu via `@angular/cdk/dialog`. Phase 2 edits the
 * display name only: a reactive `displayName` control prefilled from the current user, saved via
 * {@link ProfileService} (which updates the header/menu immediately and propagates to existing
 * cards/comments on the board's next refresh). Mirrors {@link InviteDialogComponent}'s `.modal` markup and
 * the reactive-form conventions (signals for `pending`/`errors`, `mapValidationProblem` for 400 bodies).
 * The profile-photo upload/remove section arrives in Phase 3.
 */
@Component({
  selector: 'app-settings-dialog',
  imports: [ReactiveFormsModule],
  templateUrl: './settings-dialog.component.html',
  styleUrl: './settings-dialog.component.scss',
})
export class SettingsDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly profile = inject(ProfileService);
  private readonly auth = inject(AuthService);
  private readonly dialogRef = inject<DialogRef<void>>(DialogRef);

  readonly maxLength = MAX_DISPLAY_NAME_LENGTH;

  readonly form = this.fb.nonNullable.group({
    displayName: ['', [nonBlank, Validators.maxLength(MAX_DISPLAY_NAME_LENGTH)]],
  });

  /** Mapped validation messages from a 400 (or a generic fallback). */
  readonly errors = signal<string[]>([]);
  readonly pending = signal(false);

  constructor() {
    // Prefill with the current display name so the field opens populated, not blank.
    this.form.controls.displayName.setValue(this.auth.displayName() ?? '');
  }

  save(): void {
    if (this.form.invalid || this.pending()) {
      this.form.markAllAsTouched();
      return;
    }

    this.errors.set([]);
    this.pending.set(true);

    const displayName = this.form.getRawValue().displayName.trim();
    this.profile.updateProfile(displayName).subscribe({
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
