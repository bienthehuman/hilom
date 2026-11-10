import { type Routes } from '@angular/router';

import { permissionGuard } from '../../core/auth/auth.guards';

export const routes: Routes = [
  {
    path: 'encounters',
    title: 'Encounters · Hilom',
    canActivate: [permissionGuard('clinical.encounters.view')],
    loadComponent: () => import('./encounters-page').then((m) => m.EncountersPage),
  },
  {
    path: 'encounters/:id',
    title: 'Encounter · Hilom',
    canActivate: [permissionGuard('clinical.encounters.view')],
    loadComponent: () => import('./encounter-page').then((m) => m.EncounterPage),
  },
];
