import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  resource,
  signal,
  untracked,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { BedPicker } from '@hilom/ui/bed-picker';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { EmptyState } from '@hilom/ui/empty-state';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { PageHeader } from '@hilom/ui/page-header';
import { PatientHeader, formatAge } from '@hilom/ui/patient-header';
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft,
  lucideBedDouble,
  lucideCircleAlert,
  lucideCircleCheck,
  lucideSearch,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { admitPatient } from '../../api/fn/clinical/admit-patient';
import { listAttendingPhysicians } from '../../api/fn/clinical/list-attending-physicians';
import { listFreeBeds } from '../../api/fn/clinical/list-free-beds';
import { getPatient } from '../../api/fn/patients/get-patient';
import { listPatients } from '../../api/fn/patients/list-patients';
import { type AttendingPhysicianResponse } from '../../api/models/attending-physician-response';
import { type PatientSummaryResponse } from '../../api/models/patient-summary-response';
import { HasPermission } from '../../core/auth/has-permission.directive';
import { formatDate, hospitalToday } from '../../core/format/date-time';
import { type Problem, toProblem } from '../../core/http/problem';
import { formatPatientName } from '../patients/patient-labels';
import { formatPhysician } from './encounter-labels';

const DIAGNOSIS_MAX = 1000;
const REASON_MAX = 500;

/** Problems that mean the chosen bed can't be used; the bed list is reloaded. */
const BED_PROBLEMS = new Set(['bed_taken', 'bed_not_available']);

/**
 * Admit a patient into a free bed. Opened from a clinic visit (`?patientId=&encounterId=&physicianId=`),
 * from a patient's record (`?patientId=`), or from the sidebar to search for the patient. The inpatient
 * encounter, admission and bed are saved together by the API.
 */
@Component({
  selector: 'app-admit-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    NgIcon,
    BedPicker,
    EmptyState,
    FormField,
    HasPermission,
    PageHeader,
    PatientHeader,
    HlmAlertImports,
    HlmButtonImports,
    HlmCardImports,
    HlmInputImports,
    HlmSelectImports,
    HlmSkeletonImports,
    HlmTextareaImports,
  ],
  providers: [
    provideIcons({
      lucideArrowLeft,
      lucideBedDouble,
      lucideCircleAlert,
      lucideCircleCheck,
      lucideSearch,
    }),
  ],
  templateUrl: './admit-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdmitPage {
  private readonly api = inject(Api);

  /** From the query string. */
  readonly patientId = input<string>();
  /** The clinic visit the patient is admitted from, if any. */
  readonly encounterId = input<string>();
  /** The visit's physician, chosen as attending to start with. */
  readonly physicianId = input<string>();

  protected readonly today = hospitalToday();

  // ---- Patient ----
  protected readonly chosenId = signal<string | null>(null);
  protected readonly query = signal('');
  private searchTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    effect(() => {
      const id = this.patientId();
      untracked(() => this.chosenId.set(id ?? null));
    });
    effect(() => {
      const physician = this.physicianId();
      untracked(() => {
        if (physician && !this.form.controls.attendingPhysicianId.value) {
          this.form.controls.attendingPhysicianId.setValue(physician);
        }
      });
    });
  }

  protected readonly patient = resource({
    params: () => this.chosenId() ?? undefined,
    loader: ({ params }) => this.api.invoke(getPatient, { id: params }),
  });

  protected readonly results = resource({
    params: () => (this.query().length >= 2 ? this.query() : undefined),
    loader: ({ params }) => this.api.invoke(listPatients, { q: params, pageSize: 8 }),
  });
  protected readonly resultList = computed(() =>
    this.results.hasValue() ? this.results.value().items : [],
  );

  protected onSearch(value: string): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.query.set(value.trim()), 250);
  }

  protected choose(id: string): void {
    this.chosenId.set(id);
    this.problem.set(null);
  }

  protected changePatient(): void {
    this.chosenId.set(null);
    this.problem.set(null);
  }

  protected summaryOf(p: PatientSummaryResponse): string {
    return `${p.mrn} · ${formatAge(p.birthDate, this.today)} · born ${formatDate(p.birthDate)}`;
  }

  protected readonly patientName = formatPatientName;

  // ---- Stay ----
  protected readonly form = new FormGroup({
    attendingPhysicianId: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
    admittingDiagnosis: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(DIAGNOSIS_MAX)],
    }),
    reason: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(REASON_MAX)],
    }),
  });

  protected readonly physicians = resource({
    loader: () => this.api.invoke(listAttendingPhysicians, {}),
  });
  protected readonly physicianOptions = computed(() =>
    this.physicians.hasValue() ? this.physicians.value() : [],
  );
  protected readonly physicianName = (d: AttendingPhysicianResponse) => formatPhysician(d);
  protected readonly physicianLabel = (id: string) => {
    const doctor = this.physicianOptions().find((d) => d.id === id);
    return doctor ? `${formatPhysician(doctor)} · ${doctor.specialization}` : '';
  };

  // ---- Bed ----
  protected readonly freeBeds = resource({
    loader: () => this.api.invoke(listFreeBeds, {}),
  });
  protected readonly wardList = computed(() =>
    this.freeBeds.hasValue() ? this.freeBeds.value() : [],
  );
  protected readonly bedId = signal<string | null>(null);
  protected readonly bedError = signal<string | null>(null);

  protected readonly chosenBedLabel = computed(() => {
    const id = this.bedId();
    for (const ward of this.wardList()) {
      const bed = ward.beds.find((b) => b.id === id);
      if (bed) {
        return `${ward.code} · ${ward.name}, ${bed.label}`;
      }
    }
    return 'No bed chosen yet.';
  });

  // ---- Admit ----
  protected readonly admitting = signal(false);
  protected readonly problem = signal<Problem | null>(null);
  protected readonly admitted = signal<{ encounterId: string; where: string } | null>(null);

  protected async admit(): Promise<void> {
    const patientId = this.chosenId();
    const bedId = this.bedId();
    this.form.markAllAsTouched();
    this.bedError.set(bedId ? null : 'Choose a free bed.');
    if (!patientId || !bedId || this.form.invalid || this.admitting()) {
      return;
    }

    const value = this.form.getRawValue();
    const where = this.chosenBedLabel();
    this.admitting.set(true);
    this.problem.set(null);
    try {
      const result = await this.api.invoke(admitPatient, {
        body: {
          patientId,
          parentEncounterId: this.encounterId() ?? null,
          attendingPhysicianId: value.attendingPhysicianId,
          bedId,
          admittingDiagnosis: value.admittingDiagnosis.trim(),
          reason: value.reason.trim(),
        },
      });
      this.admitted.set({ encounterId: result.encounterId, where });
      toast.success('Patient admitted', { description: where });
    } catch (error) {
      const problem = toProblem(error);
      if (problem.code && BED_PROBLEMS.has(problem.code)) {
        this.bedId.set(null);
        this.bedError.set(problem.detail ?? 'Choose another free bed.');
        this.freeBeds.reload();
        return;
      }
      const fields = problem.errors ?? {};
      let handled = false;
      for (const name of ['attendingPhysicianId', 'admittingDiagnosis', 'reason'] as const) {
        const message = fields[name]?.[0];
        if (message) {
          this.form.controls[name].setErrors({ server: message });
          handled = true;
        }
      }
      if (!handled) {
        this.problem.set(problem);
      }
    } finally {
      this.admitting.set(false);
    }
  }
}
