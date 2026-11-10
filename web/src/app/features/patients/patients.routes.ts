import { type Routes } from '@angular/router';

import { permissionGuard } from '../../core/auth/auth.guards';

export const routes: Routes = [
  {
    path: '',
    title: 'Patients · Hilom',
    canActivate: [permissionGuard('patients.view')],
    loadComponent: () => import('./patients-page').then((m) => m.PatientsPage),
  },
  {
    path: 'new',
    title: 'Register patient · Hilom',
    canActivate: [permissionGuard('patients.register')],
    loadComponent: () => import('./patient-form-page').then((m) => m.PatientFormPage),
  },
  {
    path: ':id',
    title: 'Patient · Hilom',
    canActivate: [permissionGuard('patients.view')],
    loadComponent: () => import('./patient-record-page').then((m) => m.PatientRecordPage),
  },
  {
    path: ':id/edit',
    title: 'Edit patient · Hilom',
    canActivate: [permissionGuard('patients.edit')],
    loadComponent: () => import('./patient-form-page').then((m) => m.PatientFormPage),
  },
];
