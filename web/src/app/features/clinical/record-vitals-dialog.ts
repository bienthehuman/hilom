import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import {
  type AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  type ValidationErrors,
  Validators,
} from '@angular/forms';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { recordVitalSigns } from '../../api/fn/clinical/record-vital-signs';
import { type VitalSignsResponse } from '../../api/models/vital-signs-response';
import { type Problem, toProblem } from '../../core/http/problem';

type NumberControl = FormControl<number | null>;

function numberControl(min: number, max: number): NumberControl {
  return new FormControl<number | null>(null, [Validators.min(min), Validators.max(max)]);
}

const MEASURE_KEYS = [
  'temperatureC',
  'systolicBp',
  'diastolicBp',
  'heartRate',
  'respiratoryRate',
  'oxygenSaturation',
  'painScore',
  'weightKg',
  'heightCm',
] as const;

/** At least one measurement; blood pressure both or neither, diastolic below systolic. */
function vitalsValidator(group: AbstractControl): ValidationErrors | null {
  const v = group.value as Record<string, number | null>;
  if (MEASURE_KEYS.every((key) => v[key] === null || v[key] === undefined)) {
    return { noMeasurement: true };
  }
  const systolic = v['systolicBp'] ?? null;
  const diastolic = v['diastolicBp'] ?? null;
  if ((systolic === null) !== (diastolic === null)) {
    return { halfBloodPressure: true };
  }
  if (systolic !== null && diastolic !== null && diastolic >= systolic) {
    return { invertedBloodPressure: true };
  }
  return null;
}

/**
 * Records a set of vital signs on an encounter. Any subset can be taken; the API flags each value
 * against the shared ranges and the list shows the flags.
 */
@Component({
  selector: 'app-record-vitals-dialog',
  imports: [
    ReactiveFormsModule,
    FormField,
    HlmButtonImports,
    HlmDialogImports,
    HlmInputImports,
    HlmTextareaImports,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-dialog [state]="open() ? 'open' : 'closed'" (closed)="closed.emit()">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="sm:max-w-2xl">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>Record vital signs</h2>
          <p hlmDialogDescription>
            Fill in what you measured. Values outside the normal range are flagged.
          </p>
        </hlm-dialog-header>
        <form
          id="record-vitals-form"
          class="grid gap-4 sm:grid-cols-3"
          [formGroup]="form"
          (ngSubmit)="save()"
          novalidate
        >
          <ui-form-field label="Temperature (°C)" for="vitals-temperature">
            <input
              hlmInput
              id="vitals-temperature"
              type="number"
              inputmode="decimal"
              step="0.1"
              formControlName="temperatureC"
            />
          </ui-form-field>
          <ui-form-field label="Systolic BP (mmHg)" for="vitals-systolic">
            <input
              hlmInput
              id="vitals-systolic"
              type="number"
              inputmode="numeric"
              formControlName="systolicBp"
            />
          </ui-form-field>
          <ui-form-field label="Diastolic BP (mmHg)" for="vitals-diastolic">
            <input
              hlmInput
              id="vitals-diastolic"
              type="number"
              inputmode="numeric"
              formControlName="diastolicBp"
            />
          </ui-form-field>
          <ui-form-field label="Heart rate (bpm)" for="vitals-heart-rate">
            <input
              hlmInput
              id="vitals-heart-rate"
              type="number"
              inputmode="numeric"
              formControlName="heartRate"
            />
          </ui-form-field>
          <ui-form-field label="Respiratory rate (/min)" for="vitals-respiratory-rate">
            <input
              hlmInput
              id="vitals-respiratory-rate"
              type="number"
              inputmode="numeric"
              formControlName="respiratoryRate"
            />
          </ui-form-field>
          <ui-form-field label="SpO₂ (%)" for="vitals-spo2">
            <input
              hlmInput
              id="vitals-spo2"
              type="number"
              inputmode="numeric"
              formControlName="oxygenSaturation"
            />
          </ui-form-field>
          <ui-form-field label="Pain (0–10)" for="vitals-pain">
            <input
              hlmInput
              id="vitals-pain"
              type="number"
              inputmode="numeric"
              formControlName="painScore"
            />
          </ui-form-field>
          <ui-form-field label="Weight (kg)" for="vitals-weight">
            <input
              hlmInput
              id="vitals-weight"
              type="number"
              inputmode="decimal"
              step="0.1"
              formControlName="weightKg"
            />
          </ui-form-field>
          <ui-form-field label="Height (cm)" for="vitals-height">
            <input
              hlmInput
              id="vitals-height"
              type="number"
              inputmode="decimal"
              step="0.1"
              formControlName="heightCm"
            />
          </ui-form-field>
          <ui-form-field label="Notes" for="vitals-notes" class="sm:col-span-3">
            <textarea hlmTextarea id="vitals-notes" rows="2" formControlName="notes"></textarea>
          </ui-form-field>
          @if (formError(); as message) {
            <p class="text-sm text-destructive sm:col-span-3" role="alert">{{ message }}</p>
          }
        </form>
        <hlm-dialog-footer>
          <button hlmBtn variant="outline" type="button" (click)="closed.emit()">Cancel</button>
          <button hlmBtn type="submit" form="record-vitals-form" [disabled]="saving()">
            Save vital signs
          </button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class RecordVitalsDialog {
  private readonly api = inject(Api);

  readonly encounterId = input.required<string>();
  readonly open = input(false);
  readonly closed = output();
  readonly recorded = output<VitalSignsResponse>();

  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = new FormGroup(
    {
      temperatureC: numberControl(30, 45),
      systolicBp: numberControl(40, 300),
      diastolicBp: numberControl(20, 200),
      heartRate: numberControl(20, 300),
      respiratoryRate: numberControl(2, 80),
      oxygenSaturation: numberControl(0, 100),
      painScore: numberControl(0, 10),
      weightKg: numberControl(0.5, 500),
      heightCm: numberControl(20, 300),
      notes: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
    },
    { validators: vitalsValidator },
  );

  constructor() {
    // A fresh form every time the dialog opens.
    effect(() => {
      if (this.open()) {
        untracked(() => {
          this.form.reset();
          this.formError.set(null);
        });
      }
    });
  }

  protected async save(): Promise<void> {
    this.form.markAllAsTouched();
    const groupError = this.form.errors;
    if (groupError) {
      this.formError.set(
        groupError['noMeasurement']
          ? 'Enter at least one measurement.'
          : groupError['halfBloodPressure']
            ? 'Enter both blood pressure numbers.'
            : 'Diastolic must be lower than systolic.',
      );
      return;
    }
    if (this.form.invalid || this.saving()) {
      this.formError.set(null);
      return;
    }
    this.formError.set(null);
    this.saving.set(true);
    const { notes, ...measurements } = this.form.getRawValue();
    try {
      const recorded = await this.api.invoke(recordVitalSigns, {
        encounterId: this.encounterId(),
        body: { ...measurements, notes: notes.trim() || null, recordedAt: null },
      });
      toast.success('Vital signs recorded');
      this.recorded.emit(recorded);
    } catch (error) {
      this.formError.set(this.describe(toProblem(error)));
    } finally {
      this.saving.set(false);
    }
  }

  private describe(problem: Problem): string {
    const first = problem.errors ? Object.values(problem.errors)[0]?.[0] : undefined;
    return first ?? problem.detail ?? "Couldn't record the vital signs.";
  }
}
