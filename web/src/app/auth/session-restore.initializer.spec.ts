import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { AuthService, LoginResponse, REFRESH_TOKEN_KEY } from './auth.service';
import { restoreSession } from './session-restore.initializer';

describe('restoreSession initializer', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  const run = () => TestBed.runInInjectionContext(() => restoreSession());

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

  it('resolves immediately without a request when no refresh token is stored', async () => {
    await run();

    httpMock.expectNone('/api/auth/refresh');
    expect(service.isAuthenticated()).toBe(false);
  });

  it('restores the access token when the silent refresh succeeds', async () => {
    localStorage.setItem(REFRESH_TOKEN_KEY, 'refresh-abc');

    const pending = run();
    httpMock.expectOne('/api/auth/refresh').flush({
      accessToken: 'jwt-456',
      expiresAtUtc: '2099-01-01T00:00:00Z',
      refreshToken: 'refresh-def',
    } satisfies LoginResponse);
    await pending;

    // A successful refresh fires a fire-and-forget /me to restore the profile (display name + avatar).
    httpMock.expectOne('/api/auth/me').flush({
      sub: 'u1',
      email: 'molly@burrow.test',
      displayName: 'Molly',
      avatarUrl: null,
    });

    expect(service.isAuthenticated()).toBe(true);
    expect(service.token).toBe('jwt-456');
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBe('refresh-def');
  });

  it('resolves logged-out and clears storage when the silent refresh fails', async () => {
    localStorage.setItem(REFRESH_TOKEN_KEY, 'refresh-abc');

    const pending = run();
    httpMock
      .expectOne('/api/auth/refresh')
      .flush(null, { status: 401, statusText: 'Unauthorized' });
    await pending;

    expect(service.isAuthenticated()).toBe(false);
    expect(service.token).toBeNull();
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull();
  });
});
