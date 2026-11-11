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
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { createPhysicianOrder } from '../../api/fn/clinical/create-physician-order';
import { type OrderType } from '../../api/models/order-type';
import { type PhysicianOrderResponse } from '../../api/models/physician-order-response';
import { type Problem, toProblem } from '../../core/http/problem';
import { labelOf, ORDER_TYPES } from './encounter-labels';

/** A value that is only spaces is as empty as no value. */
function notBlank(control: { value: unknown }): ValidationErrors | null {
  return typeof control.value === 'string' && control.value.trim() ? null : { required: true };
}

/**
 * A physician writes an order. For a medication the name, dose, route and frequency are all required,
 * as the API and the database require too; the other kinds need only the order text.
 */
@Component({
  selector: 'app-write-order-dialog',
  imports: [
    ReactiveFormsModule,
    FormField,
    HlmButtonImports,
    HlmDialogImports,
    HlmInputImports,
    HlmSelectImports,
    HlmTextareaImports,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-dialog [state]="open() ? 'open' : 'closed'" (closed)="closed.emit()">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="sm:max-w-2xl">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>New order</h2>
          <p hlmDialogDescription>
            Orders can't be edited or deleted. To stop one, discontinue it with a reason.
          </p>
        </hlm-dialog-header>
        <form
          id="write-order-form"
          class="flex flex-col gap-4"
          [formGroup]="form"
          (ngSubmit)="save()"
          novalidate
        >
          <ui-form-field label="Kind of order" for="order-type" required>
            <hlm-select formControlName="orderType" [itemToString]="typeLabel">
              <hlm-select-trigger buttonId="order-type" class="w-full sm:w-64">
                <hlm-select-value placeholder="Choose" />
              </hlm-select-trigger>
              <hlm-select-content *hlmSelectPortal>
                @for (option of types; track option.value) {
                  <hlm-select-item [value]="option.value">{{ option.label }}</hlm-select-item>
                }
              </hlm-select-content>
            </hlm-select>
          </ui-form-field>

          @if (isMedication()) {
            <div class="grid gap-4 sm:grid-cols-2">
              <ui-form-field
                label="Medication"
                for="order-medication"
                required
                class="sm:col-span-2"
              >
                <input hlmInput id="order-medication" formControlName="medicationName" />
              </ui-form-field>
              <ui-form-field label="Dose" for="order-dose" hint="e.g. 500 mg" required>
                <input hlmInput id="order-dose" formControlName="dose" />
              </ui-form-field>
              <ui-form-field label="Route" for="order-route" hint="e.g. oral, IV" required>
                <input hlmInput id="order-route" formControlName="route" />
              </ui-form-field>
              <ui-form-field
                label="Frequency"
                for="order-frequency"
                hint="e.g. every 8 hours"
                required
                class="sm:col-span-2"
              >
                <input hlmInput id="order-frequency" formControlName="frequency" />
              </ui-form-field>
            </div>
          }

          <ui-form-field
            [label]="isMedication() ? 'Instructions' : 'Order'"
            for="order-details"
            required
          >
            <textarea hlmTextarea id="order-details" rows="4" formControlName="details"></textarea>
          </ui-form-field>

          @if (formError(); as message) {
            <p class="text-sm text-destructive" role="alert">{{ message }}</p>
          }
        </form>
        <hlm-dialog-footer>
          <button hlmBtn variant="outline" type="button" (click)="closed.emit()">Cancel</button>
          <button hlmBtn type="submit" form="write-order-form" [disabled]="saving()">
            {{ saving() ? 'Saving…' : 'Save order' }}
          </button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class WriteOrderDialog {
  private readonly api = inject(Api);

  readonly encounterId = input.required<string>();
  readonly open = input(false);
  readonly closed = output();
  readonly saved = output<PhysicianOrderResponse>();

  protected readonly types = ORDER_TYPES;
  protected readonly typeLabel = (value: string) => labelOf(ORDER_TYPES, value);
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly isMedication = signal(false);

  protected readonly form = new FormGroup({
    orderType: new FormControl<OrderType | null>(null, Validators.required),
    details: new FormControl('', {
      nonNullable: true,
      validators: [notBlank, Validators.maxLength(2_000)],
    }),
    medicationName: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(200)],
    }),
    dose: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(200)] }),
    route: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(200)] }),
    frequency: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(200)] }),
  });

  private readonly medicationControls = [
    this.form.controls.medicationName,
    this.form.controls.dose,
    this.form.controls.route,
    this.form.controls.frequency,
  ];

  constructor() {
    // A fresh form every time it opens.
    effect(() => {
      if (this.open()) {
        untracked(() => {
          this.form.reset();
          this.formError.set(null);
          this.setMedication(false);
        });
      }
    });
    this.form.controls.orderType.valueChanges.subscribe((type) =>
      this.setMedication(type === 'medication'),
    );
  }

  /** The four medication fields are required only for a medication order. */
  private setMedication(on: boolean): void {
    this.isMedication.set(on);
    for (const control of this.medicationControls) {
      control.setValidators(
        on ? [notBlank, Validators.maxLength(200)] : [Validators.maxLength(200)],
      );
      control.updateValueAndValidity({ emitEvent: false });
    }
  }

  protected async save(): Promise<void> {
    this.form.markAllAsTouched();
    const { orderType, details, medicationName, dose, route, frequency } = this.form.getRawValue();
    if (this.form.invalid || !orderType || this.saving()) {
      return;
    }
    this.formError.set(null);
    this.saving.set(true);
    const medication = orderType === 'medication';
    try {
      const saved = await this.api.invoke(createPhysicianOrder, {
        encounterId: this.encounterId(),
        body: {
          orderType,
          details: details.trim(),
          medicationName: medication ? medicationName.trim() : null,
          dose: medication ? dose.trim() : null,
          route: medication ? route.trim() : null,
          frequency: medication ? frequency.trim() : null,
        },
      });
      toast.success('Order saved');
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
      this.formError.set(problem.detail ?? "Couldn't save the order.");
    }
  }
}
