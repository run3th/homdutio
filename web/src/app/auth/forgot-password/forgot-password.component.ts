import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { AuthService } from '../auth.service';
import { AuthLogoComponent } from '../auth-logo.component';

/**
 * Request-a-reset screen (S-08). A single email field that POSTs to forgot-password and then ALWAYS
 * shows the same generic confirmation — mirroring the backend's always-200 anti-enumeration posture,
 * so the UI never reveals whether an account exists (even on an error/429, which the interceptor
 * allowlist lets through). Mobile-first (≤400px), reusing the shared auth page/card styles.
 */
@Component({
  selector: 'app-forgot-password',
  imports: [ReactiveFormsModule, RouterLink, AuthLogoComponent],
  templateUrl: './forgot-password.component.html',
  styleUrl: './forgot-password.component.scss',
})
export class ForgotPasswordComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  /** Once true, the form is replaced by the generic confirmation. */
  readonly submitted = signal(false);
  /** The email the link was sent to, echoed in the confirmation (never reveals whether it exists). */
  readonly sentEmail = signal('');
  readonly pending = signal(false);

  submit(): void {
    if (this.form.invalid || this.pending()) {
      this.form.markAllAsTouched();
      return;
    }

    this.pending.set(true);
    const { email } = this.form.getRawValue();
    this.sentEmail.set(email);

    // Same outcome on success or failure: never leak account existence (or rate-limit state).
    this.auth.requestPasswordReset(email).subscribe({
      next: () => {
        this.pending.set(false);
        this.submitted.set(true);
      },
      error: () => {
        this.pending.set(false);
        this.submitted.set(true);
      },
    });
  }
}
