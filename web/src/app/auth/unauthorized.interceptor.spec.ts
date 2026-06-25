import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';

import { unauthorizedInterceptor } from './unauthorized.interceptor';
import { AuthService } from './auth.service';

describe('unauthorizedInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let refresh: ReturnType<typeof vi.fn>;
  let logout: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    refresh = vi.fn();
    logout = vi.fn();
    navigate = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([unauthorizedInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { refresh, logout, token: 'new-jwt' } },
        { provide: Router, useValue: { navigate } },
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('refreshes once and replays the original request on a protected 401', () => {
    refresh.mockReturnValue(of(true));

    let succeeded = false;
    http.get('/api/households').subscribe({ next: () => (succeeded = true), error: () => {} });

    httpMock.expectOne('/api/households').flush(null, { status: 401, statusText: 'Unauthorized' });

    const retried = httpMock.expectOne('/api/households');
    expect(retried.request.headers.get('Authorization')).toBe('Bearer new-jwt');
    retried.flush({ ok: true });

    expect(refresh).toHaveBeenCalledOnce();
    expect(logout).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
    expect(succeeded).toBe(true);
  });

  it('logs out and redirects when the refresh fails', () => {
    refresh.mockReturnValue(of(false));

    http.get('/api/households').subscribe({ error: () => {} });

    httpMock.expectOne('/api/households').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(refresh).toHaveBeenCalledOnce();
    expect(logout).toHaveBeenCalledOnce();
    expect(navigate).toHaveBeenCalledWith(['/login']);
  });

  it('logs out after a second 401 on the replayed request (no refresh loop)', () => {
    refresh.mockReturnValue(of(true));

    http.get('/api/households').subscribe({ error: () => {} });

    httpMock.expectOne('/api/households').flush(null, { status: 401, statusText: 'Unauthorized' });
    // The replayed request 401s again — must not refresh a second time.
    httpMock.expectOne('/api/households').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(refresh).toHaveBeenCalledOnce();
    expect(logout).toHaveBeenCalledOnce();
    expect(navigate).toHaveBeenCalledWith(['/login']);
  });

  it('passes auth-endpoint 401s through without refreshing or redirecting', () => {
    for (const url of [
      '/api/auth/login',
      '/api/auth/register',
      '/api/auth/refresh',
      '/api/auth/logout',
      '/api/auth/forgot-password',
      '/api/auth/reset-password',
    ]) {
      http.post(url, {}).subscribe({ error: () => {} });
      httpMock.expectOne(url).flush(null, { status: 401, statusText: 'Unauthorized' });
    }

    expect(refresh).not.toHaveBeenCalled();
    expect(logout).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
  });
});
