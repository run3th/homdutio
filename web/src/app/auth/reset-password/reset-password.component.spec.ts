import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ResetPasswordComponent } from './reset-password.component';
import { AuthService } from '../auth.service';

describe('ResetPasswordComponent', () => {
  let resetPassword: ReturnType<typeof vi.fn>;
  let navSpy: ReturnType<typeof vi.spyOn>;
  /** Query params the ActivatedRoute stub reports; set per test before create(). */
  let params: Record<string, string | null>;

  beforeEach(() => {
    resetPassword = vi.fn();
    params = { email: 'user@b.com', token: 'enc-token-abc' };
    TestBed.configureTestingModule({
      imports: [ResetPasswordComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { resetPassword } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: { get: (k: string) => params[k] ?? null } } },
        },
      ],
    });
    navSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  function create() {
    const fixture = TestBed.createComponent(ResetPasswordComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('reads email+token from the query, posts them, and navigates to /login with notice + prefilled email on success', () => {
    resetPassword.mockReturnValue(of(undefined));
    const fixture = create();
    fixture.componentInstance.form.setValue({ password: 'N3w!Passw0rd' });

    fixture.componentInstance.submit();

    expect(resetPassword).toHaveBeenCalledWith('user@b.com', 'enc-token-abc', 'N3w!Passw0rd');
    expect(navSpy).toHaveBeenCalledWith(['/login'], {
      state: { notice: 'Password updated — please log in.', email: 'user@b.com' },
    });
  });

  it('maps a weak-password ValidationProblem to inline messages without navigating', () => {
    resetPassword.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { PasswordTooShort: ['Passwords must be at least 6 characters.'] } },
          }),
      ),
    );
    const fixture = create();
    fixture.componentInstance.form.setValue({ password: 'N3w!Passw0rd' });

    fixture.componentInstance.submit();

    expect(fixture.componentInstance.serverErrors()).toEqual([
      'Passwords must be at least 6 characters.',
    ]);
    expect(navSpy).not.toHaveBeenCalled();
  });

  it('shows the generic error for an invalid/expired token (backend ValidationProblem)', () => {
    resetPassword.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { token: ['This password reset link is invalid or has expired.'] } },
          }),
      ),
    );
    const fixture = create();
    fixture.componentInstance.form.setValue({ password: 'N3w!Passw0rd' });

    fixture.componentInstance.submit();

    expect(fixture.componentInstance.serverErrors()).toEqual([
      'This password reset link is invalid or has expired.',
    ]);
  });

  it('shows the generic error and makes no API call when the link has no token', () => {
    params = { email: 'user@b.com', token: null };
    const fixture = create();
    fixture.componentInstance.form.setValue({ password: 'N3w!Passw0rd' });

    fixture.componentInstance.submit();

    expect(resetPassword).not.toHaveBeenCalled();
    expect(fixture.componentInstance.serverErrors()).toEqual([
      'This password reset link is invalid or has expired.',
    ]);
  });
});
