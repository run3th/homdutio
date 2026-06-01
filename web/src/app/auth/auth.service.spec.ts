import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';

import { AuthService, LoginResponse } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [AuthService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('starts unauthenticated with a null token', () => {
    expect(service.isAuthenticated()).toBe(false);
    expect(service.token).toBeNull();
    expect(service.email()).toBeNull();
  });

  it('login stores the token, captures the email, and flips auth state', () => {
    const response: LoginResponse = {
      accessToken: 'jwt-123',
      expiresAtUtc: '2099-01-01T00:00:00Z',
    };

    service.login('user@example.com', 'Passw0rd!').subscribe();

    const req = httpMock.expectOne('/api/auth/login');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'user@example.com', password: 'Passw0rd!' });
    req.flush(response);

    expect(service.token).toBe('jwt-123');
    expect(service.isAuthenticated()).toBe(true);
    expect(service.email()).toBe('user@example.com');
  });

  it('logout clears the token, email, and auth state', () => {
    service.login('user@example.com', 'Passw0rd!').subscribe();
    httpMock.expectOne('/api/auth/login').flush({
      accessToken: 'jwt-123',
      expiresAtUtc: '2099-01-01T00:00:00Z',
    });

    service.logout();

    expect(service.token).toBeNull();
    expect(service.isAuthenticated()).toBe(false);
    expect(service.email()).toBeNull();
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
