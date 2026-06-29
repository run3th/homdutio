import { Component, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { AuthService } from '../auth.service';
import { mapValidationProblem } from '../validation-problem';
import { passwordPolicyValidator, passwordRuleChecklist } from '../password-policy.validator';
import { AuthLogoComponent } from '../auth-logo.component';

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink, AuthLogoComponent],
  templateUrl: './register.component.html',
  styleUrl: './register.component.scss',
})
export class RegisterComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  /**
   * An optional invite `returnUrl` (S-06) forwarded through to `/login` on success, so the recipient
   * returns to `/join/<token>` after authenticating. Internal paths only (no open redirect).
   */
  readonly returnUrl = (() => {
    const candidate = this.route.snapshot.queryParamMap.get('returnUrl');
    return candidate && candidate.startsWith('/') && !candidate.startsWith('//') ? candidate : null;
  })();

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    // Optional: the backend defaults DisplayName from the email local-part when blank (S-03).
    displayName: [''],
    password: ['', [Validators.required, passwordPolicyValidator]],
  });

  /** Mapped server-side validation messages (duplicate email, weak password, …). */
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

    this.serverErrors.set([]);
    this.pending.set(true);

    const { email, displayName, password } = this.form.getRawValue();
    this.auth.register(email, password, displayName.trim() || undefined).subscribe({
      next: () => {
        this.pending.set(false);
        // No auto-login: send the user to /login to authenticate explicitly, carrying a
        // success notice and the email to prefill (via navigation state, not the URL). An invite
        // returnUrl, when present, rides along as a query param so the token survives to login.
        void this.router.navigate(['/login'], {
          queryParams: this.returnUrl ? { returnUrl: this.returnUrl } : {},
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
