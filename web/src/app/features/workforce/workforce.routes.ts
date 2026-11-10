import { type Routes } from '@angular/router';

import { permissionGuard } from '../../core/auth/auth.guards';
import { LOOKUP_KINDS, type LookupKindId, isLookupKindId } from './lookups/lookup-kinds';

export const routes: Routes = [
  {
    path: 'employees',
    children: [
      {
        path: '',
        title: 'Employees · Hilom',
        canActivate: [permissionGuard('workforce.employees.view')],
        loadComponent: () => import('./employees/employees-page').then((m) => m.EmployeesPage),
      },
      {
        path: 'new',
        title: 'New employee · Hilom',
        canActivate: [permissionGuard('workforce.employees.manage')],
        loadComponent: () =>
          import('./employees/employee-form-page').then((m) => m.EmployeeFormPage),
      },
      {
        path: ':id',
        title: 'Employee · Hilom',
        canActivate: [permissionGuard('workforce.employees.view')],
        loadComponent: () =>
          import('./employees/employee-profile-page').then((m) => m.EmployeeProfilePage),
      },
      {
        path: ':id/edit',
        title: 'Edit employee · Hilom',
        canActivate: [permissionGuard('workforce.employees.manage')],
        loadComponent: () =>
          import('./employees/employee-form-page').then((m) => m.EmployeeFormPage),
      },
    ],
  },
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
