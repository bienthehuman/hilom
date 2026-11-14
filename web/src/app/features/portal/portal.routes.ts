import { type Routes } from '@angular/router';

/**
 * The patient portal's tabs (docs/DESIGN_SYSTEM.md §6: Appointments, Patients, Account). It opens on
 * Appointments, the reason people come; with nothing linked yet, that page points to linking a patient.
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'appointments' },
  {
    path: 'patients',
    title: 'Patients · Hilom',
    loadComponent: () => import('./portal-patients-page').then((m) => m.PortalPatientsPage),
  },
  {
    path: 'patients/link',
    title: 'Link a patient · Hilom',
    loadComponent: () => import('./portal-link-request-page').then((m) => m.PortalLinkRequestPage),
  },
  {
    path: 'patients/:patientId/records',
    title: 'Records · Hilom',
    loadComponent: () => import('./portal-records-page').then((m) => m.PortalRecordsPage),
  },
  {
    path: 'appointments',
    title: 'Appointments · Hilom',
    loadComponent: () => import('./portal-appointments-page').then((m) => m.PortalAppointmentsPage),
  },
  {
    path: 'appointments/book',
    title: 'Book a visit · Hilom',
    loadComponent: () => import('./portal-book-page').then((m) => m.PortalBookPage),
  },
  {
    path: 'account',
    title: 'Account · Hilom',
    loadComponent: () => import('./portal-account-page').then((m) => m.PortalAccountPage),
  },
];
