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
import { RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmAlertDialogImports } from '@hilom/ui/alert-dialog';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { EmptyState } from '@hilom/ui/empty-state';
import { PatientHeader } from '@hilom/ui/patient-header';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { HlmTabsImports } from '@hilom/ui/tabs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft,
  lucideBedDouble,
  lucideCircleAlert,
  lucideClipboardList,
  lucideContact,
  lucidePill,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { closeEncounter } from '../../api/fn/clinical/close-encounter';
import { getEncounter } from '../../api/fn/clinical/get-encounter';
import { type EncounterResponse } from '../../api/models/encounter-response';
import { HasPermission } from '../../core/auth/has-permission.directive';
import { hospitalToday } from '../../core/format/date-time';
import { toProblem } from '../../core/http/problem';
import { EncounterSummaryCard } from './encounter-summary-card';
import { EncounterNotes } from './encounter-notes';
import { EncounterVitals } from './encounter-vitals';
import { ENCOUNTER_TYPES, labelOf } from './encounter-labels';

interface Section {
  id: string;
  label: string;
  icon: string;
  empty: string;
}

/** Sections recorded on an encounter in later slices: orders (3.7), MAR (3.8). */
const RECORD_SECTIONS: readonly Section[] = [
  {
    id: 'orders',
    label: 'Orders',
    icon: 'lucideClipboardList',
    empty: "The physician's medication, lab, diet and nursing orders appear here.",
  },
  {
    id: 'mar',
    label: 'MAR',
    icon: 'lucidePill',
    empty: 'Each dose given, held or refused against a medication order appears here.',
  },
];

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
    EmptyState,
    EncounterSummaryCard,
    EncounterVitals,
    EncounterNotes,
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
      lucideBedDouble,
      lucideCircleAlert,
      lucideClipboardList,
      lucideContact,
      lucidePill,
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
          @if (e.canManage) {
            <button actions hlmBtn type="button" (click)="confirmClose.set(true)">
              Close encounter
            </button>
          }
        </ui-patient-header>
        <p class="mb-6 text-sm text-muted-foreground">
          {{ typeLabel() }}
          @if (e.status !== 'open') {
            · {{ e.status === 'closed' ? 'Closed' : 'Voided' }}
          }
        </p>

        <hlm-tabs tab="summary">
          <hlm-tabs-list aria-label="Encounter sections" class="mb-4">
            <button hlmTabsTrigger="summary" type="button">Summary</button>
            <button hlmTabsTrigger="vitals" type="button">Vitals</button>
            <button hlmTabsTrigger="notes" type="button">Notes</button>
            @for (section of sections; track section.id) {
              <button [hlmTabsTrigger]="section.id" type="button">{{ section.label }}</button>
            }
          </hlm-tabs-list>
          <div hlmTabsContent="summary">
            <app-encounter-summary-card [encounter]="e" (changed)="current.set($event)" />
          </div>
          <div hlmTabsContent="vitals">
            <app-encounter-vitals [encounterId]="e.id" [canRecord]="e.canRecordVitals" />
          </div>
          <div hlmTabsContent="notes">
            <app-encounter-notes [encounterId]="e.id" [canWrite]="e.canWriteNotes" />
          </div>
          @for (section of sections; track section.id) {
            <div [hlmTabsContent]="section.id" class="rounded-lg border bg-card">
              <ui-empty-state
                [icon]="section.icon"
                [title]="'Nothing recorded yet'"
                [description]="section.empty"
              />
            </div>
          }
        </hlm-tabs>

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

  /** From the route. */
  readonly id = input.required<string>();

  protected readonly today = hospitalToday();
  protected readonly sections = RECORD_SECTIONS;
  protected readonly confirmClose = signal(false);
  protected readonly closing = signal(false);

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
}
