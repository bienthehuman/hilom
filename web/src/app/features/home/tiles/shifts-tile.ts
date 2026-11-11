import { ChangeDetectionStrategy, Component, computed, inject, resource } from '@angular/core';
import { DashboardTile } from '@hilom/ui/dashboard-tile';
import { EmptyState } from '@hilom/ui/empty-state';
import { ShiftChip, formatShiftHours } from '@hilom/ui/shift-chip';
import { provideIcons } from '@ng-icons/core';
import { lucideCalendarCheck } from '@ng-icons/lucide';

import { Api } from '../../../api/api';
import { getMySchedule } from '../../../api/fn/workforce/get-my-schedule';
import { type RosterEntryResponse } from '../../../api/models/roster-entry-response';
import {
  addDays,
  formatMonthDay,
  formatWeekday,
  hospitalToday,
} from '../../../core/format/date-time';
import { tileError } from './tile-error';

/** The signed-in person's own shifts over the next seven days. Hidden when the account has no employee record. */
@Component({
  selector: 'app-shifts-tile',
  imports: [DashboardTile, EmptyState, ShiftChip],
  providers: [provideIcons({ lucideCalendarCheck })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'contents' },
  template: `
    @if (show()) {
      <ui-dashboard-tile
        title="My shifts"
        description="Your next seven days."
        link="/workforce/my-schedule"
        linkLabel="Open my schedule"
        [loading]="schedule.isLoading() && !schedule.hasValue()"
        [error]="error()"
        (retry)="schedule.reload()"
      >
        @if (entries().length === 0) {
          <ui-empty-state
            icon="lucideCalendarCheck"
            title="No shifts this week"
            description="Shifts you are rostered on show here."
          />
        } @else {
          <ol class="divide-y" aria-label="Upcoming shifts">
            @for (entry of entries(); track entry.id) {
              <li class="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 py-2 text-sm">
                <span class="font-medium" [class.text-primary]="entry.dutyDate === today">
                  {{ day(entry.dutyDate) }}
                </span>
                <span class="flex flex-wrap items-center gap-2">
                  <ui-shift-chip
                    [code]="entry.shift.code"
                    [color]="entry.shift.color"
                    [name]="entry.shift.name"
                  />
                  <span data-numeric>{{ hours(entry) }}</span>
                  <span class="text-muted-foreground">{{ entry.ward.code }}</span>
                </span>
              </li>
            }
          </ol>
        }
      </ui-dashboard-tile>
    }
  `,
})
export class ShiftsTile {
  private readonly api = inject(Api);

  protected readonly today = hospitalToday();
  protected readonly schedule = resource({
    loader: () => this.api.invoke(getMySchedule, { from: this.today, to: addDays(this.today, 6) }),
  });

  protected readonly show = computed(
    () => !this.schedule.hasValue() || this.schedule.value().linked,
  );
  protected readonly error = computed(() => tileError(this.schedule.error()));
  protected readonly entries = computed(() =>
    (this.schedule.hasValue() ? this.schedule.value().entries : [])
      .filter((e) => e.status === 'scheduled')
      .sort((a, b) => a.dutyDate.localeCompare(b.dutyDate))
      .slice(0, 5),
  );

  protected day(date: string): string {
    return date === this.today ? 'Today' : `${formatWeekday(date)} ${formatMonthDay(date)}`;
  }

  protected hours(entry: RosterEntryResponse): string {
    return formatShiftHours(entry.shift.startsAt, entry.shift.endsAt, entry.shift.crossesMidnight);
  }
}
