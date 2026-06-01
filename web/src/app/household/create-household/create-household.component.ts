import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';

import { HouseholdService } from '../household.service';
import { mapValidationProblem } from '../../auth/validation-problem';

/**
 * The no-household landing screen (S-02). A reactive form with one required `name`; on submit it
 * creates the household and routes to the board. Mirrors {@link LoginComponent}'s structure
 * (signals for `pending`/`errors`, `mapValidationProblem` for 400 bodies).
 */
@Component({
  selector: 'app-create-household',
  imports: [ReactiveFormsModule],
  templateUrl: './create-household.component.html',
  styleUrl: './create-household.component.scss',
})
export class CreateHouseholdComponent {
  private readonly fb = inject(FormBuilder);
  private readonly households = inject(HouseholdService);
  private readonly router = inject(Router);

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required]],
  });

  /** Mapped validation messages from a 400 (or a generic fallback). */
  readonly errors = signal<string[]>([]);
  readonly pending = signal(false);

  submit(): void {
    if (this.form.invalid || this.pending()) {
      this.form.markAllAsTouched();
      return;
    }

    this.errors.set([]);
    this.pending.set(true);

    const { name } = this.form.getRawValue();
    this.households.create(name).subscribe({
      next: () => {
        this.pending.set(false);
        void this.router.navigate(['/board']);
      },
      error: (error: HttpErrorResponse) => {
        this.pending.set(false);
        // Already in a household — the guard's invariant; just go to the board.
        if (error.status === 409) {
          void this.router.navigate(['/board']);
          return;
        }
        this.errors.set(
          error.status === 400
            ? mapValidationProblem(error)
            : ['Something went wrong. Please try again.'],
        );
      },
    });
  }
}
