import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { FormControl } from '@angular/forms';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ActivatedRoute } from '@angular/router';

import { RegisterComponent } from './register.component';
import { passwordPolicyValidator } from '../password-policy.validator';
import { AuthService } from '../auth.service';

describe('passwordPolicyValidator', () => {
  it('passes a password meeting Identity rules', () => {
    expect(passwordPolicyValidator(new FormControl('Passw0rd!'))).toBeNull();
  });

  it('flags a password missing required character classes', () => {
    const result = passwordPolicyValidator(new FormControl('weak'));
    expect(result?.['passwordPolicy']).toBeTruthy();
  });
});

describe('RegisterComponent', () => {
  let register: ReturnType<typeof vi.fn>;
  let loginSpy: ReturnType<typeof vi.fn>;
  let navSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    register = vi.fn();
    loginSpy = vi.fn();
    TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { register, login: loginSpy } },
      ],
    });
    navSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  function create() {
    const fixture = TestBed.createComponent(RegisterComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('navigates to /login with notice + email and does not auto-login on 200', () => {
    register.mockReturnValue(of(undefined));
    const fixture = create();
    fixture.componentInstance.form.setValue({
      email: 'new@b.com',
      displayName: 'Molly',
      password: 'Passw0rd!',
    });

    fixture.componentInstance.submit();

    expect(register).toHaveBeenCalledWith('new@b.com', 'Passw0rd!', 'Molly');
    expect(loginSpy).not.toHaveBeenCalled();
    expect(navSpy).toHaveBeenCalledWith(['/login'], {
      queryParams: {},
      state: { notice: 'Account created — please log in.', email: 'new@b.com' },
    });
  });

  it('forwards an undefined display name when the field is left blank', () => {
    register.mockReturnValue(of(undefined));
    const fixture = create();
    fixture.componentInstance.form.setValue({
      email: 'new@b.com',
      displayName: '   ',
      password: 'Passw0rd!',
    });

    fixture.componentInstance.submit();

    expect(register).toHaveBeenCalledWith('new@b.com', 'Passw0rd!', undefined);
  });

  it('renders mapped server errors on a 400 without navigating', () => {
    register.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { DuplicateUserName: ['Email is already taken.'] } },
          }),
      ),
    );
    const fixture = create();
    fixture.componentInstance.form.setValue({
      email: 'dupe@b.com',
      displayName: '',
      password: 'Passw0rd!',
    });

    fixture.componentInstance.submit();

    expect(fixture.componentInstance.serverErrors()).toEqual(['Email is already taken.']);
    expect(navSpy).not.toHaveBeenCalled();
  });
});

describe('RegisterComponent with an invite returnUrl', () => {
  it('forwards the returnUrl as a query param onto /login on success', () => {
    const register = vi.fn().mockReturnValue(of(undefined));
    TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { register, login: vi.fn() } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: { get: () => '/join/abc123' } } },
        },
      ],
    });
    const navSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    const fixture = TestBed.createComponent(RegisterComponent);
    fixture.detectChanges();
    fixture.componentInstance.form.setValue({
      email: 'new@b.com',
      displayName: '',
      password: 'Passw0rd!',
    });

    fixture.componentInstance.submit();

    expect(navSpy).toHaveBeenCalledWith(['/login'], {
      queryParams: { returnUrl: '/join/abc123' },
      state: { notice: 'Account created — please log in.', email: 'new@b.com' },
    });
  });
});
