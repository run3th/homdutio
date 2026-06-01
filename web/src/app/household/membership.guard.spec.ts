import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { Observable, isObservable, of } from 'rxjs';

import { requireHousehold, requireNoHousehold } from './membership.guard';
import { Household, HouseholdService } from './household.service';

describe('membership guards', () => {
  let loaded: Household | null;
  const redirectTree = new UrlTree();

  beforeEach(() => {
    loaded = null;
    TestBed.configureTestingModule({
      providers: [
        { provide: HouseholdService, useValue: { loadMine: () => of(loaded) } },
        { provide: Router, useValue: { createUrlTree: () => redirectTree } },
      ],
    });
  });

  function run(guard: typeof requireHousehold): Promise<boolean | UrlTree> {
    const result = TestBed.runInInjectionContext(() =>
      guard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );
    const obs = result as Observable<boolean | UrlTree>;
    return isObservable(obs) ? new Promise((resolve) => obs.subscribe(resolve)) : Promise.resolve(result as boolean | UrlTree);
  }

  const household: Household = { id: 'h1', name: 'The Burrow', role: 'Admin' };

  describe('requireHousehold (/board)', () => {
    it('allows access when a household is present', async () => {
      loaded = household;
      expect(await run(requireHousehold)).toBe(true);
    });

    it('redirects to /create-household when there is none', async () => {
      loaded = null;
      const router = TestBed.inject(Router);
      const spy = vi.spyOn(router, 'createUrlTree');

      expect(await run(requireHousehold)).toBe(redirectTree);
      expect(spy).toHaveBeenCalledWith(['/create-household']);
    });
  });

  describe('requireNoHousehold (/create-household)', () => {
    it('allows access when there is no household', async () => {
      loaded = null;
      expect(await run(requireNoHousehold)).toBe(true);
    });

    it('redirects to /board when a household is present', async () => {
      loaded = household;
      const router = TestBed.inject(Router);
      const spy = vi.spyOn(router, 'createUrlTree');

      expect(await run(requireNoHousehold)).toBe(redirectTree);
      expect(spy).toHaveBeenCalledWith(['/board']);
    });
  });
});
