import { type Routes } from '@angular/router';

import { permissionGuard } from '../../core/auth/auth.guards';
import { LOOKUP_KINDS, type LookupKindId, isLookupKindId } from './lookups/lookup-kinds';

export const routes: Routes = [
  {
    path: 'lists',
    canActivate: [permissionGuard('workforce.employees.manage')],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'departments' },
      {
        // Only known lists match; anything else falls through to the app's catch-all.
        path: ':kind',
        canMatch: [(_route, segments) => isLookupKindId(segments[0]?.path)],
        title: (route) =>
          `${LOOKUP_KINDS[route.paramMap.get('kind') as LookupKindId].plural} · Hilom`,
        loadComponent: () => import('./lookups/lookups-page').then((m) => m.LookupsPage),
      },
    ],
  },
];
