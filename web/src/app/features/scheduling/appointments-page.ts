import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  resource,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { EmptyState } from '@hilom/ui/empty-state';
import { HlmInputImports } from '@hilom/ui/input';
import { HlmLabelImports } from '@hilom/ui/label';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { StatusPill } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCalendarDays,
  lucideChevronLeft,
  lucideChevronRight,
  lucideCircleAlert,
} from '@ng-icons/lucide';

import { Api } from '../../api/api';
import { listAppointments } from '../../api/fn/scheduling/list-appointments';
import { listBookablePhysicians } from '../../api/fn/scheduling/list-bookable-physicians';
import { type AppointmentResponse } from '../../api/models/appointment-response';
import { AuthService } from '../../core/auth/auth.service';
import {
  addDays,
  formatLongDay,
  formatTimeRange,
  hospitalToday,
} from '../../core/format/date-time';
import { toProblem } from '../../core/http/problem';
import { formatPatientName } from '../patients/patient-labels';
import { AppointmentActions } from './appointment-actions';

const ANY = 'any';

/**
 * A day's appointments. The front desk sees every physician (with a filter); a physician sees their own.
 * Actions follow permissions: confirm/decline, no-show, cancel.
 */
@Component({
  selector: 'app-appointments-page',
  imports: [
    RouterLink,
    NgIcon,
    PageHeader,
    EmptyState,
    StatusPill,
    AppointmentActions,
    HlmAlertImports,
    HlmButtonImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSelectImports,
    HlmSkeletonImports,
  ],
  providers: [
    provideIcons({ lucideCalendarDays, lucideChevronLeft, lucideChevronRight, lucideCircleAlert }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-5xl">
      <ui-page-header
        title="Appointments"
        [description]="
          seesEveryone()
            ? 'Everyone booked for the day, by time.'
            : 'Your appointments for the day, by time.'
        "
      />

      <div class="mb-4 flex flex-wrap items-end justify-between gap-3">
        <div class="flex flex-wrap items-center gap-2">
          <button
            hlmBtn
            variant="outline"
            size="icon"
            type="button"
            aria-label="Previous day"
            (click)="day.set(addDays(day(), -1))"
          >
            <ng-icon name="lucideChevronLeft" aria-hidden="true" />
          </button>
          <button
            hlmBtn
            variant="outline"
            size="icon"
            type="button"
            aria-label="Next day"
            (click)="day.set(addDays(day(), 1))"
          >
            <ng-icon name="lucideChevronRight" aria-hidden="true" />
          </button>
          <button
            hlmBtn
            variant="ghost"
            type="button"
            [disabled]="day() === today"
            (click)="day.set(today)"
          >
            Today
          </button>
          <label hlmLabel for="appointments-day" class="sr-only">Day</label>
          <input
            hlmInput
            id="appointments-day"
            type="date"
            class="w-40"
            [value]="day()"
            (change)="$any($event.target).value && day.set($any($event.target).value)"
          />
          <h2 class="ms-1 font-semibold" data-numeric aria-live="polite">
            {{ formatLongDay(day()) }}
          </h2>
        </div>
        @if (seesEveryone()) {
          <div class="flex w-64 flex-col gap-1.5">
            <label hlmLabel for="appointments-physician">Doctor</label>
            <hlm-select
              [value]="physician()"
              [itemToString]="physicianLabel"
              (valueChange)="physician.set($any($event))"
            >
              <hlm-select-trigger buttonId="appointments-physician" class="w-full">
                <hlm-select-value />
              </hlm-select-trigger>
              <hlm-select-content *hlmSelectPortal>
                <hlm-select-item value="any">All doctors</hlm-select-item>
                @for (d of physicianOptions(); track d.id) {
                  <hlm-select-item [value]="d.id"
                    >Dr. {{ d.firstName }} {{ d.lastName }}</hlm-select-item
                  >
                }
              </hlm-select-content>
            </hlm-select>
          </div>
        }
      </div>

      @if (loadError(); as message) {
        <div hlmAlert variant="destructive">
          <ng-icon name="lucideCircleAlert" aria-hidden="true" />
          <h2 hlmAlertTitle>Couldn't load appointments</h2>
          <p hlmAlertDescription>{{ message }}</p>
          <div hlmAlertAction>
            <button
              hlmBtn
              variant="outline"
              size="sm"
              type="button"
              (click)="appointments.reload()"
            >
              Try again
            </button>
          </div>
        </div>
      } @else if (!appointments.hasValue()) {
        <div class="flex flex-col gap-2" aria-busy="true">
          <div hlmSkeleton class="h-20 w-full rounded-lg"></div>
          <div hlmSkeleton class="h-20 w-full rounded-lg"></div>
        </div>
      } @else if (appointments.value().length === 0) {
        <div class="rounded-lg border bg-card">
          <ui-empty-state
            icon="lucideCalendarDays"
            title="No appointments"
            description="Nobody is booked for this day."
          />
        </div>
      } @else {
        <ul class="flex flex-col gap-2" [attr.aria-label]="'Appointments, ' + formatLongDay(day())">
          @for (a of appointments.value(); track a.id) {
            <li
              class="flex flex-wrap items-start justify-between gap-3 rounded-lg border bg-card p-4"
            >
              <div class="flex min-w-0 gap-4">
                <p class="w-28 shrink-0 font-medium" data-numeric>
                  {{ formatTimeRange(a.startsAt, a.endsAt) }}
                </p>
                <div class="min-w-0">
                  <p class="flex flex-wrap items-center gap-2">
                    <a
                      class="font-medium underline-offset-4 hover:underline"
                      [routerLink]="['/patients', a.patient.id]"
                      >{{ patientName(a) }}</a
                    >
                    <span class="text-xs text-muted-foreground" data-numeric>{{
                      a.patient.mrn
                    }}</span>
                    <ui-status-pill kind="appointment" [status]="a.status" />
                  </p>
                  @if (seesEveryone()) {
                    <p class="text-sm text-muted-foreground">
                      Dr. {{ a.physician.firstName }} {{ a.physician.lastName }} ·
                      {{ a.physician.specialization }}
                    </p>
                  }
                  <p class="text-sm">{{ a.reason }}</p>
                  @if (a.cancelReason) {
                    <p class="text-xs text-muted-foreground">
                      Cancelled by {{ a.cancelledBy }}: "{{ a.cancelReason }}"
                    </p>
                  } @else if (a.decisionNote) {
                    <p class="text-xs text-muted-foreground">
                      {{ a.status === 'declined' ? 'Declined' : 'Note' }} ({{ a.decidedBy }}): "{{
                        a.decisionNote
                      }}"
                    </p>
                  }
                </div>
              </div>
              <app-appointment-actions
                [appointment]="a"
                [label]="patientName(a) + ', ' + formatTimeRange(a.startsAt, a.endsAt)"
                (changed)="appointments.reload()"
              />
            </li>
          }
        </ul>
      }
    </div>
  `,
})
export class AppointmentsPage {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);

  protected readonly today = hospitalToday();
  protected readonly day = signal(this.today);
  protected readonly physician = signal<string>(ANY);

  /** Front desk (scope `all`) sees every physician; a physician only themselves. */
  protected readonly seesEveryone = computed(() =>
    this.auth.scopes('scheduling.appointments.view').includes('all'),
  );

  private readonly physicians = resource({
    params: () => (this.seesEveryone() ? true : undefined),
    loader: () => this.api.invoke(listBookablePhysicians, {}),
  });
  protected readonly physicianOptions = computed(() =>
    this.physicians.hasValue() ? this.physicians.value() : [],
  );
  protected readonly physicianLabel = (id: string) => {
    if (id === ANY) {
      return 'All doctors';
    }
    const d = this.physicianOptions().find((p) => p.id === id);
    return d ? `Dr. ${d.firstName} ${d.lastName}` : '';
  };

  protected readonly appointments = resource({
    params: () => ({ day: this.day(), physician: this.physician() }),
    loader: ({ params }) =>
      this.api.invoke(listAppointments, {
        from: params.day,
        days: 1,
        physicianId: params.physician === ANY ? undefined : params.physician,
      }),
  });
  protected readonly loadError = computed(() =>
    this.appointments.error()
      ? (toProblem(this.appointments.error()).detail ?? "Couldn't load appointments.")
      : null,
  );

  protected readonly addDays = addDays;
  protected readonly formatLongDay = formatLongDay;
  protected readonly formatTimeRange = formatTimeRange;
  protected patientName(a: AppointmentResponse): string {
    return formatPatientName(a.patient);
  }
}
