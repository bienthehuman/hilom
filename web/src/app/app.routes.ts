import { isDevMode } from '@angular/core';
import { type Routes } from '@angular/router';

import { authGuard, guestGuard } from './core/auth/auth.guards';

export const routes: Routes = [
  {
    path: 'login',
    title: 'Sign in · Hilom',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login-page').then((m) => m.LoginPage),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/app-shell').then((m) => m.AppShell),
    children: [
      {
        path: '',
        title: 'Home · Hilom',
        loadComponent: () => import('./features/home/home-page').then((m) => m.HomePage),
      },
      {
        // Living style guide (docs/DESIGN_SYSTEM.md §9). Development builds only for now;
        // the demo build flag arrives with deployment in phase 4.
        path: 'design',
        title: 'Design system · Hilom',
        canMatch: [() => isDevMode()],
        loadComponent: () => import('./features/design/design-page').then((m) => m.DesignPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
