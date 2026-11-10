import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  linkedSignal,
  model,
  resource,
  signal,
  untracked,
} from '@angular/core';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmLabelImports } from '@hilom/ui/label';
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert } from '@ng-icons/lucide';

import { Api } from '../../api/api';
import { listBookablePhysicians } from '../../api/fn/scheduling/list-bookable-physicians';
import { listOpenSlots } from '../../api/fn/scheduling/list-open-slots';
import { listSpecializations } from '../../api/fn/workforce/list-specializations';
import { type OpenSlotResponse } from '../../api/models/open-slot-response';
import {
  addDays,
  formatLongDay,
  formatMonthDay,
  formatTimeRange,
  formatWeekday,
  hospitalDateOf,
  hospitalToday,
} from '../../core/format/date-time';
import { toProblem } from '../../core/http/problem';

/** How far ahead patients can look for a free slot. */
const DAYS = 14;
const ANY = 'any';

interface DoctorSlots {
  physician: OpenSlotResponse['physician'];
  slots: OpenSlotResponse[];
}

/**
 * Find a free slot: filter by specialization and doctor, pick a day in the next two weeks, then a time.
 * Used by staff booking and the patient portal. No patient data is involved.
 */
@Component({
  selector: 'app-slot-picker',
  imports: [
    NgIcon,
    HlmAlertImports,
    HlmButtonImports,
    HlmLabelImports,
    HlmSelectImports,
    HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideCircleAlert })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col gap-4">
      <div class="grid gap-4 sm:grid-cols-2">
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="slot-specialization">Specialization</label>
          <hlm-select
            [value]="specialization()"
            [itemToString]="specializationLabel"
            (valueChange)="setSpecialization($any($event))"
          >
            <hlm-select-trigger buttonId="slot-specialization" class="w-full">
              <hlm-select-value />
            </hlm-select-trigger>
            <hlm-select-content *hlmSelectPortal>
              <hlm-select-item value="any">Any specialization</hlm-select-item>
              @for (s of specializationOptions(); track s.id) {
                <hlm-select-item [value]="s.id">{{ s.name }}</hlm-select-item>
              }
            </hlm-select-content>
          </hlm-select>
        </div>
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="slot-doctor">Doctor</label>
          <hlm-select
            [value]="doctor()"
            [itemToString]="doctorLabel"
            (valueChange)="doctor.set($any($event))"
          >
            <hlm-select-trigger buttonId="slot-doctor" class="w-full">
              <hlm-select-value />
            </hlm-select-trigger>
            <hlm-select-content *hlmSelectPortal>
              <hlm-select-item value="any">Any doctor</hlm-select-item>
              @for (d of doctorOptions(); track d.id) {
                <hlm-select-item [value]="d.id">{{ doctorName(d) }}</hlm-select-item>
              }
            </hlm-select-content>
          </hlm-select>
        </div>
      </div>

      @if (loadError(); as message) {
        <div hlmAlert variant="destructive">
          <ng-icon name="lucideCircleAlert" aria-hidden="true" />
          <h3 hlmAlertTitle>Couldn't load free times</h3>
          <p hlmAlertDescription>{{ message }}</p>
          <div hlmAlertAction>
            <button hlmBtn variant="outline" size="sm" type="button" (click)="slots.reload()">
              Try again
            </button>
          </div>
        </div>
      } @else if (!slots.hasValue()) {
        <div class="flex flex-col gap-3" aria-busy="true">
          <div hlmSkeleton class="h-16 w-full"></div>
          <div hlmSkeleton class="h-24 w-full"></div>
        </div>
      } @else if (allSlots().length === 0) {
        <p class="rounded-lg border bg-card px-4 py-6 text-center text-muted-foreground">
          No free times in the next two weeks{{ isFiltered() ? ' for these choices' : '' }}. Try
          another doctor or specialization.
        </p>
      } @else {
        <fieldset>
          <legend class="mb-2 text-sm font-medium">Day</legend>
          <div class="flex gap-2 overflow-x-auto pb-1">
            @for (day of days; track day) {
              <button
                hlmBtn
                type="button"
                [variant]="day === selectedDay() ? 'default' : 'outline'"
                class="h-auto shrink-0 flex-col gap-0 px-3 py-1.5"
                [attr.aria-pressed]="day === selectedDay()"
                [disabled]="countOn(day) === 0"
                (click)="selectedDay.set(day)"
              >
                <span class="text-xs">{{ formatWeekday(day) }}</span
                >{{ ' ' }}<span class="font-medium" data-numeric>{{ formatMonthDay(day) }}</span>
                <span class="sr-only">, {{ countLabel(day) }}</span>
              </button>
            }
          </div>
        </fieldset>

        <div role="radiogroup" aria-label="Free times" class="flex flex-col gap-4">
          @for (group of dayGroups(); track group.physician.id) {
            <div>
              <p class="mb-2 text-sm font-medium">
                {{ doctorName(group.physician) }}
                <span class="font-normal text-muted-foreground"
                  >· {{ group.physician.specialization }}</span
                >
              </p>
              <div class="flex flex-wrap gap-2">
                @for (slot of group.slots; track slot.id) {
                  <button
                    hlmBtn
                    type="button"
                    role="radio"
                    [variant]="selected()?.id === slot.id ? 'default' : 'outline'"
                    [attr.aria-checked]="selected()?.id === slot.id"
                    [attr.aria-label]="
                      doctorName(group.physician) +
                      ', ' +
                      formatLongDay(day(slot)) +
                      ', ' +
                      formatTimeRange(slot.startsAt, slot.endsAt)
                    "
                    data-numeric
                    (click)="selected.set(slot)"
                  >
                    {{ formatTimeRange(slot.startsAt, slot.endsAt) }}
                  </button>
                }
              </div>
            </div>
          }
        </div>
      }
    </div>
  `,
})
export class SlotPicker {
  private readonly api = inject(Api);

  /** The chosen slot. */
  readonly selected = model<OpenSlotResponse | null>(null);

  private readonly today = hospitalToday();
  protected readonly days = Array.from({ length: DAYS }, (_, i) => addDays(this.today, i));

  // ---- Filters ----
  protected readonly specialization = signal<string>(ANY);
  protected readonly doctor = signal<string>(ANY);

  private readonly specializations = resource({
    loader: () => this.api.invoke(listSpecializations, { pageSize: 100, status: 'active' }),
  });
  protected readonly specializationOptions = computed(() =>
    this.specializations.hasValue() ? this.specializations.value().items : [],
  );
  protected readonly specializationLabel = (id: string) =>
    id === ANY
      ? 'Any specialization'
      : (this.specializationOptions().find((s) => s.id === id)?.name ?? '');

  private readonly doctors = resource({
    params: () => this.specialization(),
    loader: ({ params }) =>
      this.api.invoke(listBookablePhysicians, {
        specializationId: params === ANY ? undefined : params,
      }),
  });
  protected readonly doctorOptions = computed(() =>
    this.doctors.hasValue() ? this.doctors.value() : [],
  );
  protected readonly doctorLabel = (id: string) => {
    if (id === ANY) {
      return 'Any doctor';
    }
    const doctor = this.doctorOptions().find((d) => d.id === id);
    return doctor ? this.doctorName(doctor) : '';
  };

  protected setSpecialization(value: string): void {
    this.specialization.set(value);
    this.doctor.set(ANY);
  }

  protected readonly isFiltered = computed(
    () => this.specialization() !== ANY || this.doctor() !== ANY,
  );

  // ---- Slots ----
  protected readonly slots = resource({
    params: () => ({ specialization: this.specialization(), doctor: this.doctor() }),
    loader: ({ params }) =>
      this.api.invoke(listOpenSlots, {
        from: this.today,
        days: DAYS,
        specializationId: params.specialization === ANY ? undefined : params.specialization,
        physicianId: params.doctor === ANY ? undefined : params.doctor,
      }),
  });
  protected readonly allSlots = computed(() => (this.slots.hasValue() ? this.slots.value() : []));
  protected readonly loadError = computed(() =>
    this.slots.error() ? (toProblem(this.slots.error()).detail ?? 'Please try again.') : null,
  );

  private readonly byDay = computed(() => {
    const map = new Map<string, OpenSlotResponse[]>();
    for (const slot of this.allSlots()) {
      const day = hospitalDateOf(slot.startsAt);
      map.set(day, [...(map.get(day) ?? []), slot]);
    }
    return map;
  });

  /** The first day with free times, until someone picks another. */
  protected readonly selectedDay = linkedSignal(
    () => this.days.find((day) => (this.byDay().get(day)?.length ?? 0) > 0) ?? this.today,
  );

  protected readonly dayGroups = computed<DoctorSlots[]>(() => {
    const groups = new Map<string, DoctorSlots>();
    for (const slot of this.byDay().get(this.selectedDay()) ?? []) {
      const group = groups.get(slot.physician.id) ?? { physician: slot.physician, slots: [] };
      group.slots.push(slot);
      groups.set(slot.physician.id, group);
    }
    return [...groups.values()];
  });

  constructor() {
    // A choice that's no longer listed (new filters, someone else booked it) is cleared.
    effect(() => {
      const ids = new Set(this.allSlots().map((s) => s.id));
      untracked(() => {
        const current = this.selected();
        if (current && this.slots.hasValue() && !ids.has(current.id)) {
          this.selected.set(null);
        }
      });
    });
  }

  /** Reloads the free times, e.g. after a slot was taken. */
  refresh(): void {
    this.slots.reload();
  }

  protected countOn(day: string): number {
    return this.byDay().get(day)?.length ?? 0;
  }

  protected countLabel(day: string): string {
    const count = this.countOn(day);
    return count === 0 ? 'no free times' : count === 1 ? '1 free time' : `${count} free times`;
  }

  protected doctorName(doctor: { firstName: string; lastName: string }): string {
    return `Dr. ${doctor.firstName} ${doctor.lastName}`;
  }

  protected day(slot: OpenSlotResponse): string {
    return hospitalDateOf(slot.startsAt);
  }

  protected readonly formatLongDay = formatLongDay;
  protected readonly formatMonthDay = formatMonthDay;
  protected readonly formatWeekday = formatWeekday;
  protected readonly formatTimeRange = formatTimeRange;
}
