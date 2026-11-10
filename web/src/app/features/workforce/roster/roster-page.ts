import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  linkedSignal,
  resource,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmDropdownMenuImports } from '@hilom/ui/dropdown-menu';
import { EmptyState } from '@hilom/ui/empty-state';
import { HlmLabelImports } from '@hilom/ui/label';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSelectImports } from '@hilom/ui/select';
import { ShiftChip, formatShiftHours } from '@hilom/ui/shift-chip';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideCircleAlert, lucidePlus, lucideTrash2 } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../../api/api';
import { listShifts } from '../../../api/fn/workforce/list-shifts';
import { addRosterEntry } from '../../../api/fn/workforce/add-roster-entry';
import { changeRosterShift } from '../../../api/fn/workforce/change-roster-shift';
import { getWardRoster } from '../../../api/fn/workforce/get-ward-roster';
import { listRosterWards } from '../../../api/fn/workforce/list-roster-wards';
import { removeRosterEntry } from '../../../api/fn/workforce/remove-roster-entry';
import { type RosterEntryResponse } from '../../../api/models/roster-entry-response';
import { type RosterStaff } from '../../../api/models/roster-staff';
import { type ShiftResponse } from '../../../api/models/shift-response';
import { type WardRosterResponse } from '../../../api/models/ward-roster-response';
import { AuthService } from '../../../core/auth/auth.service';
import {
  addDays,
  formatLongDay,
  formatMonthDay,
  formatWeekday,
  hospitalToday,
  weekDays,
  weekStart,
} from '../../../core/format/date-time';
import { toProblem } from '../../../core/http/problem';
import { WeekNav } from './week-nav';

interface Cell {
  staff: RosterStaff;
  day: string;
  entry?: RosterEntryResponse;
  /** Assigned to the ward that day, so they can be rostered. */
  assigned: boolean;
}

/**
 * A ward's week: staff down the side, days across, one shift per cell. Managers click a cell to set or
 * change the shift. Days someone isn't assigned to the ward can't be rostered.
 */
@Component({
  selector: 'app-roster-page',
  imports: [
    NgIcon,
    PageHeader,
    EmptyState,
    ShiftChip,
    WeekNav,
    HlmAlertImports,
    HlmButtonImports,
    HlmDropdownMenuImports,
    HlmLabelImports,
    HlmSelectImports,
    HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideCheck, lucideCircleAlert, lucidePlus, lucideTrash2 })],
  templateUrl: './roster-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RosterPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);

  /** From the query string, so a week and ward can be bookmarked and shared. */
  readonly ward = input<string>();
  readonly week = input<string>();

  protected readonly today = hospitalToday();
  protected readonly canManage = computed(() => this.auth.can('workforce.roster.manage'));
  private readonly seesAllWards = computed(() =>
    this.auth.scopes('workforce.roster.view').includes('all'),
  );

  // ---- Ward and week ----
  protected readonly wards = resource({ loader: () => this.api.invoke(listRosterWards) });
  protected readonly wardOptions = computed(() =>
    this.wards.hasValue() ? this.wards.value() : [],
  );
  protected readonly wardLabel = (id: string) => {
    const ward = this.wardOptions().find((w) => w.id === id);
    return ward ? `${ward.code} · ${ward.name}` : '';
  };
  protected readonly noWardsMessage = computed(() =>
    this.seesAllWards()
      ? { title: 'No wards yet', description: 'Add wards under Facility → Wards first.' }
      : {
          title: "You're not on a ward today",
          description: 'The roster shows the wards you are assigned to. Ask your chief nurse.',
        },
  );

  /** The ward in the URL, or the first one the user can open. */
  protected readonly wardId = computed(() => {
    const options = this.wardOptions();
    const requested = this.ward();
    return options.find((w) => w.id === requested)?.id ?? options[0]?.id;
  });
  protected readonly monday = computed(() => weekStart(this.week() ?? this.today));
  protected readonly days = computed(() => weekDays(this.monday()));

  // ---- Roster ----
  protected readonly roster = resource({
    params: () => {
      const wardId = this.wardId();
      return wardId ? { wardId, from: this.monday() } : undefined;
    },
    loader: ({ params }) =>
      this.api.invoke(getWardRoster, {
        wardId: params.wardId,
        from: params.from,
        to: addDays(params.from, 6),
      }),
  });
  /** Last loaded roster, kept while the next one loads so the grid doesn't flash. */
  protected readonly loaded = linkedSignal<
    WardRosterResponse | undefined,
    WardRosterResponse | undefined
  >({
    source: () => (this.roster.hasValue() ? this.roster.value() : undefined),
    computation: (next, previous) => next ?? previous?.value,
  });

  protected readonly rows = computed(() => {
    const roster = this.loaded();
    if (!roster) {
      return [];
    }
    return roster.staff.map((staff) => ({
      staff,
      cells: this.days().map<Cell>((day) => ({
        staff,
        day,
        entry: roster.entries.find((e) => e.employeeId === staff.employee.id && e.dutyDate === day),
        assigned: staff.assignments.some(
          (a) => a.startsOn <= day && (a.lastDay === null || day <= a.lastDay),
        ),
      })),
    }));
  });

  protected readonly shifts = resource({
    loader: () => this.api.invoke(listShifts, { status: 'active', pageSize: 100 }),
  });
  protected readonly shiftOptions = computed(() =>
    this.shifts.hasValue() ? this.shifts.value().items : [],
  );

  protected readonly weekday = formatWeekday;
  protected readonly monthDay = formatMonthDay;
  protected readonly hours = (s: { startsAt: string; endsAt: string; crossesMidnight: boolean }) =>
    formatShiftHours(s.startsAt, s.endsAt, s.crossesMidnight);
  protected readonly name = (staff: RosterStaff) =>
    `${staff.employee.lastName}, ${staff.employee.firstName}`;

  /** What a cell says to screen readers, e.g. "Reyes, Ana, Monday, November 9: AM". */
  protected cellLabel(cell: Cell): string {
    const who = `${this.name(cell.staff)}, ${formatLongDay(cell.day)}`;
    if (cell.entry) {
      return `${who}: ${cell.entry.shift.code}${this.canManage() ? '. Change' : ''}`;
    }
    return cell.assigned
      ? `${who}: off${this.canManage() ? '. Add a shift' : ''}`
      : `${who}: not on this ward`;
  }

  constructor() {
    // Put the chosen ward and week in the URL so reloads and links keep them.
    effect(() => {
      const ward = this.wardId();
      const week = this.monday();
      if (ward && (ward !== this.ward() || week !== this.week())) {
        void this.router.navigate([], {
          queryParams: { ward, week },
          queryParamsHandling: 'merge',
          replaceUrl: true,
        });
      }
    });
  }

  protected selectWard(ward: string): void {
    void this.router.navigate([], { queryParams: { ward }, queryParamsHandling: 'merge' });
  }

  protected selectWeek(week: string): void {
    void this.router.navigate([], { queryParams: { week }, queryParamsHandling: 'merge' });
  }

  // ---- Changing a cell ----
  /** The cell whose menu is open. */
  protected readonly active = signal<Cell | null>(null);
  protected readonly busy = signal(false);

  protected async pick(shift: ShiftResponse): Promise<void> {
    const cell = this.active();
    const wardId = this.wardId();
    if (!cell || !wardId || this.busy() || cell.entry?.shift.id === shift.id) {
      return;
    }

    await this.run(
      () =>
        cell.entry
          ? this.api.invoke(changeRosterShift, {
              id: cell.entry.id,
              body: { shiftId: shift.id, version: cell.entry.version },
            })
          : this.api.invoke(addRosterEntry, {
              body: {
                employeeId: cell.staff.employee.id,
                wardId,
                shiftId: shift.id,
                dutyDate: cell.day,
              },
            }),
      `${this.name(cell.staff)} on ${shift.code}, ${formatLongDay(cell.day)}`,
    );
  }

  protected async remove(): Promise<void> {
    const cell = this.active();
    if (!cell?.entry || this.busy()) {
      return;
    }
    const entry = cell.entry;
    await this.run(
      () => this.api.invoke(removeRosterEntry, { id: entry.id }),
      `${this.name(cell.staff)} is off, ${formatLongDay(cell.day)}`,
    );
  }

  private async run(action: () => Promise<unknown>, done: string): Promise<void> {
    this.busy.set(true);
    try {
      await action();
      toast.success('Roster updated', { description: done });
    } catch (error) {
      toast.error("Couldn't update the roster", {
        description: toProblem(error).detail ?? 'Please try again.',
      });
    } finally {
      this.busy.set(false);
      this.roster.reload();
    }
  }
}
