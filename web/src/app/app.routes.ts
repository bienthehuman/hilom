import { isDevMode } from '@angular/core';
import { type Routes } from '@angular/router';

import { authGuard, guestGuard, permissionGuard } from './core/auth/auth.guards';

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
