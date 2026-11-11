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
    path: 'admit',
    title: 'Admit patient · Hilom',
    canActivate: [permissionGuard('clinical.admissions.admit')],
    loadComponent: () => import('./admit-page').then((m) => m.AdmitPage),
  },
  {
    path: 'census',
    title: 'Ward census · Hilom',
    canActivate: [permissionGuard('facility.census.view')],
    loadComponent: () => import('./census-page').then((m) => m.CensusPage),
  },
  {
    path: 'encounters/:id',
    title: 'Encounter · Hilom',
    canActivate: [permissionGuard('clinical.encounters.view')],
    loadComponent: () => import('./encounter-page').then((m) => m.EncounterPage),
  },
];
