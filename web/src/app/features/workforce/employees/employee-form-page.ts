import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  resource,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { HlmLabelImports } from '@hilom/ui/label';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { HlmSwitchImports } from '@hilom/ui/switch';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../../api/api';
import { createEmployee } from '../../../api/fn/workforce/create-employee';
import { getEmployee } from '../../../api/fn/workforce/get-employee';
import { listDepartments } from '../../../api/fn/workforce/list-departments';
import { listPositions } from '../../../api/fn/workforce/list-positions';
import { listSpecializations } from '../../../api/fn/workforce/list-specializations';
import { updateEmployee } from '../../../api/fn/workforce/update-employee';
import { type CreateEmployeeRequest } from '../../../api/models/create-employee-request';
import { type EmployeeResponse } from '../../../api/models/employee-response';
import { type EmploymentType } from '../../../api/models/employment-type';
import { type LookupRef } from '../../../api/models/lookup-ref';
import { type ProfessionalType } from '../../../api/models/professional-type';
import { type Sex } from '../../../api/models/sex';
import { type Problem, toProblem } from '../../../core/http/problem';
import {
  EMPLOYMENT_TYPES,
  PROFESSIONAL_TYPES,
  SEXES,
  formatFullName,
  labelOf,
  requiresLicense,
} from './employee-labels';

const DEFAULT_CONSULTATION_MINUTES = 30;

/** Create an employee, or edit one (`/workforce/employees/:id/edit`). Status changes live on the profile. */
@Component({
  selector: 'app-employee-form-page',
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
    HlmLabelImports,
    HlmSelectImports,
    HlmSkeletonImports,
    HlmSwitchImports,
  ],
  providers: [provideIcons({ lucideCircleAlert })],
  templateUrl: './employee-form-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmployeeFormPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  /** From the route; absent when creating. */
  readonly id = input<string>();

  protected readonly isEdit = computed(() => this.id() !== undefined);

  protected readonly employee = resource({
    params: () => this.id(),
    loader: ({ params }) => this.api.invoke(getEmployee, { id: params }),
  });

  protected readonly loadError = computed(() =>
    this.employee.error()
      ? (toProblem(this.employee.error()).detail ?? "Couldn't load this employee.")
      : null,
  );

  protected readonly title = computed(() => {
    if (!this.isEdit()) {
      return 'New employee';
    }
    return this.employee.hasValue()
      ? `Edit ${formatFullName(this.employee.value())}`
      : 'Edit employee';
  });

  // ---- Choices ----
  private readonly departments = resource({
    loader: () => this.api.invoke(listDepartments, { pageSize: 100, status: 'active' }),
  });
  private readonly positions = resource({
    loader: () => this.api.invoke(listPositions, { pageSize: 100, status: 'active' }),
  });
  private readonly specializations = resource({
    loader: () => this.api.invoke(listSpecializations, { pageSize: 100, status: 'active' }),
  });

  /** Active entries, plus the employee's current one if it has since been deactivated. */
  protected readonly departmentOptions = computed(() =>
    withCurrent(
      this.departments.hasValue() ? this.departments.value().items : [],
      this.current()?.department,
    ),
  );
  protected readonly positionOptions = computed(() =>
    withCurrent(
      this.positions.hasValue() ? this.positions.value().items : [],
      this.current()?.position,
    ),
  );
  protected readonly specializationOptions = computed(() =>
    withCurrent(
      this.specializations.hasValue() ? this.specializations.value().items : [],
      this.current()?.physician?.specialization,
    ),
  );

  private readonly current = computed(() =>
    this.employee.hasValue() ? this.employee.value() : undefined,
  );

  protected readonly sexes = SEXES;
  protected readonly professionalTypes = PROFESSIONAL_TYPES;
  protected readonly employmentTypes = EMPLOYMENT_TYPES;
  protected readonly sexLabel = (value: string) => labelOf(SEXES, value);
  protected readonly professionalTypeLabel = (value: string) => labelOf(PROFESSIONAL_TYPES, value);
  protected readonly employmentTypeLabel = (value: string) => labelOf(EMPLOYMENT_TYPES, value);
  protected readonly departmentLabel = (id: string) =>
    this.departmentOptions().find((o) => o.id === id)?.name ?? '';
  protected readonly positionLabel = (id: string) =>
    this.positionOptions().find((o) => o.id === id)?.name ?? '';
  protected readonly specializationLabel = (id: string) =>
    this.specializationOptions().find((o) => o.id === id)?.name ?? '';

  // ---- Form ----
  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    middleName: ['', Validators.maxLength(100)],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    sex: this.fb.control<Sex | null>(null, Validators.required),
    birthDate: ['', Validators.required],
    contactNumber: ['', Validators.maxLength(30)],
    email: ['', [Validators.email, Validators.maxLength(320)]],
    departmentId: ['', Validators.required],
    positionId: ['', Validators.required],
    professionalType: this.fb.control<ProfessionalType | null>(null, Validators.required),
    licenseNumber: ['', Validators.maxLength(50)],
    employmentType: this.fb.control<EmploymentType | null>('full_time', Validators.required),
    hiredOn: ['', Validators.required],
    physician: this.fb.group({
      specializationId: [''],
      consultationMinutes: this.fb.control<number | null>(DEFAULT_CONSULTATION_MINUTES, [
        Validators.min(5),
        Validators.max(240),
      ]),
      acceptsAppointments: [true],
    }),
  });

  protected readonly professionalType = signal<ProfessionalType | null>(null);
  protected readonly isPhysician = computed(() => this.professionalType() === 'physician');
  protected readonly needsLicense = computed(() => requiresLicense(this.professionalType()));

  protected readonly saving = signal(false);
  protected readonly problem = signal<Problem | null>(null);

  constructor() {
    // License and physician fields are required only for some professional types.
    this.form.controls.professionalType.valueChanges
      .pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe((type) => this.applyTypeRules(type));

    // Fill the form once the employee loads (edit only).
    effect(() => {
      const employee = this.current();
      if (employee) {
        untracked(() => this.fill(employee));
      }
    });
  }

  private applyTypeRules(type: ProfessionalType | null): void {
    this.professionalType.set(type);
    const license = this.form.controls.licenseNumber;
    license.setValidators(
      requiresLicense(type)
        ? [Validators.required, Validators.maxLength(50)]
        : Validators.maxLength(50),
    );
    license.updateValueAndValidity({ emitEvent: false });

    const specialization = this.form.controls.physician.controls.specializationId;
    specialization.setValidators(type === 'physician' ? Validators.required : null);
    specialization.updateValueAndValidity({ emitEvent: false });
  }

  private fill(employee: EmployeeResponse): void {
    this.form.reset({
      firstName: employee.firstName,
      middleName: employee.middleName ?? '',
      lastName: employee.lastName,
      sex: employee.sex,
      birthDate: employee.birthDate,
      contactNumber: employee.contactNumber ?? '',
      email: employee.email ?? '',
      departmentId: employee.department.id,
      positionId: employee.position.id,
      professionalType: employee.professionalType,
      licenseNumber: employee.licenseNumber ?? '',
      employmentType: employee.employmentType,
      hiredOn: employee.hiredOn,
      physician: {
        specializationId: employee.physician?.specialization.id ?? '',
        consultationMinutes:
          employee.physician?.consultationMinutes ?? DEFAULT_CONSULTATION_MINUTES,
        acceptsAppointments: employee.physician?.acceptsAppointments ?? true,
      },
    });
    this.applyTypeRules(employee.professionalType);
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
          ? await this.api.invoke(createEmployee, { body })
          : await this.api.invoke(updateEmployee, { id, body: { ...body, version } });
      toast.success(id ? 'Changes saved' : 'Employee added', {
        description: `${formatFullName(saved)} · ${saved.employeeNo}`,
      });
      await this.router.navigate(['/workforce/employees', saved.id]);
    } catch (error) {
      this.showProblem(toProblem(error));
    } finally {
      this.saving.set(false);
    }
  }

  private body(): CreateEmployeeRequest {
    const value = this.form.getRawValue();
    const optional = (text: string) => (text.trim() === '' ? null : text.trim());
    return {
      firstName: value.firstName.trim(),
      middleName: optional(value.middleName),
      lastName: value.lastName.trim(),
      sex: value.sex,
      birthDate: value.birthDate || null,
      contactNumber: optional(value.contactNumber),
      email: optional(value.email),
      departmentId: value.departmentId,
      positionId: value.positionId,
      professionalType: value.professionalType,
      licenseNumber: optional(value.licenseNumber),
      employmentType: value.employmentType,
      hiredOn: value.hiredOn || null,
      physician:
        value.professionalType === 'physician'
          ? {
              specializationId: value.physician.specializationId,
              consultationMinutes: value.physician.consultationMinutes,
              acceptsAppointments: value.physician.acceptsAppointments,
            }
          : null,
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
          'Someone else changed this employee while you were editing. Reload to see their changes.',
      });
    } else if (!placed) {
      this.problem.set(problem);
    }
  }
}

interface Choice {
  id: string;
  name: string;
}

function withCurrent(active: readonly Choice[], current?: LookupRef): Choice[] {
  const options = active.map(({ id, name }) => ({ id, name }));
  if (current && !options.some((o) => o.id === current.id)) {
    options.push({ id: current.id, name: `${current.name} (inactive)` });
  }
  return options;
}
