import { type Routes } from '@angular/router';

/** The patient portal's tabs (docs/DESIGN_SYSTEM.md §6: Appointments, Patients, Account). */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'patients' },
  {
    path: 'patients',
    title: 'Patients · Hilom',
    loadComponent: () => import('./portal-patients-page').then((m) => m.PortalPatientsPage),
  },
  {
    path: 'appointments',
    title: 'Appointments · Hilom',
    loadComponent: () => import('./portal-appointments-page').then((m) => m.PortalAppointmentsPage),
  },
  {
    path: 'account',
    title: 'Account · Hilom',
    loadComponent: () => import('./portal-account-page').then((m) => m.PortalAccountPage),
  },
];
