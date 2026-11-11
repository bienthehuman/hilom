import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  resource,
  signal,
  untracked,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { HlmAlertImports } from '@hilom/ui/alert';
import { BedPicker } from '@hilom/ui/bed-picker';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { EmptyState } from '@hilom/ui/empty-state';
import { FormField } from '@hilom/ui/form-field';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBedDouble, lucideCircleAlert } from '@ng-icons/lucide';

import { Api } from '../../api/api';
import { listTransferBeds } from '../../api/fn/clinical/list-transfer-beds';
import { transferPatient } from '../../api/fn/clinical/transfer-patient';
import { type EncounterResponse } from '../../api/models/encounter-response';
import { type TransferResponse } from '../../api/models/transfer-response';
import { toProblem } from '../../core/http/problem';

const REASON_MAX = 500;

/** Problems that mean the chosen bed can't be used; the bed list is reloaded. */
const BED_PROBLEMS = new Set(['bed_taken', 'bed_not_available', 'same_bed']);

/**
 * Move an admitted patient to another free bed, on any ward, with a reason. The current bed is
 * closed and the next one opened together; ward access moves with the patient.
 */
@Component({
  selector: 'app-transfer-dialog',
  imports: [
    ReactiveFormsModule,
    NgIcon,
    BedPicker,
    EmptyState,
    FormField,
    HlmAlertImports,
    HlmButtonImports,
    HlmDialogImports,
    HlmSkeletonImports,
    HlmTextareaImports,
  ],
  providers: [provideIcons({ lucideBedDouble, lucideCircleAlert })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-dialog [state]="open() ? 'open' : 'closed'" (closed)="closed.emit()">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="sm:max-w-2xl">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>Transfer patient</h2>
          <p hlmDialogDescription>
            Now in <span data-numeric>{{ currentBed() }}</span
            >. Choose a free bed on any ward.
          </p>
        </hlm-dialog-header>
        <form
          id="transfer-form"
          class="flex max-h-[60vh] flex-col gap-4 overflow-y-auto"
          [formGroup]="form"
          novalidate
          (ngSubmit)="save()"
        >
          <div class="flex flex-col gap-3">
            <p class="text-sm font-medium">New bed</p>
            @if (beds.error()) {
              <div hlmAlert variant="destructive">
                <ng-icon name="lucideCircleAlert" aria-hidden="true" />
                <h3 hlmAlertTitle>Couldn't load free beds</h3>
                <div hlmAlertAction>
                  <button hlmBtn variant="outline" size="sm" type="button" (click)="beds.reload()">
                    Try again
                  </button>
                </div>
              </div>
            } @else if (!beds.hasValue()) {
              <div class="flex flex-col gap-3" aria-busy="true">
                <div hlmSkeleton class="h-5 w-48"></div>
                <div hlmSkeleton class="h-11 w-full"></div>
              </div>
            } @else if (wardList().length === 0) {
              <ui-empty-state
                icon="lucideBedDouble"
                title="No free beds"
                description="Every active bed is taken. A bed frees up when a patient is discharged or moved."
              />
            } @else {
              <ui-bed-picker
                [wards]="wardList()"
                [(value)]="bedId"
                [describedBy]="bedError() ? 'transfer-bed-error' : undefined"
              />
            }
            @if (bedError(); as message) {
              <p id="transfer-bed-error" class="text-sm text-destructive" role="alert">
                {{ message }}
              </p>
            }
          </div>
          <ui-form-field
            label="Reason for transfer"
            for="transfer-reason"
            hint="e.g. needs cardiac monitoring, isolation, step-down care"
            required
          >
            <textarea hlmTextarea id="transfer-reason" rows="2" formControlName="reason"></textarea>
          </ui-form-field>
          @if (formError(); as message) {
            <p class="text-sm text-destructive" role="alert">{{ message }}</p>
          }
        </form>
        <hlm-dialog-footer>
          <button hlmBtn variant="outline" type="button" (click)="closed.emit()">Cancel</button>
          <button hlmBtn type="submit" form="transfer-form" [disabled]="saving()">
            {{ saving() ? 'Transferring…' : 'Transfer patient' }}
          </button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class TransferDialog {
  private readonly api = inject(Api);

  readonly encounter = input.required<EncounterResponse>();
  readonly open = input(false);
  readonly closed = output();
  readonly moved = output<TransferResponse>();

  protected readonly bedId = signal<string | null>(null);
  protected readonly bedError = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly form = new FormGroup({
    reason: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(REASON_MAX)],
    }),
  });
  private readonly reason = this.form.controls.reason;

  protected readonly beds = resource({
    params: () => (this.open() ? this.encounter().id : undefined),
    loader: ({ params }) => this.api.invoke(listTransferBeds, { id: params }),
  });
  protected readonly wardList = computed(() => (this.beds.hasValue() ? this.beds.value() : []));

  protected readonly currentBed = computed(() => {
    const admission = this.encounter().admission;
    return admission ? `${admission.ward.code} · ${admission.bed.label}` : '';
  });

  constructor() {
    // A fresh form every time it opens.
    effect(() => {
      if (this.open()) {
        untracked(() => {
          this.bedId.set(null);
          this.bedError.set(null);
          this.formError.set(null);
          this.reason.reset('');
        });
      }
    });
  }

  protected async save(): Promise<void> {
    const admission = this.encounter().admission;
    const bedId = this.bedId();
    if (!this.reason.value.trim()) {
      this.reason.setValue('');
    }
    this.reason.markAsTouched();
    this.bedError.set(bedId ? null : 'Choose a free bed.');
    if (!admission || !bedId || this.reason.invalid || this.saving()) {
      return;
    }
    this.formError.set(null);
    this.saving.set(true);
    try {
      const moved = await this.api.invoke(transferPatient, {
        id: this.encounter().id,
        body: { bedId, reason: this.reason.value.trim(), version: admission.version },
      });
      this.moved.emit(moved);
    } catch (error) {
      const problem = toProblem(error);
      if (problem.code && BED_PROBLEMS.has(problem.code)) {
        this.bedId.set(null);
        this.bedError.set(problem.detail ?? 'Choose another free bed.');
        this.beds.reload();
      } else if (problem.errors?.['reason']?.[0]) {
        this.reason.setErrors({ server: problem.errors['reason'][0] });
      } else {
        this.formError.set(
          problem.status === 409
            ? 'This stay changed meanwhile (moved or discharged). Close this and check the latest.'
            : (problem.detail ?? "Couldn't transfer the patient."),
        );
      }
    } finally {
      this.saving.set(false);
    }
  }
}
