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
    // Authenticated app shell (S-11): a persistent sidebar + topbar wrapping the household routes.
    // Guards live on this parent so they run once for the whole shell — never per child, never twice —
    // and the shell never renders for an unauthorized user. The auth/join pages stay top-level (no shell).
    path: '',
    canActivate: [authGuard, requireHousehold],
    loadComponent: () => import('./shell/shell.component').then((m) => m.ShellComponent),
    children: [
      {
        path: 'board',
        loadComponent: () => import('./board/board.component').then((m) => m.BoardComponent),
      },
    ],
  },
  {
    // Public invite landing (S-06): no household guard — reachable while logged out so a recipient can
    // preview, then register/log in and return here. JoinComponent reads auth + membership itself.
    path: 'join/:token',
    loadComponent: () => import('./join/join.component').then((m) => m.JoinComponent),
  },
  { path: '**', redirectTo: 'board' },
];
