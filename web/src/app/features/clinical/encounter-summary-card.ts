import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { FormField } from '@hilom/ui/form-field';
import { StatusPill } from '@hilom/ui/status';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePencil } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { updateEncounter } from '../../api/fn/clinical/update-encounter';
import { type EncounterResponse } from '../../api/models/encounter-response';
import { formatDateTime } from '../../core/format/date-time';
import { type Problem, toProblem } from '../../core/http/problem';
import {
  DISCHARGE_DISPOSITIONS,
  ENCOUNTER_TYPES,
  formatPhysician,
  labelOf,
} from './encounter-labels';

const CHIEF_COMPLAINT_MAX = 500;
const DIAGNOSIS_MAX = 2000;

/**
 * The encounter's summary tab: when and with whom, the chief complaint and the diagnosis summary.
 * The attending physician (`clinical.encounters.manage`) edits the last two while it's open.
 */
@Component({
  selector: 'app-encounter-summary-card',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    NgIcon,
    FormField,
    StatusPill,
    HlmButtonImports,
    HlmCardImports,
    HlmTextareaImports,
  ],
  providers: [provideIcons({ lucidePencil })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'grid gap-6 lg:grid-cols-3' },
  template: `
    <section hlmCard class="lg:col-span-2" aria-labelledby="visit-summary-heading">
      <div hlmCardHeader>
        <h2 id="visit-summary-heading" hlmCardTitle>{{ summaryTitle() }}</h2>
        @if (encounter().canManage && !editing()) {
          <div hlmCardAction>
            <button hlmBtn variant="outline" size="sm" type="button" (click)="edit()">
              <ng-icon name="lucidePencil" aria-hidden="true" data-icon="inline-start" />Edit
              <span class="sr-only"> {{ summaryTitle().toLowerCase() }}</span>
            </button>
          </div>
        }
      </div>
      <div hlmCardContent>
        @if (editing()) {
          <form
            class="flex flex-col gap-4"
            [formGroup]="form"
            (ngSubmit)="save()"
            novalidate
            [attr.aria-label]="summaryTitle()"
          >
            <ui-form-field label="Chief complaint" for="encounter-complaint" required>
              <textarea
                hlmTextarea
                id="encounter-complaint"
                rows="2"
                formControlName="chiefComplaint"
              ></textarea>
            </ui-form-field>
            <ui-form-field
              label="Diagnosis summary"
              for="encounter-diagnosis"
              hint="Free text. What you found and what happens next."
            >
              <textarea
                hlmTextarea
                id="encounter-diagnosis"
                rows="5"
                formControlName="diagnosisSummary"
              ></textarea>
            </ui-form-field>
            @if (problem(); as p) {
              <p class="text-sm text-destructive" role="alert">{{ p.detail }}</p>
            }
            <div class="flex justify-end gap-2">
              <button hlmBtn variant="outline" type="button" (click)="editing.set(false)">
                Cancel
              </button>
              <button hlmBtn type="submit" [disabled]="saving()">Save summary</button>
            </div>
          </form>
        } @else {
          <dl class="flex flex-col gap-4">
            @if (encounter().admission; as admission) {
              <div>
                <dt class="text-sm text-muted-foreground">Admitting diagnosis</dt>
                <dd class="whitespace-pre-line">{{ admission.admittingDiagnosis }}</dd>
              </div>
            }
            <div>
              <dt class="text-sm text-muted-foreground">Chief complaint</dt>
              <dd class="whitespace-pre-line">{{ encounter().chiefComplaint }}</dd>
            </div>
            <div>
              <dt class="text-sm text-muted-foreground">Diagnosis summary</dt>
              <dd class="whitespace-pre-line">
                @if (encounter().diagnosisSummary; as diagnosis) {
                  {{ diagnosis }}
                } @else {
                  <span class="text-muted-foreground">Not written yet.</span>
                }
              </dd>
            </div>
          </dl>
        }
      </div>
    </section>

    <section hlmCard aria-labelledby="encounter-details-heading">
      <div hlmCardHeader>
        <h2 id="encounter-details-heading" hlmCardTitle>Details</h2>
      </div>
      <dl hlmCardContent class="flex flex-col gap-3">
        <div>
          <dt class="text-sm text-muted-foreground">Status</dt>
          <dd><ui-status-pill kind="encounter" [status]="encounter().status" /></dd>
        </div>
        <div>
          <dt class="text-sm text-muted-foreground">Type</dt>
          <dd>{{ typeLabel() }}</dd>
        </div>
        <div>
          <dt class="text-sm text-muted-foreground">Attending physician</dt>
          <dd>
            {{ physician() }}
            <span class="text-sm text-muted-foreground"
              >· {{ encounter().attendingPhysician.specialization }}</span
            >
          </dd>
        </div>
        @if (encounter().admission; as admission) {
          <div>
            <dt class="text-sm text-muted-foreground">
              {{ admission.dischargedAt ? 'Last ward and bed' : 'Ward and bed' }}
            </dt>
            <dd>
              <span data-numeric>{{ admission.ward.code }}</span> · {{ admission.ward.name }},
              <span data-numeric>{{ admission.bed.label }}</span>
            </dd>
          </div>
          <div>
            <dt class="text-sm text-muted-foreground">Admitted</dt>
            <dd data-numeric>{{ formatDateTime(admission.admittedAt) }}</dd>
          </div>
          @if (admission.dischargedAt; as discharged) {
            <div>
              <dt class="text-sm text-muted-foreground">Discharged</dt>
              <dd>
                <span data-numeric>{{ formatDateTime(discharged) }}</span>
                @if (admission.dischargeDisposition; as disposition) {
                  <span class="block text-sm text-muted-foreground">{{
                    dispositionLabel(disposition)
                  }}</span>
                }
              </dd>
            </div>
          }
          @if (admission.beds.length > 1) {
            <div>
              <dt class="text-sm text-muted-foreground">Bed history</dt>
              <dd>
                <ol class="mt-1 flex flex-col gap-2 border-s ps-3" aria-label="Bed history">
                  @for (move of admission.beds; track move.from) {
                    <li class="text-sm">
                      <span class="font-medium" data-numeric
                        >{{ move.ward.code }} · {{ move.bed.label }}</span
                      >
                      <span class="block text-muted-foreground" data-numeric
                        >From {{ formatDateTime(move.from) }}</span
                      >
                      <span class="block text-muted-foreground">{{ move.reason }}</span>
                    </li>
                  }
                </ol>
              </dd>
            </div>
          }
        }
        @if (encounter().parentEncounterId; as visitId) {
          <div>
            <dt class="text-sm text-muted-foreground">Admitted from</dt>
            <dd>
              <a
                class="underline underline-offset-4"
                [routerLink]="['/clinical/encounters', visitId]"
                >Clinic visit</a
              >
            </dd>
          </div>
        }
        @if (encounter().appointmentStartsAt; as booked) {
          <div>
            <dt class="text-sm text-muted-foreground">Appointment</dt>
            <dd data-numeric>{{ formatDateTime(booked) }}</dd>
          </div>
        }
        <div>
          <dt class="text-sm text-muted-foreground">Started</dt>
          <dd data-numeric>
            {{ formatDateTime(encounter().startedAt) }}
            @if (encounter().openedBy; as by) {
              <span class="text-sm text-muted-foreground">by {{ by }}</span>
            }
          </dd>
        </div>
        @if (encounter().endedAt; as ended) {
          <div>
            <dt class="text-sm text-muted-foreground">Ended</dt>
            <dd data-numeric>{{ formatDateTime(ended) }}</dd>
          </div>
        }
      </dl>
    </section>
  `,
})
export class EncounterSummaryCard {
  private readonly api = inject(Api);

  readonly encounter = input.required<EncounterResponse>();
  /** The encounter after a save; the page shows it. */
  readonly changed = output<EncounterResponse>();

  protected readonly editing = signal(false);
  protected readonly saving = signal(false);
  protected readonly problem = signal<Problem | null>(null);
  protected readonly form = new FormGroup({
    chiefComplaint: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(CHIEF_COMPLAINT_MAX)],
    }),
    diagnosisSummary: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(DIAGNOSIS_MAX)],
    }),
  });

  protected readonly typeLabel = computed(() => labelOf(ENCOUNTER_TYPES, this.encounter().type));
  protected readonly summaryTitle = computed(() =>
    this.encounter().type === 'inpatient' ? 'Stay summary' : 'Visit summary',
  );
  protected readonly physician = computed(() =>
    formatPhysician(this.encounter().attendingPhysician),
  );
  protected readonly formatDateTime = formatDateTime;
  protected readonly dispositionLabel = (value: string) => labelOf(DISCHARGE_DISPOSITIONS, value);

  protected edit(): void {
    const e = this.encounter();
    this.form.reset({
      chiefComplaint: e.chiefComplaint,
      diagnosisSummary: e.diagnosisSummary ?? '',
    });
    this.problem.set(null);
    this.editing.set(true);
  }

  protected async save(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.saving()) {
      return;
    }
    const e = this.encounter();
    const value = this.form.getRawValue();
    this.saving.set(true);
    this.problem.set(null);
    try {
      const updated = await this.api.invoke(updateEncounter, {
        id: e.id,
        body: {
          chiefComplaint: value.chiefComplaint.trim(),
          diagnosisSummary: value.diagnosisSummary.trim() || null,
          version: e.version,
        },
      });
      toast.success(`${this.summaryTitle()} saved`);
      this.editing.set(false);
      this.changed.emit(updated);
    } catch (error) {
      this.problem.set(toProblem(error));
    } finally {
      this.saving.set(false);
    }
  }
}
