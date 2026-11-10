import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBedDouble } from '@ng-icons/lucide';

import { formatAge } from './patient-age';

/** What the header needs to identify a patient. API patient responses fit as they are. */
export interface PatientIdentity {
  mrn: string;
  firstName: string;
  middleName?: string | null;
  lastName: string;
  /** `YYYY-MM-DD` */
  birthDate: string;
  sex: 'male' | 'female';
}

const SEX_LABELS = { male: 'Male', female: 'Female' } as const;

/**
 * The top of every patient and encounter screen (docs/DESIGN_SYSTEM.md §7): name, MRN, age and sex,
 * and where the patient is when admitted. The name is the page's heading.
 *
 * ```html
 * <ui-patient-header [patient]="patient" [today]="today" location="Ward 3 · Bed 12">
 *   <a actions hlmBtn>Edit</a>
 * </ui-patient-header>
 * ```
 */
@Component({
  selector: 'ui-patient-header',
  imports: [NgIcon],
  providers: [provideIcons({ lucideBedDouble })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <div class="flex flex-wrap items-end justify-between gap-x-6 gap-y-3">
      <div class="min-w-0">
        @if (level() === 1) {
          <h1 class="text-2xl font-semibold tracking-tight text-foreground">{{ name() }}</h1>
        } @else {
          <h2 class="text-xl font-semibold tracking-tight text-foreground">{{ name() }}</h2>
        }
        <dl
          class="mt-1.5 flex flex-wrap items-center gap-x-4 gap-y-1 text-sm text-muted-foreground"
        >
          <div class="flex gap-1">
            <dt class="sr-only">Medical record number</dt>
            <dd class="font-medium text-foreground" data-numeric>{{ patient().mrn }}</dd>
          </div>
          <div class="flex gap-1">
            <dt class="sr-only">Age and sex</dt>
            <dd data-numeric>{{ ageAndSex() }}</dd>
          </div>
          @if (location(); as where) {
            <div class="flex items-center gap-1">
              <dt class="sr-only">Location</dt>
              <dd class="flex items-center gap-1">
                <ng-icon name="lucideBedDouble" aria-hidden="true" />{{ where }}
              </dd>
            </div>
          }
        </dl>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <ng-content select="[actions]" />
      </div>
    </div>
  `,
})
export class PatientHeader {
  readonly patient = input.required<PatientIdentity>();
  /** Today in hospital time (`YYYY-MM-DD`), for the age. */
  readonly today = input.required<string>();
  /** Ward and bed while admitted, e.g. "Ward 3 · Bed 12". */
  readonly location = input<string | null>();
  /** 1 on the patient's own page; 2 where the page has another heading. */
  readonly level = input<1 | 2>(1);

  /** "Dela Cruz, Juan Santos": last name first, as on hospital records. */
  protected readonly name = computed(() => {
    const p = this.patient();
    return `${p.lastName}, ${[p.firstName, p.middleName].filter(Boolean).join(' ')}`;
  });

  protected readonly ageAndSex = computed(() => {
    const p = this.patient();
    const age = formatAge(p.birthDate, this.today());
    return [age, SEX_LABELS[p.sex]].filter(Boolean).join(' · ');
  });
}
