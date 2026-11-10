import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  resource,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { STATUS_MAP, StatusPill } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft, lucideCircleAlert, lucidePencil, lucideUserCog } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../../api/api';
import { changeEmployeeStatus } from '../../../api/fn/workforce/change-employee-status';
import { getEmployee } from '../../../api/fn/workforce/get-employee';
import { type EmployeeResponse } from '../../../api/models/employee-response';
import { type EmployeeStatus } from '../../../api/models/employee-status';
import { HasPermission } from '../../../core/auth/has-permission.directive';
import { EmployeeAccessCard } from './employee-access-card';
import { EmployeeWardCard } from './employee-ward-card';
import { formatDate } from '../../../core/format/date-time';
import { type Problem, toProblem } from '../../../core/http/problem';
import {
  EMPLOYMENT_TYPES,
  PROFESSIONAL_TYPES,
  SEXES,
  formatFullName,
  labelOf,
} from './employee-labels';

interface Fact {
  label: string;
  value: string;
  numeric?: boolean;
}

/** One employee: personal and employment details, physician profile, and status changes. */
@Component({
  selector: 'app-employee-profile-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    NgIcon,
    PageHeader,
    FormField,
    HasPermission,
    EmployeeAccessCard,
    EmployeeWardCard,
    StatusPill,
    HlmAlertImports,
    HlmButtonImports,
    HlmCardImports,
    HlmDialogImports,
    HlmInputImports,
    HlmSelectImports,
    HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideArrowLeft, lucideCircleAlert, lucidePencil, lucideUserCog })],
  templateUrl: './employee-profile-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmployeeProfilePage {
  private readonly api = inject(Api);

  /** From the route. */
  readonly id = input.required<string>();

  protected readonly employee = resource({
    params: () => this.id(),
    loader: ({ params }) => this.api.invoke(getEmployee, { id: params }),
  });

  protected readonly current = computed(() =>
    this.employee.hasValue() ? this.employee.value() : undefined,
  );

  protected readonly loadProblem = computed(() =>
    this.employee.error() ? toProblem(this.employee.error()) : null,
  );

  protected readonly name = computed(() => {
    const employee = this.current();
    return employee ? formatFullName(employee) : 'Employee';
  });

  protected readonly personal = computed<Fact[]>(() => {
    const e = this.current();
    return e
      ? [
          { label: 'Sex', value: labelOf(SEXES, e.sex) },
          { label: 'Date of birth', value: formatDate(e.birthDate), numeric: true },
          { label: 'Contact number', value: e.contactNumber ?? '—', numeric: true },
          { label: 'Work email', value: e.email ?? '—' },
        ]
      : [];
  });

  protected readonly employment = computed<Fact[]>(() => {
    const e = this.current();
    return e
      ? [
          { label: 'Department', value: refLabel(e.department) },
          { label: 'Position', value: refLabel(e.position) },
          { label: 'Professional type', value: labelOf(PROFESSIONAL_TYPES, e.professionalType) },
          { label: 'License number', value: e.licenseNumber ?? '—', numeric: true },
          { label: 'Employment type', value: labelOf(EMPLOYMENT_TYPES, e.employmentType) },
          { label: 'Hired on', value: formatDate(e.hiredOn), numeric: true },
          ...(e.separatedOn
            ? [{ label: 'Last day of work', value: formatDate(e.separatedOn), numeric: true }]
            : []),
        ]
      : [];
  });

  protected readonly physician = computed<Fact[] | null>(() => {
    const profile = this.current()?.physician;
    return profile
      ? [
          { label: 'Specialization', value: refLabel(profile.specialization) },
          {
            label: 'Consultation length',
            value: `${profile.consultationMinutes} minutes`,
            numeric: true,
          },
          { label: 'Accepts appointments', value: profile.acceptsAppointments ? 'Yes' : 'No' },
        ]
      : null;
  });

  // ---- Change status ----
  protected readonly statusOpen = signal(false);
  protected readonly savingStatus = signal(false);
  protected readonly statusProblem = signal<Problem | null>(null);
  protected readonly statusOptions = Object.entries(STATUS_MAP.employee).map(
    ([value, appearance]) => ({ value: value as EmployeeStatus, label: appearance.label }),
  );
  protected readonly statusLabel = (value: string) =>
    this.statusOptions.find((o) => o.value === value)?.label ?? value;

  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly statusForm = this.fb.group({
    status: this.fb.control<EmployeeStatus>('active', Validators.required),
    separatedOn: [''],
  });
  protected readonly separating = signal(false);

  constructor() {
    this.statusForm.controls.status.valueChanges.pipe(takeUntilDestroyed()).subscribe((status) => {
      this.separating.set(status === 'separated');
      const date = this.statusForm.controls.separatedOn;
      date.setValidators(status === 'separated' ? Validators.required : null);
      date.updateValueAndValidity({ emitEvent: false });
    });
  }

  protected openStatus(employee: EmployeeResponse): void {
    this.statusForm.reset({ status: employee.status, separatedOn: employee.separatedOn ?? '' });
    this.statusProblem.set(null);
    this.statusOpen.set(true);
  }

  protected async saveStatus(): Promise<void> {
    const employee = this.current();
    this.statusForm.markAllAsTouched();
    if (!employee || this.statusForm.invalid || this.savingStatus()) {
      return;
    }

    const { status, separatedOn } = this.statusForm.getRawValue();
    this.savingStatus.set(true);
    this.statusProblem.set(null);
    try {
      const saved = await this.api.invoke(changeEmployeeStatus, {
        id: employee.id,
        body: {
          status,
          separatedOn: status === 'separated' ? separatedOn : null,
          version: employee.version,
        },
      });
      this.employee.set(saved);
      this.statusOpen.set(false);
      toast.success('Status updated', {
        description: `${formatFullName(saved)} is now ${this.statusLabel(saved.status).toLowerCase()}.`,
      });
    } catch (error) {
      const problem = toProblem(error);
      const message = problem.errors?.['separatedOn']?.[0];
      if (message) {
        this.statusForm.controls.separatedOn.setErrors({ server: message });
      } else if (problem.code === 'concurrency_conflict') {
        this.employee.reload();
        this.statusProblem.set({
          ...problem,
          detail: 'Someone else changed this employee just now. Close this and try again.',
        });
      } else {
        this.statusProblem.set(problem);
      }
    } finally {
      this.savingStatus.set(false);
    }
  }
}

function refLabel(ref: { name: string; isActive: boolean }): string {
  return ref.isActive ? ref.name : `${ref.name} (inactive)`;
}
