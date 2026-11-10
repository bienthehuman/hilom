import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { HlmButtonImports } from '@hilom/ui/button';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronLeft, lucideChevronRight } from '@ng-icons/lucide';

import {
  addDays,
  formatDate,
  formatMonthDay,
  hospitalToday,
  weekStart,
} from '../../../core/format/date-time';

/** "Nov 9 – 15, 2026" with previous / this week / next. Weeks run Monday to Sunday. */
@Component({
  selector: 'app-week-nav',
  imports: [HlmButtonImports, NgIcon],
  providers: [provideIcons({ lucideChevronLeft, lucideChevronRight })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-wrap items-center gap-2' },
  template: `
    <button
      hlmBtn
      variant="outline"
      size="icon"
      type="button"
      aria-label="Previous week"
      (click)="weekChange.emit(shift(-7))"
    >
      <ng-icon name="lucideChevronLeft" aria-hidden="true" />
    </button>
    <button
      hlmBtn
      variant="outline"
      size="icon"
      type="button"
      aria-label="Next week"
      (click)="weekChange.emit(shift(7))"
    >
      <ng-icon name="lucideChevronRight" aria-hidden="true" />
    </button>
    <button
      hlmBtn
      variant="ghost"
      type="button"
      [disabled]="isThisWeek()"
      (click)="weekChange.emit(thisWeek())"
    >
      This week
    </button>
    <h2 class="ms-1 text-base font-semibold" data-numeric aria-live="polite">{{ label() }}</h2>
  `,
})
export class WeekNav {
  /** Monday of the shown week, `YYYY-MM-DD`. */
  readonly week = input.required<string>();
  readonly weekChange = output<string>();

  protected readonly thisWeek = () => weekStart(hospitalToday());
  protected readonly isThisWeek = computed(() => this.week() === this.thisWeek());
  protected readonly shift = (days: number) => addDays(this.week(), days);

  protected readonly label = computed(() => {
    const start = this.week();
    const end = addDays(start, 6);
    return start.slice(0, 7) === end.slice(0, 7)
      ? `${formatMonthDay(start)} – ${Number(end.slice(8))}, ${end.slice(0, 4)}`
      : `${formatMonthDay(start)} – ${formatDate(end)}`;
  });
}
