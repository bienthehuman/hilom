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
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { FormField } from '@hilom/ui/form-field';
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmTextareaImports } from '@hilom/ui/textarea';

import { Api } from '../../api/api';
import { dischargePatient } from '../../api/fn/clinical/discharge-patient';
import { type DischargeDisposition } from '../../api/models/discharge-disposition';
import { type EncounterResponse } from '../../api/models/encounter-response';
import { toProblem } from '../../core/http/problem';
import { DISCHARGE_DISPOSITIONS, labelOf } from './encounter-labels';

const SUMMARY_MAX = 10_000;

/**
 * Discharge an admitted patient: how the stay ended and the discharge summary. The summary is saved
 * as a discharge-summary note on the stay; the bed frees up and the stay closes.
 */
@Component({
  selector: 'app-discharge-dialog',
  imports: [
    ReactiveFormsModule,
    FormField,
    HlmButtonImports,
    HlmDialogImports,
    HlmSelectImports,
    HlmTextareaImports,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-dialog [state]="open() ? 'open' : 'closed'" (closed)="closed.emit()">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="sm:max-w-2xl">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>Discharge patient</h2>
          <p hlmDialogDescription>
            The bed frees up and the stay closes. The summary is saved to the stay's notes, where
            you can edit it for 24 hours.
          </p>
        </hlm-dialog-header>
        <form
          id="discharge-form"
          class="flex flex-col gap-4"
          [formGroup]="form"
          (ngSubmit)="save()"
          novalidate
        >
          <ui-form-field label="Disposition" for="discharge-disposition" required>
            <hlm-select formControlName="disposition" [itemToString]="dispositionLabel">
              <hlm-select-trigger buttonId="discharge-disposition" class="w-full sm:w-80">
                <hlm-select-value placeholder="How the stay ended" />
              </hlm-select-trigger>
              <hlm-select-content *hlmSelectPortal>
                @for (option of dispositions; track option.value) {
                  <hlm-select-item [value]="option.value">{{ option.label }}</hlm-select-item>
                }
              </hlm-select-content>
            </hlm-select>
          </ui-form-field>
          <ui-form-field
            label="Discharge summary"
            for="discharge-summary"
            hint="Course in hospital, condition on discharge, medications and follow-up."
            required
          >
            <textarea
              hlmTextarea
              id="discharge-summary"
              rows="10"
              formControlName="summary"
            ></textarea>
          </ui-form-field>
          @if (formError(); as message) {
            <p class="text-sm text-destructive" role="alert">{{ message }}</p>
          }
        </form>
        <hlm-dialog-footer>
          <button hlmBtn variant="outline" type="button" (click)="closed.emit()">Cancel</button>
          <button hlmBtn type="submit" form="discharge-form" [disabled]="saving()">
            {{ saving() ? 'Discharging…' : 'Discharge patient' }}
          </button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class DischargeDialog {
  private readonly api = inject(Api);

  readonly encounter = input.required<EncounterResponse>();
  readonly open = input(false);
  readonly closed = output();
  /** The closed stay. */
  readonly discharged = output<EncounterResponse>();

  protected readonly dispositions = DISCHARGE_DISPOSITIONS;
  protected readonly dispositionLabel = (value: string) => labelOf(DISCHARGE_DISPOSITIONS, value);
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly form = new FormGroup({
    disposition: new FormControl<NonNullable<DischargeDisposition> | null>(
      null,
      Validators.required,
    ),
    summary: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(SUMMARY_MAX)],
    }),
  });

  constructor() {
    // A fresh form every time it opens.
    effect(() => {
      if (this.open()) {
        untracked(() => {
          this.form.reset({ disposition: null, summary: '' });
          this.formError.set(null);
        });
      }
    });
  }

  protected async save(): Promise<void> {
    const { disposition, summary } = this.form.getRawValue();
    if (!summary.trim()) {
      this.form.controls.summary.setValue('');
    }
    this.form.markAllAsTouched();
    const admission = this.encounter().admission;
    if (this.form.invalid || !disposition || !admission || this.saving()) {
      return;
    }
    this.formError.set(null);
    this.saving.set(true);
    try {
      const closed = await this.api.invoke(dischargePatient, {
        id: this.encounter().id,
        body: { disposition, summary: summary.trim(), version: admission.version },
      });
      this.discharged.emit(closed);
    } catch (error) {
      const problem = toProblem(error);
      let shown = false;
      for (const [field, messages] of Object.entries(problem.errors ?? {})) {
        const control = this.form.get(field);
        if (control && messages[0]) {
          control.setErrors({ server: messages[0] });
          shown = true;
        }
      }
      if (!shown) {
        this.formError.set(
          problem.code === 'concurrency_conflict'
            ? 'This stay changed meanwhile (the patient was moved). Close this and check the latest.'
            : (problem.detail ?? "Couldn't discharge the patient."),
        );
      }
    } finally {
      this.saving.set(false);
    }
  }
}
