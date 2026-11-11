import { ChangeDetectionStrategy, Component, computed, inject, resource } from '@angular/core';
import { DashboardTile, Stat } from '@hilom/ui/dashboard-tile';
import { EmptyState } from '@hilom/ui/empty-state';
import { StatusPill } from '@hilom/ui/status';
import { provideIcons } from '@ng-icons/core';
import { lucideCalendarDays } from '@ng-icons/lucide';

import { Api } from '../../../api/api';
import { listAppointments } from '../../../api/fn/scheduling/list-appointments';
import { AuthService } from '../../../core/auth/auth.service';
import { formatTimeRange, hospitalToday } from '../../../core/format/date-time';
import { formatPatientName } from '../../patients/patient-labels';
import { tileError } from './tile-error';

/**
 * Today's appointments. The API narrows the list to the user's scope: a physician gets their own
 * ("Today's appointments"), the front desk everyone's ("Day list").
 */
@Component({
  selector: 'app-appointments-tile',
  imports: [DashboardTile, Stat, EmptyState, StatusPill],
  providers: [provideIcons({ lucideCalendarDays })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'contents' },
  template: `
    <ui-dashboard-tile
      [title]="title()"
      [description]="everyone() ? 'Every physician, today.' : 'Your appointments, today.'"
      link="/scheduling/appointments"
      linkLabel="Open appointments"
      [loading]="appointments.isLoading() && !appointments.hasValue()"
      [error]="error()"
      (retry)="appointments.reload()"
    >
      @if (active().length === 0) {
        <ui-empty-state
          icon="lucideCalendarDays"
          title="No appointments today"
          description="Booked and confirmed appointments for today show here."
        />
      } @else {
        <div class="flex flex-wrap gap-x-8 gap-y-3">
          <ui-stat [value]="active().length" label="Appointments today" />
          @if (requested() > 0) {
            <ui-stat [value]="requested()" label="Awaiting a decision" tone="warning" />
          }
        </div>
        <ol class="divide-y" aria-label="Today's appointments">
          @for (a of active().slice(0, 6); track a.id) {
            <li class="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 py-2 text-sm">
              <span class="min-w-0">
                <span class="font-medium" data-numeric>{{ time(a.startsAt, a.endsAt) }}</span>
                {{ name(a.patient) }}
                @if (everyone()) {
                  <span class="text-muted-foreground"> · Dr. {{ a.physician.lastName }} </span>
                }
              </span>
              <ui-status-pill kind="appointment" [status]="a.status" />
            </li>
          }
        </ol>
        @if (active().length > 6) {
          <p class="text-sm text-muted-foreground">And {{ active().length - 6 }} more.</p>
        }
      }
    </ui-dashboard-tile>
  `,
})
export class AppointmentsTile {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);

  protected readonly appointments = resource({
    loader: () => this.api.invoke(listAppointments, { from: hospitalToday(), days: 1 }),
  });

  protected readonly everyone = computed(() =>
    this.auth.scopes('scheduling.appointments.view').includes('all'),
  );
  protected readonly title = computed(() =>
    this.everyone() ? 'Day list' : "Today's appointments",
  );
  protected readonly error = computed(() => tileError(this.appointments.error()));
  /** Declined and cancelled ones no longer need anyone's attention. */
  protected readonly active = computed(() =>
    (this.appointments.hasValue() ? this.appointments.value() : [])
      .filter((a) => a.status !== 'declined' && a.status !== 'cancelled')
      .sort((a, b) => a.startsAt.localeCompare(b.startsAt)),
  );
  protected readonly requested = computed(
    () => this.active().filter((a) => a.status === 'requested').length,
  );

  protected readonly time = formatTimeRange;
  protected readonly name = formatPatientName;
}
