import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { AuthService } from '../auth.service';
import { AuthLogoComponent } from '../auth-logo.component';

/** State handed off from RegisterComponent via the Router navigation `state`. */
interface PostRegisterState {
  notice?: string;
  email?: string;
}

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink, AuthLogoComponent],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  /**
   * Where to go after a successful login. Defaults to `/board`, but an invite flow passes
   * `?returnUrl=/join/<token>` so the recipient returns to finish joining (S-06). Internal paths only —
   * an absolute/protocol-relative value is ignored to avoid an open redirect.
   */
  private readonly returnUrl = (() => {
    const candidate = this.route.snapshot.queryParamMap.get('returnUrl');
    return candidate && candidate.startsWith('/') && !candidate.startsWith('//')
      ? candidate
      : '/board';
  })();

  /** Success notice carried over from a just-completed registration. */
  readonly notice = signal<string | null>(null);
  /** Generic, field-less credential error (no leak of which field was wrong). */
  readonly error = signal<string | null>(null);
  readonly pending = signal(false);
  /** Drives the password input's show/hide toggle (presentational only). */
  readonly showPassword = signal(false);

  togglePassword(): void {
    this.showPassword.update((shown) => !shown);
  }

  constructor() {
    // Read once at construction (during the activating navigation); never lands in the URL.
    const state = (this.router.getCurrentNavigation()?.extras.state ??
      history.state) as PostRegisterState | null;
    if (state?.notice) {
      this.notice.set(state.notice);
    }
    if (state?.email) {
      this.form.controls.email.setValue(state.email);
    }
  }

  submit(): void {
    if (this.form.invalid || this.pending()) {
      this.form.markAllAsTouched();
      return;
    }

    this.error.set(null);
    this.notice.set(null);
    this.pending.set(true);

    const { email, password } = this.form.getRawValue();
    this.auth.login(email, password).subscribe({
      next: () => {
        this.pending.set(false);
        void this.router.navigateByUrl(this.returnUrl);
      },
      error: () => {
        this.pending.set(false);
        this.error.set('Invalid email or password.');
      },
    });
  }
}
