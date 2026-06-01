import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { LoginComponent } from './login.component';
import { AuthService } from '../auth.service';

describe('LoginComponent', () => {
  let login: ReturnType<typeof vi.fn>;
  let navSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    history.replaceState({}, '');
    login = vi.fn();
    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [provideRouter([]), { provide: AuthService, useValue: { login } }],
    });
    navSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  function create() {
    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('blocks submit and does not call login when the form is invalid', () => {
    const fixture = create();
    fixture.componentInstance.submit();
    expect(login).not.toHaveBeenCalled();
  });

  it('navigates to /board on a successful login', () => {
    login.mockReturnValue(of({ accessToken: 'jwt', expiresAtUtc: '2099-01-01T00:00:00Z' }));
    const fixture = create();
    fixture.componentInstance.form.setValue({ email: 'a@b.com', password: 'Passw0rd!' });

    fixture.componentInstance.submit();

    expect(login).toHaveBeenCalledWith('a@b.com', 'Passw0rd!');
    expect(navSpy).toHaveBeenCalledWith(['/board']);
  });

  it('shows a single generic message on 401 without navigating', () => {
    login.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 401 })));
    const fixture = create();
    fixture.componentInstance.form.setValue({ email: 'a@b.com', password: 'wrong' });

    fixture.componentInstance.submit();

    expect(fixture.componentInstance.error()).toBe('Invalid email or password.');
    expect(navSpy).not.toHaveBeenCalled();
  });

  it('shows the post-register notice and prefills the email from navigation state', () => {
    history.replaceState({ notice: 'Account created — please log in.', email: 'new@b.com' }, '');
    const fixture = create();

    expect(fixture.componentInstance.notice()).toBe('Account created — please log in.');
    expect(fixture.componentInstance.form.controls.email.value).toBe('new@b.com');
  });
});
