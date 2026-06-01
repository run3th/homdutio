import { Routes } from '@angular/router';

import { authGuard } from './auth/auth.guard';
import { requireHousehold, requireNoHousehold } from './household/membership.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'board' },
  {
    path: 'login',
    loadComponent: () => import('./auth/login/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'register',
    loadComponent: () =>
      import('./auth/register/register.component').then((m) => m.RegisterComponent),
  },
  {
    path: 'create-household',
    canActivate: [authGuard, requireNoHousehold],
    loadComponent: () =>
      import('./household/create-household/create-household.component').then(
        (m) => m.CreateHouseholdComponent,
      ),
  },
  {
    path: 'board',
    canActivate: [authGuard, requireHousehold],
    loadComponent: () => import('./board/board.component').then((m) => m.BoardComponent),
  },
  { path: '**', redirectTo: 'board' },
];
