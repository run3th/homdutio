import { Component, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { AuthService } from '../auth.service';
import { mapValidationProblem } from '../validation-problem';
import { passwordPolicyValidator, passwordRuleChecklist } from '../password-policy.validator';
import { AuthLogoComponent } from '../auth-logo.component';

/** Shown when the link is missing its token/email — and the backend's own generic token failure. */
const INVALID_LINK_MESSAGE = 'This password reset link is invalid or has expired.';

/**
 * Set-a-new-password screen (S-08). Reads `email` + `token` from the query string (the emailed link),
 * validates the new password with the shared policy, and POSTs to reset-password. On success it routes
 * to `/login` with a notice + prefilled email (reusing the register→login navigation-state pattern) so
 * the new credential is exercised immediately. A missing/blank token, an invalid/expired token, and a
 * weak password are all surfaced inline — never an account-existence signal. Mobile-first (≤400px).
 */
@Component({
  selector: 'app-reset-password',
  imports: [ReactiveFormsModule, RouterLink, AuthLogoComponent],
  templateUrl: './reset-password.component.html',
  styleUrl: './reset-password.component.scss',
})
export class ResetPasswordComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  private readonly email = this.route.snapshot.queryParamMap.get('email') ?? '';
  private readonly token = this.route.snapshot.queryParamMap.get('token') ?? '';

  readonly form = this.fb.nonNullable.group({
    password: ['', [Validators.required, passwordPolicyValidator]],
  });

  /** Mapped server-side messages (weak password, invalid/expired token) or the missing-link message. */
  readonly serverErrors = signal<string[]>([]);
  readonly pending = signal(false);
  /** Drives the password input's show/hide toggle (presentational only). */
  readonly showPassword = signal(false);

  /** Live password-rule checklist, recomputed per keystroke off the password control's value. */
  private readonly passwordValue = toSignal(this.form.controls.password.valueChanges, {
    initialValue: '',
  });
  readonly passwordRules = computed(() => passwordRuleChecklist(this.passwordValue()));

  togglePassword(): void {
    this.showPassword.update((shown) => !shown);
  }

  submit(): void {
    if (this.form.invalid || this.pending()) {
      this.form.markAllAsTouched();
      return;
    }

    // A link without a token/email can never succeed — fail with the generic message, no API call.
    if (!this.token || !this.email) {
      this.serverErrors.set([INVALID_LINK_MESSAGE]);
      return;
    }

    this.serverErrors.set([]);
    this.pending.set(true);

    const { password } = this.form.getRawValue();
    this.auth.resetPassword(this.email, this.token, password).subscribe({
      next: () => {
        this.pending.set(false);
        void this.router.navigate(['/login'], {
          state: { notice: 'Password updated — please log in.', email: this.email },
        });
      },
      error: (err: HttpErrorResponse) => {
        this.pending.set(false);
        // The backend returns the generic INVALID_LINK_MESSAGE for a bad/expired token and Identity
        // policy descriptions for a weak password — mapValidationProblem surfaces either.
        this.serverErrors.set(mapValidationProblem(err));
      },
    });
  }
}
