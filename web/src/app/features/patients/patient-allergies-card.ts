import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { HlmDropdownMenuImports } from '@hilom/ui/dropdown-menu';
import { EmptyState } from '@hilom/ui/empty-state';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { StatusPill } from '@hilom/ui/status';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCircleAlert,
  lucideCircleCheck,
  lucideCircleX,
  lucideEllipsis,
  lucidePencil,
  lucidePlus,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { changeAllergyStatus } from '../../api/fn/patients/change-allergy-status';
import { recordAllergy } from '../../api/fn/patients/record-allergy';
import { updateAllergy } from '../../api/fn/patients/update-allergy';
import { type AllergyCategory } from '../../api/models/allergy-category';
import { type AllergyResponse } from '../../api/models/allergy-response';
import { type AllergySeverity } from '../../api/models/allergy-severity';
import { AuthService } from '../../core/auth/auth.service';
import { formatDate, hospitalToday } from '../../core/format/date-time';
import { type Problem, toProblem } from '../../core/http/problem';
import { ALLERGY_CATEGORIES, ALLERGY_SEVERITIES, labelOf } from './patient-labels';

type StatusAction = 'resolved' | 'entered_in_error';

/**
 * A patient's allergies: active ones with actions, and history (resolved, entered in error) below.
 * The parent owns the list, so the patient header's severe-allergy banner stays in step.
 */
@Component({
  selector: 'app-patient-allergies-card',
  imports: [
    ReactiveFormsModule,
    NgIcon,
    EmptyState,
    FormField,
    StatusPill,
    HlmAlertImports,
    HlmButtonImports,
    HlmCardImports,
    HlmDialogImports,
    HlmDropdownMenuImports,
    HlmInputImports,
    HlmSelectImports,
    HlmSkeletonImports,
    HlmTextareaImports,
  ],
  providers: [
    provideIcons({
      lucideCircleAlert,
      lucideCircleCheck,
      lucideCircleX,
      lucideEllipsis,
      lucidePencil,
      lucidePlus,
    }),
  ],
  templateUrl: './patient-allergies-card.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PatientAllergiesCard {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);

  readonly patientId = input.required<string>();
  /** Undefined while loading. */
  readonly allergies = input<readonly AllergyResponse[]>();
  readonly loadError = input<string | null>(null);
  /** Something changed; the parent reloads the list. */
  readonly changed = output<void>();
  readonly retry = output<void>();

  protected readonly canManage = computed(() => this.auth.can('patients.allergies.manage'));
  protected readonly active = computed(() =>
    (this.allergies() ?? []).filter((a) => a.status === 'active'),
  );
  protected readonly history = computed(() =>
    (this.allergies() ?? []).filter((a) => a.status !== 'active'),
  );

  protected readonly categoryLabel = (value: string) => labelOf(ALLERGY_CATEGORIES, value);
  protected readonly severityLabel = (value: string) => labelOf(ALLERGY_SEVERITIES, value);
  protected readonly categories = ALLERGY_CATEGORIES;
  protected readonly severities = ALLERGY_SEVERITIES;
  /** The day it was recorded, in hospital time. */
  protected readonly recordedOn = (allergy: AllergyResponse) =>
    formatDate(hospitalToday(new Date(allergy.recordedAt)));

  // ---- Add / edit ----
  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    allergen: ['', [Validators.required, Validators.maxLength(100)]],
    category: this.fb.control<AllergyCategory | null>(null, Validators.required),
    severity: this.fb.control<AllergySeverity | null>(null, Validators.required),
    reaction: ['', Validators.maxLength(300)],
  });
  protected readonly editing = signal<AllergyResponse | null>(null);
  protected readonly formOpen = signal(false);
  protected readonly saving = signal(false);
  protected readonly formProblem = signal<Problem | null>(null);

  protected openAdd(): void {
    this.editing.set(null);
    this.form.reset({ allergen: '', category: null, severity: null, reaction: '' });
    this.formProblem.set(null);
    this.formOpen.set(true);
  }

  protected openEdit(allergy: AllergyResponse): void {
    this.editing.set(allergy);
    this.form.reset({
      allergen: allergy.allergen,
      category: allergy.category,
      severity: allergy.severity,
      reaction: allergy.reaction ?? '',
    });
    this.formProblem.set(null);
    this.formOpen.set(true);
  }

  protected async save(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.saving()) {
      return;
    }

    const value = this.form.getRawValue();
    const body = {
      allergen: value.allergen.trim(),
      category: value.category,
      severity: value.severity,
      reaction: value.reaction.trim() === '' ? null : value.reaction.trim(),
    };
    const editing = this.editing();
    this.saving.set(true);
    this.formProblem.set(null);
    try {
      const saved = editing
        ? await this.api.invoke(updateAllergy, {
            patientId: this.patientId(),
            id: editing.id,
            body: { ...body, version: editing.version },
          })
        : await this.api.invoke(recordAllergy, { patientId: this.patientId(), body });
      this.formOpen.set(false);
      toast.success(editing ? 'Allergy updated' : 'Allergy recorded', {
        description: `${saved.allergen} · ${this.severityLabel(saved.severity)}`,
      });
      this.changed.emit();
    } catch (error) {
      this.showFormProblem(toProblem(error));
    } finally {
      this.saving.set(false);
    }
  }

  private showFormProblem(problem: Problem): void {
    let placed = false;
    for (const [field, messages] of Object.entries(problem.errors ?? {})) {
      const control = this.form.get(field);
      if (control) {
        control.setErrors({ server: messages[0] });
        placed = true;
      }
    }
    if (problem.code === 'allergy_already_recorded') {
      this.form.controls.allergen.setErrors({ server: problem.detail });
    } else if (problem.code === 'concurrency_conflict') {
      this.changed.emit();
      this.formProblem.set({
        ...problem,
        detail: 'Someone else changed this allergy just now. Close this and try again.',
      });
    } else if (!placed) {
      this.formProblem.set(problem);
    }
  }

  // ---- Resolve / entered in error ----
  protected readonly statusTarget = signal<AllergyResponse | null>(null);
  protected readonly statusAction = signal<StatusAction>('resolved');
  protected readonly statusOpen = signal(false);
  protected readonly savingStatus = signal(false);
  protected readonly statusProblem = signal<Problem | null>(null);
  protected readonly noteControl = this.fb.control('', Validators.maxLength(500));
  protected readonly statusForm = this.fb.group({ note: this.noteControl });

  protected openStatus(allergy: AllergyResponse, action: StatusAction): void {
    this.statusTarget.set(allergy);
    this.statusAction.set(action);
    this.noteControl.reset('');
    this.noteControl.setValidators(
      action === 'entered_in_error'
        ? [Validators.required, Validators.maxLength(500)]
        : Validators.maxLength(500),
    );
    this.noteControl.updateValueAndValidity();
    this.statusProblem.set(null);
    this.statusOpen.set(true);
  }

  protected async saveStatus(): Promise<void> {
    const allergy = this.statusTarget();
    this.noteControl.markAsTouched();
    if (!allergy || this.noteControl.invalid || this.savingStatus()) {
      return;
    }

    const note = this.noteControl.value.trim();
    this.savingStatus.set(true);
    this.statusProblem.set(null);
    try {
      await this.api.invoke(changeAllergyStatus, {
        patientId: this.patientId(),
        id: allergy.id,
        body: {
          status: this.statusAction(),
          note: note === '' ? null : note,
          version: allergy.version,
        },
      });
      this.statusOpen.set(false);
      toast.success(
        this.statusAction() === 'resolved' ? 'Allergy resolved' : 'Marked as entered in error',
        { description: allergy.allergen },
      );
      this.changed.emit();
    } catch (error) {
      const problem = toProblem(error);
      const message = problem.errors?.['note']?.[0];
      if (message) {
        this.noteControl.setErrors({ server: message });
      } else {
        if (problem.code === 'concurrency_conflict' || problem.code === 'allergy_not_active') {
          this.changed.emit();
        }
        this.statusProblem.set(problem);
      }
    } finally {
      this.savingStatus.set(false);
    }
  }
}
