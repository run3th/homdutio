import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import { AuthService } from '../auth.service';
import { mapValidationProblem } from '../validation-problem';

/**
 * Mirrors Identity's default password policy (Program.cs): ≥6 chars and at least one
 * uppercase, lowercase, digit, and non-alphanumeric character. Keeps the client in sync
 * with the server so valid input never round-trips into a surprise 400.
 */
export function passwordPolicyValidator(control: AbstractControl): ValidationErrors | null {
  const value = (control.value as string) ?? '';
  if (value.length === 0) {
    return null; // `required` owns the empty case.
  }

  const unmet: string[] = [];
  if (value.length < 6) unmet.push('at least 6 characters');
  if (!/[A-Z]/.test(value)) unmet.push('an uppercase letter');
  if (!/[a-z]/.test(value)) unmet.push('a lowercase letter');
  if (!/[0-9]/.test(value)) unmet.push('a digit');
  if (!/[^a-zA-Z0-9]/.test(value)) unmet.push('a non-alphanumeric character');

  return unmet.length > 0 ? { passwordPolicy: unmet } : null;
}

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './register.component.html',
  styleUrl: './register.component.scss',
})
export class RegisterComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    // Optional: the backend defaults DisplayName from the email local-part when blank (S-03).
    displayName: [''],
    password: ['', [Validators.required, passwordPolicyValidator]],
  });

  /** Mapped server-side validation messages (duplicate email, weak password, …). */
  readonly serverErrors = signal<string[]>([]);
  readonly pending = signal(false);

  submit(): void {
    if (this.form.invalid || this.pending()) {
      this.form.markAllAsTouched();
      return;
    }

    this.serverErrors.set([]);
    this.pending.set(true);

    const { email, displayName, password } = this.form.getRawValue();
    this.auth.register(email, password, displayName.trim() || undefined).subscribe({
      next: () => {
        this.pending.set(false);
        // No auto-login: send the user to /login to authenticate explicitly, carrying a
        // success notice and the email to prefill (via navigation state, not the URL).
        void this.router.navigate(['/login'], {
          state: { notice: 'Account created — please log in.', email },
        });
      },
      error: (err: HttpErrorResponse) => {
        this.pending.set(false);
        this.serverErrors.set(mapValidationProblem(err));
      },
    });
  }
}
