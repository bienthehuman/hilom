import { ChangeDetectionStrategy, Component, computed, inject, resource } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { EmptyState } from '@hilom/ui/empty-state';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideInbox } from '@ng-icons/lucide';

import { Api } from '../../api/api';
import { listAppointments } from '../../api/fn/scheduling/list-appointments';
import { type AppointmentResponse } from '../../api/models/appointment-response';
import {
  formatDateTime,
  formatLongDay,
  formatTimeRange,
  hospitalDateOf,
  hospitalToday,
} from '../../core/format/date-time';
import { toProblem } from '../../core/http/problem';
import { formatPatientName } from '../patients/patient-labels';
import { AppointmentActions } from './appointment-actions';

/** How far ahead the inbox looks. Booking only offers two weeks, so this covers every open request. */
const DAYS = 31;

/** Requests waiting for a decision, soonest first. A physician sees their own; the front desk sees all. */
@Component({
  selector: 'app-requests-page',
  imports: [
    RouterLink,
    NgIcon,
    PageHeader,
    EmptyState,
    AppointmentActions,
    HlmAlertImports,
    HlmButtonImports,
    HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideCircleAlert, lucideInbox })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-4xl">
      <ui-page-header
        title="Appointment requests"
        description="Confirm or decline new bookings. Declining frees the time and tells the patient why."
      />

      @if (loadError(); as message) {
        <div hlmAlert variant="destructive">
          <ng-icon name="lucideCircleAlert" aria-hidden="true" />
          <h2 hlmAlertTitle>Couldn't load requests</h2>
          <p hlmAlertDescription>{{ message }}</p>
          <div hlmAlertAction>
            <button hlmBtn variant="outline" size="sm" type="button" (click)="requests.reload()">
              Try again
            </button>
          </div>
        </div>
      } @else if (!requests.hasValue()) {
        <div class="flex flex-col gap-2" aria-busy="true">
          <div hlmSkeleton class="h-24 w-full rounded-lg"></div>
          <div hlmSkeleton class="h-24 w-full rounded-lg"></div>
        </div>
      } @else if (requests.value().length === 0) {
        <div class="rounded-lg border bg-card">
          <ui-empty-state
            title="No requests waiting"
            description="New bookings appear here for you to confirm."
          />
        </div>
      } @else {
        <p class="mb-3 text-sm text-muted-foreground" aria-live="polite">
          {{ requests.value().length }} waiting
        </p>
        <ul class="flex flex-col gap-3" aria-label="Requests waiting">
          @for (a of requests.value(); track a.id) {
            <li class="rounded-lg border bg-card p-4">
              <div class="flex flex-wrap items-start justify-between gap-3">
                <div class="min-w-0">
                  <p class="font-medium" data-numeric>{{ when(a) }}</p>
                  <p>
                    <a
                      class="underline-offset-4 hover:underline"
                      [routerLink]="['/patients', a.patient.id]"
                      >{{ patientName(a) }}</a
                    >
                    <span class="text-sm text-muted-foreground" data-numeric>
                      · {{ a.patient.mrn }}</span
                    >
                  </p>
                  <p class="text-sm text-muted-foreground">
                    Dr. {{ a.physician.firstName }} {{ a.physician.lastName }}
                  </p>
                  <p class="mt-1">"{{ a.reason }}"</p>
                  <p class="mt-1 text-xs text-muted-foreground">
                    Booked by {{ a.bookedBy }},
                    <span data-numeric>{{ formatDateTime(a.bookedAt) }}</span>
                  </p>
                </div>
                <app-appointment-actions
                  [appointment]="a"
                  [label]="patientName(a) + ', ' + when(a)"
                  (changed)="requests.reload()"
                />
              </div>
            </li>
          }
        </ul>
      }
    </div>
  `,
})
export class RequestsPage {
  private readonly api = inject(Api);

  protected readonly requests = resource({
    loader: () =>
      this.api.invoke(listAppointments, { from: hospitalToday(), days: DAYS, status: 'requested' }),
  });
  protected readonly loadError = computed(() =>
    this.requests.error()
      ? (toProblem(this.requests.error()).detail ?? "Couldn't load requests.")
      : null,
  );

  protected readonly formatDateTime = formatDateTime;
  protected when(a: AppointmentResponse): string {
    return `${formatLongDay(hospitalDateOf(a.startsAt))}, ${formatTimeRange(a.startsAt, a.endsAt)}`;
  }
  protected patientName(a: AppointmentResponse): string {
    return formatPatientName(a.patient);
  }
}
