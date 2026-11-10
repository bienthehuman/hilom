import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  linkedSignal,
  resource,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { PatientHeader } from '@hilom/ui/patient-header';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft, lucideCircleAlert, lucidePencil } from '@ng-icons/lucide';

import { Api } from '../../api/api';
import { getPatient } from '../../api/fn/patients/get-patient';
import { listAllergies } from '../../api/fn/patients/list-allergies';
import { type AllergyResponse } from '../../api/models/allergy-response';
import { HasPermission } from '../../core/auth/has-permission.directive';
import { formatDate, formatDateTime, hospitalToday } from '../../core/format/date-time';
import { toProblem } from '../../core/http/problem';
import { PatientAllergiesCard } from './patient-allergies-card';
import { CIVIL_STATUSES, SEXES, labelOf } from './patient-labels';

interface Fact {
  label: string;
  value: string;
  numeric?: boolean;
  wide?: boolean;
}

/** One patient's record: demographics and contacts. Opening it is logged as a record view. */
@Component({
  selector: 'app-patient-record-page',
  imports: [
    RouterLink,
    NgIcon,
    PatientHeader,
    PatientAllergiesCard,
    HasPermission,
    HlmAlertImports,
    HlmButtonImports,
    HlmCardImports,
    HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideArrowLeft, lucideCircleAlert, lucidePencil })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-4xl">
      <a hlmBtn variant="ghost" size="sm" class="-ms-2 mb-2" routerLink="/patients">
        <ng-icon name="lucideArrowLeft" aria-hidden="true" data-icon="inline-start" />Patients
      </a>

      @if (loadProblem(); as problem) {
        <div hlmAlert variant="destructive">
          <ng-icon name="lucideCircleAlert" aria-hidden="true" />
          <h1 hlmAlertTitle>
            {{ problem.status === 404 ? 'Patient not found' : "Couldn't load this patient" }}
          </h1>
          <p hlmAlertDescription>
            {{
              problem.status === 404
                ? "This record doesn't exist, or it isn't one you can see."
                : (problem.detail ?? 'Please try again.')
            }}
          </p>
          @if (problem.status !== 404) {
            <div hlmAlertAction>
              <button hlmBtn variant="outline" size="sm" type="button" (click)="patient.reload()">
                Try again
              </button>
            </div>
          }
        </div>
      } @else if (current(); as p) {
        <ui-patient-header
          class="mb-6 border-b pb-4"
          [patient]="p"
          [today]="today"
          [severeAllergies]="severeAllergies()"
        >
          <a
            *appHasPermission="'patients.edit'"
            actions
            hlmBtn
            [routerLink]="['/patients', p.id, 'edit']"
          >
            <ng-icon name="lucidePencil" aria-hidden="true" data-icon="inline-start" />Edit
          </a>
        </ui-patient-header>

        <div class="grid gap-6 lg:grid-cols-2">
          <app-patient-allergies-card
            class="lg:col-span-2"
            [patientId]="p.id"
            [allergies]="allergyList()"
            [loadError]="allergyError()"
            (changed)="allergies.reload()"
            (retry)="allergies.reload()"
          />

          <section hlmCard aria-labelledby="personal-heading">
            <div hlmCardHeader>
              <h2 id="personal-heading" hlmCardTitle>Personal details</h2>
            </div>
            <dl hlmCardContent class="grid gap-x-6 gap-y-4 sm:grid-cols-2">
              @for (fact of personal(); track fact.label) {
                <div>
                  <dt class="text-sm text-muted-foreground">{{ fact.label }}</dt>
                  <dd [attr.data-numeric]="fact.numeric || null">{{ fact.value }}</dd>
                </div>
              }
            </dl>
          </section>

          <section hlmCard aria-labelledby="contact-heading">
            <div hlmCardHeader>
              <h2 id="contact-heading" hlmCardTitle>Contact</h2>
            </div>
            <dl hlmCardContent class="grid gap-x-6 gap-y-4 sm:grid-cols-2">
              @for (fact of contact(); track fact.label) {
                <div [class]="fact.wide ? 'sm:col-span-2' : ''">
                  <dt class="text-sm text-muted-foreground">{{ fact.label }}</dt>
                  <dd class="break-words" [attr.data-numeric]="fact.numeric || null">
                    {{ fact.value }}
                  </dd>
                </div>
              }
            </dl>
          </section>
        </div>

        <p class="mt-6 text-sm text-muted-foreground">
          Registered <span data-numeric>{{ registeredAt() }}</span>
          @if (p.registeredBy) {
            by {{ p.registeredBy }}
          }
        </p>
      } @else {
        <div class="flex flex-col gap-6" aria-busy="true">
          <div hlmSkeleton class="h-16 w-80 rounded-lg"></div>
          <div class="grid gap-6 lg:grid-cols-2">
            <div hlmSkeleton class="h-48 w-full rounded-lg"></div>
            <div hlmSkeleton class="h-48 w-full rounded-lg"></div>
          </div>
        </div>
      }
    </div>
  `,
})
export class PatientRecordPage {
  private readonly api = inject(Api);

  /** From the route. */
  readonly id = input.required<string>();

  protected readonly today = hospitalToday();

  protected readonly patient = resource({
    params: () => this.id(),
    loader: ({ params }) => this.api.invoke(getPatient, { id: params }),
  });

  protected readonly allergies = resource({
    params: () => this.id(),
    loader: ({ params }) => this.api.invoke(listAllergies, { patientId: params }),
  });

  /** Kept while reloading after a change, so the list doesn't flash back to skeletons. */
  protected readonly allergyList = linkedSignal<
    AllergyResponse[] | undefined,
    AllergyResponse[] | undefined
  >({
    source: () => (this.allergies.hasValue() ? this.allergies.value() : undefined),
    computation: (next, previous) => next ?? (this.allergies.error() ? undefined : previous?.value),
  });

  protected readonly allergyError = computed(() =>
    this.allergies.error()
      ? (toProblem(this.allergies.error()).detail ?? "Couldn't load allergies.")
      : null,
  );

  protected readonly severeAllergies = computed(() =>
    (this.allergyList() ?? []).filter((a) => a.status === 'active' && a.severity === 'severe'),
  );

  protected readonly current = computed(() =>
    this.patient.hasValue() ? this.patient.value() : undefined,
  );

  protected readonly loadProblem = computed(() =>
    this.patient.error() ? toProblem(this.patient.error()) : null,
  );

  protected readonly personal = computed<Fact[]>(() => {
    const p = this.current();
    return p
      ? [
          { label: 'Date of birth', value: formatDate(p.birthDate), numeric: true },
          { label: 'Sex', value: labelOf(SEXES, p.sex) },
          {
            label: 'Civil status',
            value: p.civilStatus ? labelOf(CIVIL_STATUSES, p.civilStatus) : 'Not stated',
          },
          { label: 'Religion', value: p.religion ?? '—' },
        ]
      : [];
  });

  protected readonly contact = computed<Fact[]>(() => {
    const p = this.current();
    return p
      ? [
          { label: 'Home address', value: p.address, wide: true },
          { label: 'Contact number', value: p.contactNumber, numeric: true },
          {
            label: 'Emergency contact',
            value: `${p.emergencyContactName} (${p.emergencyContactRelation})`,
          },
          {
            label: 'Emergency contact number',
            value: p.emergencyContactNumber,
            numeric: true,
          },
        ]
      : [];
  });

  protected readonly registeredAt = computed(() => formatDateTime(this.current()?.registeredAt));
}
