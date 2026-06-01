import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree } from '@angular/router';

import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';

describe('authGuard', () => {
  let authenticated: boolean;
  const loginTree = new UrlTree();

  beforeEach(() => {
    authenticated = false;
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { isAuthenticated: () => authenticated } },
        { provide: Router, useValue: { createUrlTree: () => loginTree } },
      ],
    });
  });

  const run = () =>
    TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );

  it('returns true when authenticated', () => {
    authenticated = true;
    expect(run()).toBe(true);
  });

  it('returns a /login UrlTree when not authenticated', () => {
    authenticated = false;
    const router = TestBed.inject(Router);
    const spy = vi.spyOn(router, 'createUrlTree');

    expect(run()).toBe(loginTree);
    expect(spy).toHaveBeenCalledWith(['/login']);
  });
});
