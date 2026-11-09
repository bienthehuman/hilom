import { isDevMode } from '@angular/core';
import { type Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    title: 'Hilom',
    loadComponent: () =>
      import('./features/foundation/foundation-page').then((m) => m.FoundationPage),
  },
  {
    // Living style guide (docs/DESIGN_SYSTEM.md §9). Development builds only for now;
    // the demo build flag arrives with deployment in phase 4.
    path: 'design',
    title: 'Design system · Hilom',
    canMatch: [() => isDevMode()],
    loadComponent: () => import('./features/design/design-page').then((m) => m.DesignPage),
  },
  { path: '**', redirectTo: '' },
];
