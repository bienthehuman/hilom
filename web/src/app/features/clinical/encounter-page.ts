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
import { Router, RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmAlertDialogImports } from '@hilom/ui/alert-dialog';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { PatientHeader } from '@hilom/ui/patient-header';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { HlmTabsImports } from '@hilom/ui/tabs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft,
  lucideArrowRightLeft,
  lucideBedDouble,
  lucideCircleAlert,
  lucideContact,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { closeEncounter } from '../../api/fn/clinical/close-encounter';
import { getEncounter } from '../../api/fn/clinical/get-encounter';
import { type EncounterResponse } from '../../api/models/encounter-response';
import { type TransferResponse } from '../../api/models/transfer-response';
import { HasPermission } from '../../core/auth/has-permission.directive';
import { hospitalToday } from '../../core/format/date-time';
import { toProblem } from '../../core/http/problem';
import { DischargeDialog } from './discharge-dialog';
import { EncounterSummaryCard } from './encounter-summary-card';
import { EncounterMar } from './encounter-mar';
import { EncounterNotes } from './encounter-notes';
import { EncounterOrders } from './encounter-orders';
import { EncounterVitals } from './encounter-vitals';
import { ENCOUNTER_TYPES, labelOf } from './encounter-labels';
import { TransferDialog } from './transfer-dialog';

/**
 * One encounter: the patient header (with severe allergies), then tabs for the summary and the
 * clinical record. Opening it is logged as a record view. The attending physician can close it.
 */
@Component({
  selector: 'app-encounter-page',
  imports: [
    RouterLink,
    NgIcon,
    PatientHeader,
    EncounterSummaryCard,
    EncounterVitals,
    EncounterNotes,
    EncounterOrders,
    EncounterMar,
    TransferDialog,
    DischargeDialog,
    HasPermission,
    HlmAlertImports,
    HlmAlertDialogImports,
    HlmButtonImports,
    HlmCardImports,
    HlmSkeletonImports,
    HlmTabsImports,
  ],
  providers: [
    provideIcons({
      lucideArrowLeft,
      lucideArrowRightLeft,
      lucideBedDouble,
      lucideCircleAlert,
      lucideContact,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-5xl">
      <a hlmBtn variant="ghost" size="sm" class="-ms-2 mb-2" routerLink="/clinical/encounters">
        <ng-icon name="lucideArrowLeft" aria-hidden="true" data-icon="inline-start" />Encounters
      </a>

      @if (loadProblem(); as problem) {
        <div hlmAlert variant="destructive">
          <ng-icon name="lucideCircleAlert" aria-hidden="true" />
          <h1 hlmAlertTitle>
            {{ problem.status === 404 ? 'Encounter not found' : "Couldn't load this encounter" }}
          </h1>
          <p hlmAlertDescription>
            {{
              problem.status === 404
                ? "This encounter doesn't exist, or it isn't one you can see."
                : (problem.detail ?? 'Please try again.')
            }}
          </p>
          @if (problem.status !== 404) {
            <div hlmAlertAction>
              <button hlmBtn variant="outline" size="sm" type="button" (click)="loaded.reload()">
                Try again
              </button>
            </div>
          }
        </div>
      } @else if (current(); as e) {
        <ui-patient-header
          class="mb-2"
          [patient]="e.patient"
          [today]="today"
          [severeAllergies]="severeAllergies()"
          [location]="location()"
        >
          <a
            *appHasPermission="'patients.view'"
            actions
            hlmBtn
            variant="outline"
            [routerLink]="['/patients', e.patient.id]"
          >
            <ng-icon name="lucideContact" aria-hidden="true" data-icon="inline-start" />Patient
            record
          </a>
          @if (e.admittedAsEncounterId; as stayId) {
            <a actions hlmBtn variant="outline" [routerLink]="['/clinical/encounters', stayId]">
              <ng-icon name="lucideBedDouble" aria-hidden="true" data-icon="inline-start" />Ward
              stay
            </a>
          } @else if (e.canAdmit) {
            <a
              actions
              hlmBtn
              variant="outline"
              routerLink="/clinical/admit"
              [queryParams]="{
                patientId: e.patient.id,
                encounterId: e.id,
                physicianId: e.attendingPhysician.id,
              }"
            >
              <ng-icon name="lucideBedDouble" aria-hidden="true" data-icon="inline-start" />Admit
            </a>
          }
          @if (e.canTransfer) {
            <button actions hlmBtn variant="outline" type="button" (click)="dialog.set('transfer')">
              <ng-icon
                name="lucideArrowRightLeft"
                aria-hidden="true"
                data-icon="inline-start"
              />Transfer
            </button>
          }
          @if (e.canDischarge) {
            <button actions hlmBtn type="button" (click)="dialog.set('discharge')">
              Discharge
            </button>
          }
          <!-- A ward stay ends by discharge, not by closing it. -->
          @if (e.canManage && e.type === 'outpatient') {
            <button actions hlmBtn type="button" (click)="confirmClose.set(true)">
              Close encounter
            </button>
          }
        </ui-patient-header>
        <p class="mb-6 text-sm text-muted-foreground">
          {{ typeLabel() }}
          @if (e.status !== 'open') {
            ·
            {{
              e.admission?.dischargedAt ? 'Discharged' : e.status === 'closed' ? 'Closed' : 'Voided'
            }}
          }
        </p>

        <hlm-tabs tab="summary">
          <hlm-tabs-list aria-label="Encounter sections" class="mb-4">
            <button hlmTabsTrigger="summary" type="button">Summary</button>
            <button hlmTabsTrigger="vitals" type="button">Vitals</button>
            <button hlmTabsTrigger="notes" type="button">Notes</button>
            <button hlmTabsTrigger="orders" type="button">Orders</button>
            <button hlmTabsTrigger="mar" type="button">MAR</button>
          </hlm-tabs-list>
          <div hlmTabsContent="summary">
            <app-encounter-summary-card [encounter]="e" (changed)="current.set($event)" />
          </div>
          <div hlmTabsContent="vitals">
            <app-encounter-vitals [encounterId]="e.id" [canRecord]="e.canRecordVitals" />
          </div>
          <div hlmTabsContent="notes">
            <app-encounter-notes
              [encounterId]="e.id"
              [canWrite]="e.canWriteNotes"
              [refreshKey]="e.status"
            />
          </div>
          <div hlmTabsContent="orders">
            <app-encounter-orders [encounterId]="e.id" [canWrite]="e.canWriteOrders" />
          </div>
          <div hlmTabsContent="mar">
            <app-encounter-mar
              [encounterId]="e.id"
              [allergies]="e.activeAllergies"
              [refreshKey]="e.status"
            />
          </div>
        </hlm-tabs>

        @if (e.type === 'inpatient' && e.admission) {
          <app-transfer-dialog
            [encounter]="e"
            [open]="dialog() === 'transfer'"
            (closed)="dialog.set(null)"
            (moved)="onMoved($event)"
          />
          <app-discharge-dialog
            [encounter]="e"
            [open]="dialog() === 'discharge'"
            (closed)="dialog.set(null)"
            (discharged)="onDischarged($event)"
          />
        }

        <hlm-alert-dialog
          [state]="confirmClose() ? 'open' : 'closed'"
          (closed)="confirmClose.set(false)"
        >
          <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
            <hlm-alert-dialog-header>
              <h2 hlmAlertDialogTitle>Close this encounter?</h2>
              <p hlmAlertDialogDescription>
                Nothing more can be recorded on it afterwards.
                @if (e.appointmentId) {
                  The appointment is marked as completed.
                }
              </p>
            </hlm-alert-dialog-header>
            <hlm-alert-dialog-footer>
              <button hlmAlertDialogCancel type="button">Keep open</button>
              <button hlmAlertDialogAction type="button" [disabled]="closing()" (click)="close()">
                Close encounter
              </button>
            </hlm-alert-dialog-footer>
          </hlm-alert-dialog-content>
        </hlm-alert-dialog>
      } @else {
        <div class="flex flex-col gap-6" aria-busy="true">
          <div hlmSkeleton class="h-16 w-80 rounded-lg"></div>
          <div hlmSkeleton class="h-10 w-96 rounded-lg"></div>
          <div class="grid gap-6 lg:grid-cols-3">
            <div hlmSkeleton class="h-48 w-full rounded-lg lg:col-span-2"></div>
            <div hlmSkeleton class="h-48 w-full rounded-lg"></div>
          </div>
        </div>
      }
    </div>
  `,
})
export class EncounterPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  /** From the route. */
  readonly id = input.required<string>();

  protected readonly today = hospitalToday();
  protected readonly confirmClose = signal(false);
  protected readonly closing = signal(false);
  protected readonly dialog = signal<'transfer' | 'discharge' | null>(null);

  protected readonly loaded = resource({
    params: () => this.id(),
    loader: ({ params }) => this.api.invoke(getEncounter, { id: params }),
  });

  /** The loaded encounter, replaced by the API's answer after a change. */
  protected readonly current = linkedSignal<EncounterResponse | undefined>(() =>
    this.loaded.hasValue() ? this.loaded.value() : undefined,
  );

  protected readonly loadProblem = computed(() =>
    this.loaded.error() ? toProblem(this.loaded.error()) : null,
  );

  protected readonly severeAllergies = computed(() =>
    (this.current()?.activeAllergies ?? []).filter((a) => a.severity === 'severe'),
  );

  /** Where an admitted patient is now, for the header. */
  protected readonly location = computed(() => {
    const admission = this.current()?.admission;
    return admission && !admission.dischargedAt
      ? `${admission.ward.code} · ${admission.bed.label}`
      : null;
  });

  protected readonly typeLabel = computed(() => {
    const e = this.current();
    return e ? labelOf(ENCOUNTER_TYPES, e.type) : '';
  });

  protected async close(): Promise<void> {
    const e = this.current();
    if (!e || this.closing()) {
      return;
    }
    this.closing.set(true);
    try {
      const closed = await this.api.invoke(closeEncounter, {
        id: e.id,
        body: { version: e.version },
      });
      this.current.set(closed);
      this.confirmClose.set(false);
      toast.success('Encounter closed');
    } catch (error) {
      toast.error("Couldn't close the encounter", { description: toProblem(error).detail });
      if (toProblem(error).status === 409) {
        this.loaded.reload();
      }
    } finally {
      this.closing.set(false);
    }
  }

  /** Moved: reload the stay, or leave it when the move took the patient off the caller's wards. */
  protected onMoved(moved: TransferResponse): void {
    this.dialog.set(null);
    const where = `${moved.ward.code} · ${moved.bed.label}`;
    if (moved.canStillView) {
      toast.success('Patient transferred', { description: where });
      this.loaded.reload();
    } else {
      toast.success('Patient transferred', {
        description: `${where}. They're no longer on your wards, so this stay has closed for you.`,
      });
      void this.router.navigateByUrl('/clinical/census');
    }
  }

  protected onDischarged(stay: EncounterResponse): void {
    this.dialog.set(null);
    this.current.set(stay);
    toast.success('Patient discharged', { description: 'The bed is free and the stay is closed.' });
  }
}
