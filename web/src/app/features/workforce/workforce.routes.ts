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
    path: 'roster',
    title: 'Duty roster · Hilom',
    canActivate: [permissionGuard('workforce.roster.view')],
    loadComponent: () => import('./roster/roster-page').then((m) => m.RosterPage),
  },
  {
    path: 'my-schedule',
    title: 'My schedule · Hilom',
    canActivate: [permissionGuard('workforce.roster.view')],
    loadComponent: () => import('./roster/my-schedule-page').then((m) => m.MySchedulePage),
  },
  {
    path: 'shifts',
    title: 'Shifts · Hilom',
    canActivate: [permissionGuard('workforce.shifts.manage')],
    loadComponent: () => import('./shifts/shifts-page').then((m) => m.ShiftsPage),
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
