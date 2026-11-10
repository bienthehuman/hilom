import { ChangeDetectionStrategy, Component, computed, inject, resource } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { EmptyState } from '@hilom/ui/empty-state';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { StatusPill } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCalendarDays, lucideCalendarPlus, lucideCircleAlert } from '@ng-icons/lucide';

import { Api } from '../../api/api';
import { listMyAppointments } from '../../api/fn/scheduling/list-my-appointments';
import { type AppointmentResponse } from '../../api/models/appointment-response';
import { formatLongDay, formatTimeRange, hospitalDateOf } from '../../core/format/date-time';
import { toProblem } from '../../core/http/problem';
import { formatPatientName } from '../patients/patient-labels';

/** Upcoming visits of the patients linked to this account. Cancelling and history come with slice 2.7. */
@Component({
  selector: 'app-portal-appointments-page',
  imports: [
    RouterLink,
    NgIcon,
    EmptyState,
    StatusPill,
    HlmAlertImports,
    HlmButtonImports,
    HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideCalendarDays, lucideCalendarPlus, lucideCircleAlert })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="mb-6 text-2xl font-semibold tracking-tight">Appointments</h1>

    @if (loadError(); as message) {
      <div hlmAlert variant="destructive">
        <ng-icon name="lucideCircleAlert" aria-hidden="true" />
        <h2 hlmAlertTitle>Couldn't load appointments</h2>
        <p hlmAlertDescription>{{ message }}</p>
        <div hlmAlertAction>
          <button hlmBtn variant="outline" type="button" (click)="appointments.reload()">
            Try again
          </button>
        </div>
      </div>
    } @else if (!appointments.hasValue()) {
      <div hlmSkeleton class="h-24 w-full rounded-lg" aria-busy="true"></div>
    } @else if (appointments.value().length === 0) {
      <div class="rounded-lg border bg-card p-6">
        <ui-empty-state
          icon="lucideCalendarDays"
          title="No upcoming appointments"
          description="Book a visit for a patient linked to your account."
        >
          <a hlmBtn routerLink="/portal/appointments/book">
            <ng-icon name="lucideCalendarPlus" aria-hidden="true" data-icon="inline-start" />Book a
            visit
          </a>
        </ui-empty-state>
      </div>
    } @else {
      <ul class="flex flex-col gap-3" aria-label="Upcoming appointments">
        @for (a of appointments.value(); track a.id) {
          <li class="rounded-lg border bg-card p-4">
            <div class="flex flex-wrap items-center justify-between gap-2">
              <p class="font-medium" data-numeric>{{ day(a) }}, {{ time(a) }}</p>
              <ui-status-pill kind="appointment" [status]="a.status" />
            </div>
            <p>
              Dr. {{ a.physician.firstName }} {{ a.physician.lastName }} ·
              {{ a.physician.specialization }}
            </p>
            <p class="text-sm text-muted-foreground">For {{ patient(a) }}</p>
            @if (a.status === 'requested') {
              <p class="mt-2 text-sm">Waiting for the doctor to confirm.</p>
            }
          </li>
        }
      </ul>
      <a hlmBtn variant="outline" class="mt-6 w-full" routerLink="/portal/appointments/book">
        <ng-icon name="lucideCalendarPlus" aria-hidden="true" data-icon="inline-start" />Book
        another visit
      </a>
    }
  `,
})
export class PortalAppointmentsPage {
  private readonly api = inject(Api);

  protected readonly appointments = resource({
    loader: () => this.api.invoke(listMyAppointments),
  });
  protected readonly loadError = computed(() =>
    this.appointments.error()
      ? (toProblem(this.appointments.error()).detail ?? 'Please try again in a moment.')
      : null,
  );

  protected day(a: AppointmentResponse): string {
    return formatLongDay(hospitalDateOf(a.startsAt));
  }

  protected time(a: AppointmentResponse): string {
    return formatTimeRange(a.startsAt, a.endsAt);
  }

  protected patient(a: AppointmentResponse): string {
    return formatPatientName(a.patient);
  }
}
