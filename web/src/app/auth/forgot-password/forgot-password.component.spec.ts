import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ForgotPasswordComponent } from './forgot-password.component';
import { AuthService } from '../auth.service';

describe('ForgotPasswordComponent', () => {
  let requestPasswordReset: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    requestPasswordReset = vi.fn();
    TestBed.configureTestingModule({
      imports: [ForgotPasswordComponent],
      providers: [provideRouter([]), { provide: AuthService, useValue: { requestPasswordReset } }],
    });
  });

  function create() {
    const fixture = TestBed.createComponent(ForgotPasswordComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('does not submit an invalid (empty) email', () => {
    const fixture = create();
    fixture.componentInstance.submit();
    expect(requestPasswordReset).not.toHaveBeenCalled();
    expect(fixture.componentInstance.submitted()).toBe(false);
  });

  it('posts the email and shows the generic confirmation on success', () => {
    requestPasswordReset.mockReturnValue(of(undefined));
    const fixture = create();
    fixture.componentInstance.form.setValue({ email: 'user@b.com' });

    fixture.componentInstance.submit();

    expect(requestPasswordReset).toHaveBeenCalledWith('user@b.com');
    expect(fixture.componentInstance.submitted()).toBe(true);
  });

  it('shows the same generic confirmation even on an error (no enumeration)', () => {
    requestPasswordReset.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 429 })),
    );
    const fixture = create();
    fixture.componentInstance.form.setValue({ email: 'user@b.com' });

    fixture.componentInstance.submit();

    expect(fixture.componentInstance.submitted()).toBe(true);
  });
});
