import { type Routes } from '@angular/router';

import { permissionGuard } from '../../core/auth/auth.guards';

export const routes: Routes = [
  {
    path: 'wards',
    canActivate: [permissionGuard('facility.wards.view')],
    children: [
      {
        path: '',
        title: 'Wards · Hilom',
        loadComponent: () => import('./wards/wards-page').then((m) => m.WardsPage),
      },
      {
        path: ':id',
        title: 'Ward · Hilom',
        loadComponent: () => import('./wards/ward-page').then((m) => m.WardPage),
      },
    ],
  },
];
