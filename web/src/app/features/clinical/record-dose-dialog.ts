import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  type ValidationErrors,
  Validators,
} from '@angular/forms';
import { AllergyWarning } from '@hilom/ui/allergy-warning';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCheckboxImports } from '@hilom/ui/checkbox';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { HlmLabelImports } from '@hilom/ui/label';
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { recordMedicationAdministration } from '../../api/fn/clinical/record-medication-administration';
import { type EncounterAllergyRef } from '../../api/models/encounter-allergy-ref';
import { type MarOrderResponse } from '../../api/models/mar-order-response';
import { type MedicationAdministrationResponse } from '../../api/models/medication-administration-response';
import { type MedicationOutcome } from '../../api/models/medication-outcome';
import { type Problem, toProblem } from '../../core/http/problem';
import { labelOf, MEDICATION_OUTCOMES } from './encounter-labels';

/** A value that is only spaces is as empty as no value. */
function notBlank(control: { value: unknown }): ValidationErrors | null {
  return typeof control.value === 'string' && control.value.trim() ? null : { required: true };
}

/**
 * Records a dose of a medication order: the patient's allergies first, then given (with the dose)
 * or held, refused or not available (with the reason). With active allergies on record, the nurse
 * ticks that they checked them; the API asks for that too.
 */
@Component({
  selector: 'app-record-dose-dialog',
  imports: [
    ReactiveFormsModule,
    AllergyWarning,
    FormField,
    HlmButtonImports,
    HlmCheckboxImports,
    HlmDialogImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSelectImports,
    HlmTextareaImports,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-dialog [state]="open() ? 'open' : 'closed'" (closed)="closed.emit()">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="sm:max-w-xl">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>Record dose</h2>
          @if (order(); as o) {
            <p hlmDialogDescription>
              <span class="font-medium text-foreground">{{ o.medicationName }}</span>
              {{ o.dose }} · {{ o.route }} · {{ o.frequency }}
            </p>
          }
        </hlm-dialog-header>

        <ui-allergy-warning
          [allergies]="allergies()"
          [matches]="order()?.allergyMatches ?? []"
          [medication]="order()?.medicationName ?? null"
        />

        <form
          id="record-dose-form"
          class="flex flex-col gap-4"
          [formGroup]="form"
          (ngSubmit)="save()"
          novalidate
        >
          <ui-form-field label="Outcome" for="dose-outcome" required>
            <hlm-select formControlName="outcome" [itemToString]="outcomeLabel">
              <hlm-select-trigger buttonId="dose-outcome" class="w-full sm:w-64">
                <hlm-select-value placeholder="Choose" />
              </hlm-select-trigger>
              <hlm-select-content *hlmSelectPortal>
                @for (option of outcomes; track option.value) {
                  <hlm-select-item [value]="option.value">{{ option.label }}</hlm-select-item>
                }
              </hlm-select-content>
            </hlm-select>
          </ui-form-field>

          @if (given()) {
            <ui-form-field
              label="Dose given"
              for="dose-given"
              hint="As ordered, unless you gave less"
              required
            >
              <input hlmInput id="dose-given" class="sm:w-64" formControlName="doseGiven" />
            </ui-form-field>
          }

          <ui-form-field
            [label]="given() ? 'Notes' : 'Reason not given'"
            for="dose-notes"
            [hint]="given() ? '' : 'e.g. BP 85/50, held as ordered; patient vomiting'"
            [required]="!given()"
          >
            <textarea hlmTextarea id="dose-notes" rows="3" formControlName="notes"></textarea>
          </ui-form-field>

          @if (allergies().length > 0) {
            <div class="flex items-start gap-3">
              <hlm-checkbox
                class="mt-0.5"
                inputId="dose-allergies-checked"
                formControlName="allergiesChecked"
                [attr.aria-invalid]="allergyCheckMissing() ? 'true' : null"
                aria-describedby="dose-allergies-error"
              />
              <label
                hlmLabel
                for="dose-allergies-checked"
                class="flex flex-col items-start gap-0.5"
              >
                <span>I checked the allergies above against this medication</span>
              </label>
            </div>
            @if (allergyCheckMissing()) {
              <p id="dose-allergies-error" class="-mt-2 text-sm text-destructive">
                Confirm you checked the allergies.
              </p>
            }
          }

          @if (formError(); as message) {
            <p class="text-sm text-destructive" role="alert">{{ message }}</p>
          }
        </form>
        <hlm-dialog-footer>
          <button hlmBtn variant="outline" type="button" (click)="closed.emit()">Cancel</button>
          <button hlmBtn type="submit" form="record-dose-form" [disabled]="saving()">
            {{ saving() ? 'Saving…' : 'Record dose' }}
          </button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class RecordDoseDialog {
  private readonly api = inject(Api);

  readonly encounterId = input.required<string>();
  /** The order the dose is due under. */
  readonly order = input<MarOrderResponse | null>(null);
  /** The patient's active allergies (the encounter's `activeAllergies`). */
  readonly allergies = input<readonly EncounterAllergyRef[]>([]);
  readonly open = input(false);
  readonly closed = output();
  readonly saved = output<MedicationAdministrationResponse>();

  protected readonly outcomes = MEDICATION_OUTCOMES;
  protected readonly outcomeLabel = (value: string) => labelOf(MEDICATION_OUTCOMES, value);
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);
  private readonly submitted = signal(false);

  protected readonly form = new FormGroup({
    outcome: new FormControl<MedicationOutcome>('given', {
      nonNullable: true,
      validators: Validators.required,
    }),
    doseGiven: new FormControl('', {
      nonNullable: true,
      validators: [notBlank, Validators.maxLength(200)],
    }),
    notes: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
    allergiesChecked: new FormControl(false, { nonNullable: true }),
  });

  private readonly outcome = toSignal(this.form.controls.outcome.valueChanges, {
    initialValue: this.form.controls.outcome.value,
  });
  private readonly allergiesChecked = toSignal(this.form.controls.allergiesChecked.valueChanges, {
    initialValue: false,
  });

  protected readonly given = computed(() => this.outcome() === 'given');
  protected readonly allergyCheckMissing = computed(
    () => this.submitted() && this.allergies().length > 0 && !this.allergiesChecked(),
  );

  constructor() {
    // A fresh form every time it opens, with the ordered dose filled in.
    effect(() => {
      if (this.open()) {
        const order = this.order();
        untracked(() => {
          this.form.reset({
            outcome: 'given',
            doseGiven: order?.dose ?? '',
            notes: '',
            allergiesChecked: false,
          });
          this.submitted.set(false);
          this.formError.set(null);
          this.setGiven(true);
        });
      }
    });
    this.form.controls.outcome.valueChanges.subscribe((outcome) =>
      this.setGiven(outcome === 'given'),
    );
  }

  /** A dose given needs the amount; one not given needs the reason. */
  private setGiven(given: boolean): void {
    const { doseGiven, notes } = this.form.controls;
    doseGiven.setValidators(given ? [notBlank, Validators.maxLength(200)] : []);
    notes.setValidators(
      given ? [Validators.maxLength(500)] : [notBlank, Validators.maxLength(500)],
    );
    doseGiven.updateValueAndValidity({ emitEvent: false });
    notes.updateValueAndValidity({ emitEvent: false });
  }

  protected async save(): Promise<void> {
    this.form.markAllAsTouched();
    this.submitted.set(true);
    const order = this.order();
    const { outcome, doseGiven, notes, allergiesChecked } = this.form.getRawValue();
    if (this.form.invalid || this.allergyCheckMissing() || !order || this.saving()) {
      return;
    }
    this.formError.set(null);
    this.saving.set(true);
    try {
      const saved = await this.api.invoke(recordMedicationAdministration, {
        encounterId: this.encounterId(),
        body: {
          orderId: order.orderId,
          outcome,
          doseGiven: outcome === 'given' ? doseGiven.trim() : null,
          notes: notes.trim() || null,
          allergiesChecked: this.allergies().length > 0 && allergiesChecked,
          administeredAt: null,
        },
      });
      toast.success(outcome === 'given' ? 'Dose recorded' : 'Missed dose recorded', {
        description: `${order.medicationName}: ${labelOf(MEDICATION_OUTCOMES, outcome).toLowerCase()}`,
      });
      this.saved.emit(saved);
    } catch (error) {
      this.showProblem(toProblem(error));
    } finally {
      this.saving.set(false);
    }
  }

  private showProblem(problem: Problem): void {
    let shown = false;
    for (const [field, messages] of Object.entries(problem.errors ?? {})) {
      const control = this.form.get(field);
      if (control && messages[0]) {
        control.setErrors({ server: messages[0] });
        shown = true;
      }
    }
    if (!shown) {
      this.formError.set(problem.detail ?? "Couldn't record the dose.");
    }
  }
}
