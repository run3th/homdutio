import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Router } from '@angular/router';

import { unauthorizedInterceptor } from './unauthorized.interceptor';
import { AuthService } from './auth.service';

describe('unauthorizedInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let logout: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    logout = vi.fn();
    navigate = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([unauthorizedInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { logout } },
        { provide: Router, useValue: { navigate } },
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('discards auth and redirects to /login on a protected-call 401', () => {
    http.get('/api/households').subscribe({ error: () => {} });

    httpMock
      .expectOne('/api/households')
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(logout).toHaveBeenCalledOnce();
    expect(navigate).toHaveBeenCalledWith(['/login']);
  });

  it('passes a /api/auth/login 401 through without logout or redirect', () => {
    http.post('/api/auth/login', {}).subscribe({ error: () => {} });

    httpMock
      .expectOne('/api/auth/login')
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(logout).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
  });

  it('passes a /api/auth/register 400 through without logout or redirect', () => {
    http.post('/api/auth/register', {}).subscribe({ error: () => {} });

    httpMock
      .expectOne('/api/auth/register')
      .flush(null, { status: 400, statusText: 'Bad Request' });

    expect(logout).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
  });
});
