import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  resource,
} from '@angular/core';
import { Router } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { EmptyState } from '@hilom/ui/empty-state';
import { PageHeader } from '@hilom/ui/page-header';
import { ShiftChip, formatShiftHours } from '@hilom/ui/shift-chip';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert } from '@ng-icons/lucide';

import { Api } from '../../../api/api';
import { getMySchedule } from '../../../api/fn/workforce/get-my-schedule';
import { type RosterEntryResponse } from '../../../api/models/roster-entry-response';
import {
  addDays,
  formatLongDay,
  hospitalToday,
  weekDays,
  weekStart,
} from '../../../core/format/date-time';
import { WeekNav } from './week-nav';

/** The signed-in person's own shifts, a week at a time, on whichever ward they're rostered. */
@Component({
  selector: 'app-my-schedule-page',
  imports: [
    NgIcon,
    PageHeader,
    EmptyState,
    ShiftChip,
    WeekNav,
    HlmAlertImports,
    HlmButtonImports,
    HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideCircleAlert })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-3xl">
      <ui-page-header title="My schedule" description="Your shifts, a week at a time." />

      <app-week-nav class="mb-4" [week]="monday()" (weekChange)="selectWeek($event)" />

      @if (schedule.error()) {
        <div hlmAlert variant="destructive">
          <ng-icon name="lucideCircleAlert" aria-hidden="true" />
          <h2 hlmAlertTitle>Couldn't load your schedule</h2>
          <p hlmAlertDescription>Please try again.</p>
          <div hlmAlertAction>
            <button hlmBtn variant="outline" size="sm" type="button" (click)="schedule.reload()">
              Try again
            </button>
          </div>
        </div>
      } @else if (!schedule.hasValue()) {
        <div hlmSkeleton class="h-96 w-full rounded-lg" aria-busy="true"></div>
      } @else if (!schedule.value().linked) {
        <ui-empty-state
          icon="lucideInbox"
          title="No schedule for this account"
          description="Your sign-in isn't linked to an employee record, so there are no shifts to show."
        />
      } @else {
        <ol class="divide-y rounded-lg border bg-card" aria-label="Days this week">
          @for (day of days(); track day) {
            <li
              class="flex flex-wrap items-center justify-between gap-3 px-4 py-3"
              [class.bg-accent]="day === today"
              [attr.aria-current]="day === today ? 'date' : null"
            >
              <span class="font-medium" [class.text-primary]="day === today">
                {{ longDay(day) }}{{ day === today ? ' (today)' : '' }}
              </span>
              @if (entryOn(day); as entry) {
                <span class="flex flex-wrap items-center gap-2 text-sm">
                  <ui-shift-chip
                    [code]="entry.shift.code"
                    [color]="entry.shift.color"
                    [name]="entry.shift.name"
                  />
                  <span data-numeric>{{ hours(entry) }}</span>
                  <span class="text-muted-foreground"
                    >{{ entry.ward.code }} · {{ entry.ward.name }}</span
                  >
                </span>
              } @else {
                <span class="text-sm text-muted-foreground">Off</span>
              }
            </li>
          }
        </ol>
      }
    </div>
  `,
})
export class MySchedulePage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  readonly week = input<string>();

  protected readonly today = hospitalToday();
  protected readonly monday = computed(() => weekStart(this.week() ?? this.today));
  protected readonly days = computed(() => weekDays(this.monday()));

  protected readonly schedule = resource({
    params: () => this.monday(),
    loader: ({ params }) =>
      this.api.invoke(getMySchedule, { from: params, to: addDays(params, 6) }),
  });

  protected readonly longDay = formatLongDay;
  protected readonly hours = (entry: RosterEntryResponse) =>
    formatShiftHours(entry.shift.startsAt, entry.shift.endsAt, entry.shift.crossesMidnight);

  protected entryOn(day: string): RosterEntryResponse | undefined {
    return this.schedule.hasValue()
      ? this.schedule.value().entries.find((e) => e.dutyDate === day && e.status === 'scheduled')
      : undefined;
  }

  protected selectWeek(week: string): void {
    void this.router.navigate([], { queryParams: { week }, queryParamsHandling: 'merge' });
  }
}
