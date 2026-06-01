import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';

import { bearerInterceptor } from './bearer.interceptor';
import { AuthService } from './auth.service';

describe('bearerInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let tokenValue: string | null;

  beforeEach(() => {
    tokenValue = null;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([bearerInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { get token() { return tokenValue; } } },
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('adds an Authorization header when a token is present', () => {
    tokenValue = 'jwt-123';

    http.get('/api/auth/me').subscribe();

    const req = httpMock.expectOne('/api/auth/me');
    expect(req.request.headers.get('Authorization')).toBe('Bearer jwt-123');
    req.flush({});
  });

  it('omits the Authorization header when there is no token', () => {
    tokenValue = null;

    http.get('/api/auth/me').subscribe();

    const req = httpMock.expectOne('/api/auth/me');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });
});
