import { isDevMode } from '@angular/core';
import { type Routes } from '@angular/router';

import { authGuard, guestGuard, permissionGuard, portalGuard } from './core/auth/auth.guards';

export const routes: Routes = [
  {
    path: 'login',
    title: 'Sign in · Hilom',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login-page').then((m) => m.LoginPage),
  },
  {
    // Public: where staff invite emails link to. The token is in the URL fragment.
    path: 'invite',
    title: 'Set up your account · Hilom',
    loadComponent: () =>
      import('./features/auth/accept-invite-page').then((m) => m.AcceptInvitePage),
  },
  {
    path: 'portal/login',
    title: 'Sign in · Hilom patient portal',
    canActivate: [guestGuard],
    data: { portal: true },
    loadComponent: () => import('./features/auth/login-page').then((m) => m.LoginPage),
  },
  {
    // Public: patients and families create their own portal account.
    path: 'portal/register',
    title: 'Create your account · Hilom patient portal',
    canActivate: [guestGuard],
    loadComponent: () =>
      import('./features/portal/portal-register-page').then((m) => m.PortalRegisterPage),
  },
  {
    // The patient portal: its own mobile-first shell, for portal accounts only.
    path: 'portal',
    canActivate: [portalGuard],
    loadComponent: () => import('./features/portal/portal-shell').then((m) => m.PortalShell),
    loadChildren: () => import('./features/portal/portal.routes').then((m) => m.routes),
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
        path: 'patients',
        loadChildren: () => import('./features/patients/patients.routes').then((m) => m.routes),
      },
      {
        path: 'scheduling/availability',
        title: 'Availability · Hilom',
        canActivate: [permissionGuard('scheduling.slots.manage')],
        loadComponent: () =>
          import('./features/scheduling/availability-page').then((m) => m.AvailabilityPage),
      },
      {
        path: 'scheduling/appointments',
        title: 'Appointments · Hilom',
        canActivate: [permissionGuard('scheduling.appointments.view')],
        loadComponent: () =>
          import('./features/scheduling/appointments-page').then((m) => m.AppointmentsPage),
      },
      {
        path: 'scheduling/requests',
        title: 'Appointment requests · Hilom',
        canActivate: [permissionGuard('scheduling.appointments.decide')],
        loadComponent: () =>
          import('./features/scheduling/requests-page').then((m) => m.RequestsPage),
      },
      {
        path: 'scheduling/book',
        title: 'Book appointment · Hilom',
        canActivate: [permissionGuard('scheduling.appointments.book')],
        loadComponent: () =>
          import('./features/scheduling/book-appointment-page').then((m) => m.BookAppointmentPage),
      },
      {
        path: 'clinical',
        loadChildren: () => import('./features/clinical/clinical.routes').then((m) => m.routes),
      },
      {
        path: 'link-requests',
        title: 'Portal link requests · Hilom',
        canActivate: [permissionGuard('patients.links.manage')],
        loadComponent: () =>
          import('./features/patients/link-requests-page').then((m) => m.LinkRequestsPage),
      },
      {
        path: 'facility',
        loadChildren: () => import('./features/facility/facility.routes').then((m) => m.routes),
      },
      {
        path: 'workforce',
        loadChildren: () => import('./features/workforce/workforce.routes').then((m) => m.routes),
      },
      {
        path: 'admin/users',
        title: 'Users · Hilom',
        canActivate: [permissionGuard('access.users.view')],
        loadComponent: () => import('./features/admin/users/users-page').then((m) => m.UsersPage),
      },
      {
        path: 'admin/roles',
        title: 'Roles · Hilom',
        canActivate: [permissionGuard('access.roles.view')],
        loadComponent: () => import('./features/admin/roles/roles-page').then((m) => m.RolesPage),
      },
      {
        path: 'admin/roles/:id',
        title: 'Role · Hilom',
        canActivate: [permissionGuard('access.roles.view')],
        loadComponent: () =>
          import('./features/admin/roles/role-editor-page').then((m) => m.RoleEditorPage),
      },
      {
        path: 'admin/audit',
        title: 'Audit log · Hilom',
        canActivate: [permissionGuard('access.audit.view')],
        loadComponent: () => import('./features/admin/audit/audit-page').then((m) => m.AuditPage),
      },
      {
        path: 'forbidden',
        title: 'No access · Hilom',
        loadComponent: () =>
          import('./features/errors/forbidden-page').then((m) => m.ForbiddenPage),
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
