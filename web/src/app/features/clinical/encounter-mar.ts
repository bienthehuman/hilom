import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  linkedSignal,
  resource,
  signal,
} from '@angular/core';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { ConfirmAction } from '@hilom/ui/confirm-action';
import { EmptyState } from '@hilom/ui/empty-state';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { StatusPill } from '@hilom/ui/status';
import { VoidedPill } from '@hilom/ui/voided';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucidePill, lucidePlus } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { listMedicationAdministrations } from '../../api/fn/clinical/list-medication-administrations';
import { voidMedicationAdministration } from '../../api/fn/clinical/void-medication-administration';
import { type EncounterAllergyRef } from '../../api/models/encounter-allergy-ref';
import { type MarOrderResponse } from '../../api/models/mar-order-response';
import { type MedicationAdministrationResponse } from '../../api/models/medication-administration-response';
import { type VoidedResponse } from '../../api/models/voided-response';
import { formatDateTime } from '../../core/format/date-time';
import { toProblem } from '../../core/http/problem';
import { RecordDoseDialog } from './record-dose-dialog';

/**
 * The encounter's MAR tab: each medication order (active ones first) with the doses recorded
 * against it, newest first. Nurses record a dose from an active order; the dialog shows the
 * patient's allergies first. A dose entered in error is voided with a reason and stays, struck
 * through.
 */
@Component({
  selector: 'app-encounter-mar',
  imports: [
    NgIcon,
    EmptyState,
    StatusPill,
    VoidedPill,
    ConfirmAction,
    RecordDoseDialog,
    HlmAlertImports,
    HlmButtonImports,
    HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideCircleAlert, lucidePill, lucidePlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    @if (loadError(); as message) {
      <div hlmAlert variant="destructive">
        <ng-icon name="lucideCircleAlert" aria-hidden="true" />
        <h3 hlmAlertTitle>Couldn't load the MAR</h3>
        <p hlmAlertDescription>{{ message }}</p>
        <div hlmAlertAction>
          <button hlmBtn variant="outline" size="sm" type="button" (click)="mar.reload()">
            Try again
          </button>
        </div>
      </div>
    } @else if (list() === undefined) {
      <div class="flex flex-col gap-3" aria-busy="true">
        <div hlmSkeleton class="h-32 w-full rounded-lg"></div>
        <div hlmSkeleton class="h-32 w-full rounded-lg"></div>
      </div>
    } @else if (list()!.length === 0) {
      <div class="rounded-lg border bg-card">
        <ui-empty-state
          icon="lucidePill"
          title="No medication orders"
          description="When a physician orders a medication, it appears here, and each dose given, held or refused is recorded against it."
        />
      </div>
    } @else {
      <ol class="flex flex-col gap-3" aria-label="Medication orders, active first">
        @for (o of list(); track o.orderId) {
          <li
            class="rounded-lg border bg-card p-4"
            [class.text-muted-foreground]="o.status !== 'active'"
          >
            <article [attr.aria-labelledby]="'mar-heading-' + o.orderId">
              <header class="mb-3 flex flex-wrap items-start justify-between gap-2">
                <div class="min-w-0">
                  <h3 [id]="'mar-heading-' + o.orderId" class="font-semibold">
                    {{ o.medicationName }}
                    <span class="font-normal" data-numeric>{{ o.dose }}</span>
                  </h3>
                  <p class="text-sm">{{ o.route }} · {{ o.frequency }}</p>
                  <p class="text-xs text-muted-foreground">
                    Ordered <span data-numeric>{{ formatDateTime(o.orderedAt) }}</span> · Dr.
                    {{ o.orderedBy }}
                  </p>
                </div>
                <div class="flex flex-wrap items-center gap-2">
                  @if (o.allergyMatches.length > 0) {
                    <span
                      class="inline-flex items-center gap-1 text-xs font-semibold text-destructive"
                    >
                      <ng-icon name="lucideCircleAlert" aria-hidden="true" />Allergy:
                      {{ o.allergyMatches.join(', ') }}
                    </span>
                  }
                  <ui-status-pill kind="order" [status]="o.status" />
                  @if (o.canRecord) {
                    <button
                      hlmBtn
                      size="sm"
                      type="button"
                      [attr.aria-label]="'Record dose of ' + o.medicationName"
                      (click)="recordFor.set(o)"
                    >
                      <ng-icon
                        name="lucidePlus"
                        aria-hidden="true"
                        data-icon="inline-start"
                      />Record dose
                    </button>
                  }
                </div>
              </header>
              @if (o.details) {
                <p class="mb-3 max-w-[72ch] text-sm whitespace-pre-line">{{ o.details }}</p>
              }

              @if (o.administrations.length === 0) {
                <p class="border-t pt-3 text-sm text-muted-foreground">No doses recorded yet.</p>
              } @else {
                <ol
                  class="flex flex-col divide-y border-t"
                  [attr.aria-label]="'Doses of ' + o.medicationName + ', newest first'"
                >
                  @for (d of o.administrations; track d.id) {
                    <li
                      class="flex flex-wrap items-start justify-between gap-2 py-2 text-sm"
                      [class.text-muted-foreground]="d.voided"
                    >
                      <div class="min-w-0" [class.line-through]="d.voided">
                        <p>
                          <span class="font-medium" data-numeric>{{
                            formatDateTime(d.administeredAt)
                          }}</span>
                          · {{ d.administeredBy }}
                          @if (d.doseGiven) {
                            · <span data-numeric>{{ d.doseGiven }}</span>
                          }
                        </p>
                        @if (d.notes) {
                          <p class="max-w-[72ch] whitespace-pre-line">
                            @if (d.outcome !== 'given') {
                              <span class="font-medium">Reason:</span>
                            }
                            {{ d.notes }}
                          </p>
                        }
                      </div>
                      <div class="flex flex-wrap items-center gap-1">
                        <ui-status-pill kind="medicationOutcome" [status]="d.outcome" />
                        @if (d.voided; as voided) {
                          <ui-voided-pill [reason]="voided.reason" [by]="voidedBy(voided)" />
                        }
                        @if (d.canVoid) {
                          <button
                            hlmBtn
                            variant="ghost"
                            size="sm"
                            type="button"
                            [attr.aria-label]="
                              'Void dose of ' +
                              o.medicationName +
                              ' at ' +
                              formatDateTime(d.administeredAt)
                            "
                            [uiConfirmAction]="voidDose(d)"
                            confirmTitle="Void this dose?"
                            confirmDescription="Use this for a dose entered in error, such as on the wrong patient or order. It stays on the record, struck through, with your reason."
                            confirmLabel="Void dose"
                            reasonHint="e.g. recorded on the wrong order"
                            requireReason
                            destructive
                          >
                            Void
                          </button>
                        }
                      </div>
                    </li>
                  }
                </ol>
              }
            </article>
          </li>
        }
      </ol>
    }

    <app-record-dose-dialog
      [encounterId]="encounterId()"
      [order]="recordFor()"
      [allergies]="allergies()"
      [open]="recordFor() !== null"
      (closed)="recordFor.set(null)"
      (saved)="onSaved()"
    />
  `,
})
export class EncounterMar {
  private readonly api = inject(Api);

  readonly encounterId = input.required<string>();
  /** The patient's active allergies (the encounter's `activeAllergies`), shown before recording. */
  readonly allergies = input<readonly EncounterAllergyRef[]>([]);
  /** Reloads the list when it changes, e.g. the encounter's status after a discharge. */
  readonly refreshKey = input<unknown>(null);

  protected readonly formatDateTime = formatDateTime;
  protected readonly recordFor = signal<MarOrderResponse | null>(null);

  protected readonly mar = resource({
    params: () => ({ encounterId: this.encounterId(), refresh: this.refreshKey() }),
    loader: ({ params }) =>
      this.api.invoke(listMedicationAdministrations, { encounterId: params.encounterId }),
  });

  /** Kept while reloading after a change, so the list doesn't flash back to skeletons. */
  protected readonly list = linkedSignal<
    MarOrderResponse[] | undefined,
    MarOrderResponse[] | undefined
  >({
    source: () => (this.mar.hasValue() ? this.mar.value() : undefined),
    computation: (next, previous) => next ?? (this.mar.error() ? undefined : previous?.value),
  });

  protected readonly loadError = computed(() =>
    this.mar.error() ? (toProblem(this.mar.error()).detail ?? "Couldn't load the MAR.") : null,
  );

  protected voidedBy(voided: VoidedResponse): string {
    return [voided.by, formatDateTime(voided.at)].filter(Boolean).join(', ');
  }

  protected onSaved(): void {
    this.recordFor.set(null);
    this.mar.reload();
  }

  protected voidDose(d: MedicationAdministrationResponse): (reason: string) => Promise<void> {
    return async (reason) => {
      await this.api.invoke(voidMedicationAdministration, {
        encounterId: this.encounterId(),
        id: d.id,
        body: { reason },
      });
      toast.success('Dose voided');
      this.mar.reload();
    };
  }
}
