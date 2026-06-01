import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';

import { HouseholdService } from './household.service';

/**
 * The create-vs-board router. Both guards run after {@link authGuard} (chained on the routes) and
 * resolve asynchronously: unlike the synchronous auth check, household state is unknown right after
 * login, so the guard triggers {@link HouseholdService.loadMine} (cached after the first call) and
 * decides from the result. A `204`/empty membership is "no household", not an error.
 */

/** `/board`: a household is required; its absence redirects to `/create-household`. */
export const requireHousehold: CanActivateFn = () => {
  const households = inject(HouseholdService);
  const router = inject(Router);

  return households
    .loadMine()
    .pipe(map((household) => (household ? true : router.createUrlTree(['/create-household']))));
};

/** `/create-household`: the absence of a household is required; having one redirects to `/board`. */
export const requireNoHousehold: CanActivateFn = () => {
  const households = inject(HouseholdService);
  const router = inject(Router);

  return households
    .loadMine()
    .pipe(map((household) => (household ? router.createUrlTree(['/board']) : true)));
};
