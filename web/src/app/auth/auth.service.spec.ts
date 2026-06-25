import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { AuthService, LoginResponse, REFRESH_TOKEN_KEY } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  const loginResponse: LoginResponse = {
    accessToken: 'jwt-123',
    expiresAtUtc: '2099-01-01T00:00:00Z',
    refreshToken: 'refresh-abc',
  };

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [AuthService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('starts unauthenticated with a null token', () => {
    expect(service.isAuthenticated()).toBe(false);
    expect(service.token).toBeNull();
    expect(service.email()).toBeNull();
    expect(service.hasRefreshToken).toBe(false);
  });

  it('requestPasswordReset posts the email and touches no auth state', () => {
    service.requestPasswordReset('user@example.com').subscribe();

    const req = httpMock.expectOne('/api/auth/forgot-password');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'user@example.com' });
    req.flush(null);

    expect(service.isAuthenticated()).toBe(false);
  });

  it('resetPassword posts email+token+newPassword and touches no auth state', () => {
    service.resetPassword('user@example.com', 'enc-token', 'N3w!Passw0rd').subscribe();

    const req = httpMock.expectOne('/api/auth/reset-password');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      email: 'user@example.com',
      token: 'enc-token',
      newPassword: 'N3w!Passw0rd',
    });
    req.flush(null);

    expect(service.isAuthenticated()).toBe(false);
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull();
  });

  it('login stores the token, captures the email, persists the refresh token, and flips auth state', () => {
    service.login('user@example.com', 'Passw0rd!').subscribe();

    const req = httpMock.expectOne('/api/auth/login');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'user@example.com', password: 'Passw0rd!' });
    req.flush(loginResponse);

    expect(service.token).toBe('jwt-123');
    expect(service.isAuthenticated()).toBe(true);
    expect(service.email()).toBe('user@example.com');
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBe('refresh-abc');
  });

  it('refresh posts the stored token, updates the access token, and rotates the stored token', () => {
    localStorage.setItem(REFRESH_TOKEN_KEY, 'refresh-abc');

    let result: boolean | undefined;
    service.refresh().subscribe((ok) => (result = ok));

    const req = httpMock.expectOne('/api/auth/refresh');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ refreshToken: 'refresh-abc' });
    req.flush({
      accessToken: 'jwt-456',
      expiresAtUtc: '2099-01-01T00:00:00Z',
      refreshToken: 'refresh-def',
    } satisfies LoginResponse);

    expect(result).toBe(true);
    expect(service.token).toBe('jwt-456');
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBe('refresh-def');
  });

  it('refresh recovers the email from the access token claim so identity survives a reload', () => {
    localStorage.setItem(REFRESH_TOKEN_KEY, 'refresh-abc');
    // A real (unsigned) JWT with an `email` claim — header.payload.signature.
    const payload = btoa(JSON.stringify({ sub: 'u1', email: 'reload@example.com' }));
    const jwt = `header.${payload}.sig`;

    service.refresh().subscribe();

    httpMock.expectOne('/api/auth/refresh').flush({
      accessToken: jwt,
      expiresAtUtc: '2099-01-01T00:00:00Z',
      refreshToken: 'refresh-def',
    } satisfies LoginResponse);

    expect(service.email()).toBe('reload@example.com');
  });

  it('refresh with no stored token resolves false without a request', () => {
    let result: boolean | undefined;
    service.refresh().subscribe((ok) => (result = ok));

    httpMock.expectNone('/api/auth/refresh');
    expect(result).toBe(false);
  });

  it('refresh on a 401 clears the token and stored credentials and resolves false', () => {
    localStorage.setItem(REFRESH_TOKEN_KEY, 'refresh-abc');

    let result: boolean | undefined;
    service.refresh().subscribe((ok) => (result = ok));

    httpMock
      .expectOne('/api/auth/refresh')
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(result).toBe(false);
    expect(service.token).toBeNull();
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull();
  });

  it('refresh shares a single in-flight request across concurrent callers', () => {
    localStorage.setItem(REFRESH_TOKEN_KEY, 'refresh-abc');

    const results: boolean[] = [];
    service.refresh().subscribe((ok) => results.push(ok));
    service.refresh().subscribe((ok) => results.push(ok));

    // Only one HTTP refresh despite two callers.
    httpMock.expectOne('/api/auth/refresh').flush({
      accessToken: 'jwt-456',
      expiresAtUtc: '2099-01-01T00:00:00Z',
      refreshToken: 'refresh-def',
    } satisfies LoginResponse);

    expect(results).toEqual([true, true]);
  });

  it('logout posts to the logout endpoint, clears auth state, and clears localStorage', () => {
    service.login('user@example.com', 'Passw0rd!').subscribe();
    httpMock.expectOne('/api/auth/login').flush(loginResponse);

    service.logout();

    const logoutReq = httpMock.expectOne('/api/auth/logout');
    expect(logoutReq.request.method).toBe('POST');
    expect(logoutReq.request.body).toEqual({ refreshToken: 'refresh-abc' });
    logoutReq.flush(null);

    expect(service.token).toBeNull();
    expect(service.isAuthenticated()).toBe(false);
    expect(service.email()).toBeNull();
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull();
  });

  it('logout without a stored refresh token clears state without a request', () => {
    service.logout();

    httpMock.expectNone('/api/auth/logout');
    expect(service.isAuthenticated()).toBe(false);
  });

  it('register posts to the register endpoint without storing a token', () => {
    service.register('new@example.com', 'Passw0rd!').subscribe();

    const req = httpMock.expectOne('/api/auth/register');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'new@example.com', password: 'Passw0rd!' });
    req.flush(null);

    expect(service.isAuthenticated()).toBe(false);
    expect(service.token).toBeNull();
  });
});
