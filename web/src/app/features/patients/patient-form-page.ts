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
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { getPatient } from '../../api/fn/patients/get-patient';
import { registerPatient } from '../../api/fn/patients/register-patient';
import { updatePatient } from '../../api/fn/patients/update-patient';
import { type CivilStatus } from '../../api/models/civil-status';
import { type PatientResponse } from '../../api/models/patient-response';
import { type RegisterPatientRequest } from '../../api/models/register-patient-request';
import { type Sex } from '../../api/models/sex';
import { hospitalToday } from '../../core/format/date-time';
import { type Problem, toProblem } from '../../core/http/problem';
import { CIVIL_STATUSES, SEXES, formatPatientName, labelOf } from './patient-labels';

/** Not stated: civil status is optional. */
const NOT_STATED = 'not_stated';

/** Register a patient, or edit their demographics (`/patients/:id/edit`). */
@Component({
  selector: 'app-patient-form-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    NgIcon,
    PageHeader,
    FormField,
    HlmAlertImports,
    HlmButtonImports,
    HlmCardImports,
    HlmInputImports,
    HlmSelectImports,
    HlmSkeletonImports,
    HlmTextareaImports,
  ],
  providers: [provideIcons({ lucideCircleAlert })],
  templateUrl: './patient-form-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PatientFormPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  /** From the route; absent when registering. */
  readonly id = input<string>();

  protected readonly isEdit = computed(() => this.id() !== undefined);
  protected readonly today = hospitalToday();

  protected readonly patient = resource({
    params: () => this.id(),
    loader: ({ params }) => this.api.invoke(getPatient, { id: params }),
  });

  private readonly current = computed(() =>
    this.patient.hasValue() ? this.patient.value() : undefined,
  );

  protected readonly loadProblem = computed(() =>
    this.patient.error() ? toProblem(this.patient.error()) : null,
  );

  protected readonly title = computed(() => {
    if (!this.isEdit()) {
      return 'Register patient';
    }
    const patient = this.current();
    return patient ? `Edit ${formatPatientName(patient)}` : 'Edit patient';
  });

  protected readonly sexes = SEXES;
  protected readonly civilStatuses = CIVIL_STATUSES;
  protected readonly sexLabel = (value: string) => labelOf(SEXES, value);
  protected readonly civilStatusLabel = (value: string) =>
    value === NOT_STATED ? 'Not stated' : labelOf(CIVIL_STATUSES, value);

  // ---- Form ----
  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    middleName: ['', Validators.maxLength(100)],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    birthDate: ['', Validators.required],
    sex: this.fb.control<Sex | null>(null, Validators.required),
    civilStatus: this.fb.control<NonNullable<CivilStatus> | typeof NOT_STATED>(NOT_STATED),
    religion: ['', Validators.maxLength(60)],
    address: ['', [Validators.required, Validators.maxLength(300)]],
    contactNumber: ['', [Validators.required, Validators.maxLength(30)]],
    emergencyContactName: ['', [Validators.required, Validators.maxLength(200)]],
    emergencyContactRelation: ['', [Validators.required, Validators.maxLength(50)]],
    emergencyContactNumber: ['', [Validators.required, Validators.maxLength(30)]],
  });

  protected readonly saving = signal(false);
  protected readonly problem = signal<Problem | null>(null);

  constructor() {
    // Fill the form once the patient loads (edit only).
    effect(() => {
      const patient = this.current();
      if (patient) {
        untracked(() => this.fill(patient));
      }
    });
  }

  private fill(patient: PatientResponse): void {
    this.form.reset({
      firstName: patient.firstName,
      middleName: patient.middleName ?? '',
      lastName: patient.lastName,
      birthDate: patient.birthDate,
      sex: patient.sex,
      civilStatus: patient.civilStatus ?? NOT_STATED,
      religion: patient.religion ?? '',
      address: patient.address,
      contactNumber: patient.contactNumber,
      emergencyContactName: patient.emergencyContactName,
      emergencyContactRelation: patient.emergencyContactRelation,
      emergencyContactNumber: patient.emergencyContactNumber,
    });
  }

  protected async save(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.saving()) {
      return;
    }

    const body = this.body();
    const id = this.id();
    const version = this.current()?.version;
    this.saving.set(true);
    this.problem.set(null);
    try {
      const saved =
        id === undefined || version === undefined
          ? await this.api.invoke(registerPatient, { body })
          : await this.api.invoke(updatePatient, { id, body: { ...body, version } });
      toast.success(id ? 'Changes saved' : 'Patient registered', {
        description: `${formatPatientName(saved)} · ${saved.mrn}`,
      });
      await this.router.navigate(['/patients', saved.id]);
    } catch (error) {
      this.showProblem(toProblem(error));
    } finally {
      this.saving.set(false);
    }
  }

  private body(): RegisterPatientRequest {
    const value = this.form.getRawValue();
    const optional = (text: string) => (text.trim() === '' ? null : text.trim());
    return {
      firstName: value.firstName.trim(),
      middleName: optional(value.middleName),
      lastName: value.lastName.trim(),
      birthDate: value.birthDate || null,
      sex: value.sex,
      civilStatus: value.civilStatus === NOT_STATED ? null : value.civilStatus,
      religion: optional(value.religion),
      address: value.address.trim(),
      contactNumber: value.contactNumber.trim(),
      emergencyContactName: value.emergencyContactName.trim(),
      emergencyContactRelation: value.emergencyContactRelation.trim(),
      emergencyContactNumber: value.emergencyContactNumber.trim(),
    };
  }

  private showProblem(problem: Problem): void {
    let placed = false;
    for (const [field, messages] of Object.entries(problem.errors ?? {})) {
      const control = this.form.get(field);
      if (control) {
        control.setErrors({ server: messages[0] });
        control.markAsTouched();
        placed = true;
      }
    }

    if (problem.code === 'concurrency_conflict') {
      this.problem.set({
        ...problem,
        detail:
          'Someone else changed this patient while you were editing. Reload to see their changes.',
      });
    } else if (!placed) {
      this.problem.set(problem);
    }
  }
}
