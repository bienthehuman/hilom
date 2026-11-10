import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  linkedSignal,
  resource,
  signal,
  untracked,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCheckboxImports } from '@hilom/ui/checkbox';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { EmptyState } from '@hilom/ui/empty-state';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { HlmLabelImports } from '@hilom/ui/label';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCalendarPlus, lucideCalendarX, lucideCircleAlert, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { closeSlot } from '../../api/fn/scheduling/close-slot';
import { listSlotPhysicians } from '../../api/fn/scheduling/list-slot-physicians';
import { listSlots } from '../../api/fn/scheduling/list-slots';
import { publishSlots } from '../../api/fn/scheduling/publish-slots';
import { type DayOfWeek } from '../../api/models/day-of-week';
import { type SlotPhysicianResponse } from '../../api/models/slot-physician-response';
import { type SlotResponse } from '../../api/models/slot-response';
import {
  addDays,
  formatLongDay,
  formatTimeRange,
  hospitalDateOf,
  hospitalToday,
  weekDays,
  weekStart,
} from '../../core/format/date-time';
import { type Problem, toProblem } from '../../core/http/problem';
import { WeekNav } from '../workforce/roster/week-nav';

const WEEKDAYS: readonly { value: DayOfWeek; label: string }[] = [
  { value: 'monday', label: 'Mon' },
  { value: 'tuesday', label: 'Tue' },
  { value: 'wednesday', label: 'Wed' },
  { value: 'thursday', label: 'Thu' },
  { value: 'friday', label: 'Fri' },
  { value: 'saturday', label: 'Sat' },
  { value: 'sunday', label: 'Sun' },
];

/**
 * A physician's bookable time, a week at a time. Front desk manages every physician; physicians see
 * only themselves (the API decides).
 */
@Component({
  selector: 'app-availability-page',
  imports: [
    ReactiveFormsModule,
    NgIcon,
    PageHeader,
    EmptyState,
    FormField,
    WeekNav,
    HlmAlertImports,
    HlmButtonImports,
    HlmCheckboxImports,
    HlmDialogImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSelectImports,
    HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideCalendarPlus, lucideCalendarX, lucideCircleAlert, lucideX })],
  templateUrl: './availability-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AvailabilityPage {
  private readonly api = inject(Api);

  // ---- Physician ----
  protected readonly physicians = resource({ loader: () => this.api.invoke(listSlotPhysicians) });
  protected readonly physicianList = computed(() =>
    this.physicians.hasValue() ? this.physicians.value() : [],
  );
  /** The first physician until someone picks another. */
  protected readonly physicianId = linkedSignal(() => this.physicianList()[0]?.id ?? null);
  protected readonly physician = computed(() =>
    this.physicianList().find((p) => p.id === this.physicianId()),
  );
  protected readonly physicianLabel = (id: string) => {
    const p = this.physicianList().find((x) => x.id === id);
    return p ? physicianName(p) : '';
  };
  protected readonly physicianName = physicianName;

  // ---- Week ----
  private readonly today = hospitalToday();
  protected readonly week = signal(weekStart(this.today));
  protected readonly days = computed(() => weekDays(this.week()));

  protected readonly slots = resource({
    params: () => {
      const physicianId = this.physicianId();
      return physicianId ? { physicianId, week: this.week() } : undefined;
    },
    loader: ({ params }) =>
      this.api.invoke(listSlots, {
        physicianId: params.physicianId,
        from: params.week,
        to: addDays(params.week, 6),
      }),
  });

  /** Slots grouped by hospital date. */
  protected readonly byDay = computed(() => {
    const groups = new Map<string, SlotResponse[]>();
    for (const slot of this.slots.hasValue() ? this.slots.value() : []) {
      const day = hospitalDateOf(slot.startsAt);
      groups.set(day, [...(groups.get(day) ?? []), slot]);
    }
    return groups;
  });
  protected readonly openCount = computed(
    () =>
      (this.slots.hasValue() ? this.slots.value() : []).filter((s) => s.status === 'open').length,
  );

  protected readonly loadError = computed(() => {
    const error = this.physicians.error() ?? this.slots.error();
    return error ? (toProblem(error).detail ?? "Couldn't load availability.") : null;
  });

  protected readonly formatLongDay = formatLongDay;
  protected readonly formatTimeRange = formatTimeRange;
  protected isPast(slot: SlotResponse): boolean {
    return new Date(slot.startsAt).getTime() < Date.now();
  }

  // ---- Close ----
  protected readonly closing = signal<string | null>(null);

  protected async close(slot: SlotResponse): Promise<void> {
    if (this.closing()) {
      return;
    }
    this.closing.set(slot.id);
    try {
      await this.api.invoke(closeSlot, { id: slot.id });
      toast.success('Slot closed', {
        description: `${formatLongDay(hospitalDateOf(slot.startsAt))}, ${formatTimeRange(slot.startsAt, slot.endsAt)}`,
      });
      this.slots.reload();
    } catch (error) {
      toast.error("Couldn't close the slot", { description: toProblem(error).detail });
      this.slots.reload();
    } finally {
      this.closing.set(null);
    }
  }

  // ---- Publish ----
  protected readonly weekdays = WEEKDAYS;
  protected readonly publishOpen = signal(false);
  protected readonly publishing = signal(false);
  protected readonly publishProblem = signal<Problem | null>(null);
  protected readonly chosenDays = signal<ReadonlySet<DayOfWeek>>(new Set());
  protected readonly daysError = signal<string | null>(null);

  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    from: ['', Validators.required],
    to: ['', Validators.required],
    startTime: ['09:00', Validators.required],
    endTime: ['12:00', Validators.required],
    slotMinutes: this.fb.control<number | null>(30, [
      Validators.required,
      Validators.min(5),
      Validators.max(240),
    ]),
  });

  constructor() {
    // The slot length starts at the physician's usual consultation length.
    effect(() => {
      const minutes = this.physician()?.consultationMinutes;
      if (minutes) {
        untracked(() => this.form.controls.slotMinutes.setValue(minutes));
      }
    });
  }

  protected openPublish(): void {
    // Defaults: the week on screen (or from today, if it has started), on weekdays.
    const from = this.week() < this.today ? this.today : this.week();
    this.form.patchValue({ from, to: addDays(this.week(), 6) });
    this.chosenDays.set(new Set(['monday', 'tuesday', 'wednesday', 'thursday', 'friday']));
    this.daysError.set(null);
    this.publishProblem.set(null);
    this.publishOpen.set(true);
  }

  protected toggleDay(day: DayOfWeek, on: boolean): void {
    const next = new Set(this.chosenDays());
    if (on) {
      next.add(day);
    } else {
      next.delete(day);
    }
    this.chosenDays.set(next);
    this.daysError.set(null);
  }

  protected async publish(): Promise<void> {
    const physicianId = this.physicianId();
    this.form.markAllAsTouched();
    if (this.chosenDays().size === 0) {
      this.daysError.set('Choose at least one day of the week.');
    }
    if (!physicianId || this.form.invalid || this.chosenDays().size === 0 || this.publishing()) {
      return;
    }

    const value = this.form.getRawValue();
    this.publishing.set(true);
    this.publishProblem.set(null);
    try {
      const result = await this.api.invoke(publishSlots, {
        body: {
          physicianId,
          from: value.from,
          to: value.to,
          days: [...this.chosenDays()],
          startTime: value.startTime,
          endTime: value.endTime,
          slotMinutes: value.slotMinutes,
        },
      });
      this.publishOpen.set(false);
      toast.success(
        result.created === 1 ? 'Published 1 slot' : `Published ${result.created} slots`,
        result.skipped > 0
          ? { description: `${result.skipped} skipped because they overlapped open slots.` }
          : undefined,
      );
      this.week.set(weekStart(value.from));
      this.slots.reload();
    } catch (error) {
      const problem = toProblem(error);
      let placed = false;
      for (const [field, messages] of Object.entries(problem.errors ?? {})) {
        if (field === 'days') {
          this.daysError.set(messages[0] ?? null);
          placed = true;
          continue;
        }
        const control = this.form.get(field);
        if (control && messages[0]) {
          control.setErrors({ server: messages[0] });
          placed = true;
        }
      }
      if (!placed) {
        this.publishProblem.set(problem);
      }
    } finally {
      this.publishing.set(false);
    }
  }
}

function physicianName(p: SlotPhysicianResponse): string {
  return `Dr. ${p.firstName} ${p.lastName} · ${p.specialization}`;
}
