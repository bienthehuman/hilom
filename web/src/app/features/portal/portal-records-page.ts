import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  resource,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { EmptyState } from '@hilom/ui/empty-state';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { StatusPill } from '@hilom/ui/status';
import { VitalValue, worstFlag } from '@hilom/ui/vital-value';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft,
  lucideCalendarDays,
  lucideCircleAlert,
  lucideClipboardList,
  lucideHeartPulse,
} from '@ng-icons/lucide';

import { Api } from '../../api/api';
import { getPortalRecords } from '../../api/fn/clinical/get-portal-records';
import { listMyLinks } from '../../api/fn/patients/list-my-links';
import { type PortalAppointmentResponse } from '../../api/models/portal-appointment-response';
import { type PortalPhysicianRef } from '../../api/models/portal-physician-ref';
import { type PortalVitalsResponse } from '../../api/models/portal-vitals-response';
import {
  formatDateTime,
  formatLongDay,
  formatTimeRange,
  hospitalDateOf,
} from '../../core/format/date-time';
import { toProblem } from '../../core/http/problem';
import { DISCHARGE_DISPOSITIONS, ENCOUNTER_TYPES, labelOf } from '../clinical/encounter-labels';
import { formatPatientName } from '../patients/patient-labels';

interface Reading {
  label: string;
  value: string;
  unit?: string;
  flag?: Parameters<typeof worstFlag>[0];
}

/**
 * What a family member with "can see records" may read about a linked patient: visits and discharge
 * summaries, vital signs and appointments. Nothing else is offered, because the API sends nothing else.
 */
@Component({
  selector: 'app-portal-records-page',
  imports: [
    RouterLink,
    NgIcon,
    EmptyState,
    StatusPill,
    VitalValue,
    HlmAlertImports,
    HlmButtonImports,
    HlmSkeletonImports,
  ],
  providers: [
    provideIcons({
      lucideArrowLeft,
      lucideCalendarDays,
      lucideCircleAlert,
      lucideClipboardList,
      lucideHeartPulse,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a hlmBtn variant="ghost" class="mb-2 -ml-3" routerLink="/portal/patients">
      <ng-icon name="lucideArrowLeft" aria-hidden="true" data-icon="inline-start" />Patients
    </a>
    <h1 class="mb-1 text-2xl font-semibold tracking-tight">Records</h1>
    <p class="mb-6 text-muted-foreground">{{ patientName() }}</p>

    @if (loadError(); as message) {
      <div hlmAlert variant="destructive">
        <ng-icon name="lucideCircleAlert" aria-hidden="true" />
        <h2 hlmAlertTitle>Couldn't load these records</h2>
        <p hlmAlertDescription>{{ message }}</p>
        <div hlmAlertAction>
          <button hlmBtn variant="outline" type="button" (click)="records.reload()">
            Try again
          </button>
        </div>
      </div>
    } @else if (!records.hasValue()) {
      <div class="flex flex-col gap-3" aria-busy="true">
        <div hlmSkeleton class="h-28 w-full rounded-lg"></div>
        <div hlmSkeleton class="h-28 w-full rounded-lg"></div>
      </div>
    } @else {
      <section class="mb-8" aria-labelledby="records-visits">
        <h2 id="records-visits" class="mb-3 text-lg font-medium">Visits and discharge</h2>
        @if (records.value().visits.length === 0) {
          <div class="rounded-lg border bg-card p-6">
            <ui-empty-state
              icon="lucideClipboardList"
              title="No visits yet"
              description="Visit and discharge summaries appear here after the patient is seen."
            />
          </div>
        } @else {
          <ul class="flex flex-col gap-3" aria-label="Visits">
            @for (v of records.value().visits; track v.id) {
              <li class="rounded-lg border bg-card p-4">
                <div class="flex flex-wrap items-center justify-between gap-2">
                  <p class="font-medium">{{ visitType(v.type) }}</p>
                  <ui-status-pill kind="encounter" [status]="v.status" />
                </div>
                <p class="text-sm text-muted-foreground">
                  <span data-numeric>{{ formatDateTime(v.startedAt) }}</span>
                  @if (v.endedAt) {
                    to <span data-numeric>{{ formatDateTime(v.endedAt) }}</span>
                  }
                  · {{ doctor(v.physician) }}
                </p>

                <h3 class="mt-3 text-sm font-medium text-muted-foreground">Reason for the visit</h3>
                <p>{{ v.chiefComplaint }}</p>

                @if (v.diagnosisSummary) {
                  <h3 class="mt-3 text-sm font-medium text-muted-foreground">Summary</h3>
                  <p class="whitespace-pre-line">{{ v.diagnosisSummary }}</p>
                }

                @if (v.dischargeSummary) {
                  <h3 class="mt-3 text-sm font-medium text-muted-foreground">Discharge summary</h3>
                  <p class="whitespace-pre-line">{{ v.dischargeSummary }}</p>
                  @if (v.dischargeDisposition) {
                    <p class="mt-1 text-sm text-muted-foreground">
                      Went: {{ disposition(v.dischargeDisposition) }}
                    </p>
                  }
                }
              </li>
            }
          </ul>
        }
      </section>

      <section class="mb-8" aria-labelledby="records-vitals">
        <h2 id="records-vitals" class="mb-3 text-lg font-medium">Vital signs</h2>
        @if (records.value().vitals.length === 0) {
          <div class="rounded-lg border bg-card p-6">
            <ui-empty-state
              icon="lucideHeartPulse"
              title="No vital signs yet"
              description="Temperature, blood pressure and other checks appear here once they are taken."
            />
          </div>
        } @else {
          <ul class="flex flex-col gap-3" aria-label="Vital signs">
            @for (set of records.value().vitals; track set.id) {
              <li class="rounded-lg border bg-card p-4">
                <p class="mb-2 font-medium" data-numeric>{{ formatDateTime(set.recordedAt) }}</p>
                <dl class="grid grid-cols-2 gap-x-4 gap-y-2">
                  @for (r of readings(set); track r.label) {
                    <div>
                      <dt class="text-sm text-muted-foreground">{{ r.label }}</dt>
                      <dd class="m-0">
                        <ui-vital-value [value]="r.value" [unit]="r.unit" [flag]="r.flag" />
                      </dd>
                    </div>
                  }
                </dl>
              </li>
            }
          </ul>
        }
      </section>

      <section class="mb-8" aria-labelledby="records-appointments">
        <h2 id="records-appointments" class="mb-3 text-lg font-medium">Appointments</h2>
        @if (records.value().appointments.length === 0) {
          <div class="rounded-lg border bg-card p-6">
            <ui-empty-state
              icon="lucideCalendarDays"
              title="No appointments"
              description="Booked visits, upcoming and from the last year, appear here."
            />
          </div>
        } @else {
          <ul class="flex flex-col gap-3" aria-label="Appointments">
            @for (a of records.value().appointments; track a.id) {
              <li class="rounded-lg border bg-card p-4">
                <div class="flex flex-wrap items-center justify-between gap-2">
                  <p class="font-medium" data-numeric>{{ when(a) }}</p>
                  <ui-status-pill kind="appointment" [status]="a.status" />
                </div>
                <p>{{ doctor(a.physician) }} · {{ a.physician.specialization }}</p>
                <p class="text-sm text-muted-foreground">{{ a.reason }}</p>
              </li>
            }
          </ul>
        }
      </section>
    }
  `,
})
export class PortalRecordsPage {
  private readonly api = inject(Api);

  /** From the route: `/portal/patients/:patientId/records`. */
  readonly patientId = input.required<string>();

  protected readonly records = resource({
    params: () => this.patientId(),
    loader: ({ params }) => this.api.invoke(getPortalRecords, { patientId: params }),
  });
  private readonly links = resource({ loader: () => this.api.invoke(listMyLinks) });

  protected readonly loadError = computed(() => {
    const error = this.records.error();
    if (!error) {
      return null;
    }
    const problem = toProblem(error);
    return problem.status === 404
      ? "These records aren't available to you. Ask the front desk if you think this is a mistake."
      : (problem.detail ?? 'Please try again in a moment.');
  });

  protected readonly patientName = computed(() => {
    const patient = (this.links.hasValue() ? this.links.value() : []).find(
      (l) => l.patient?.id === this.patientId(),
    )?.patient;
    return patient ? formatPatientName(patient) : '';
  });

  protected readonly formatDateTime = formatDateTime;

  protected visitType(type: string): string {
    return labelOf(ENCOUNTER_TYPES, type);
  }

  protected disposition(value: string): string {
    return labelOf(DISCHARGE_DISPOSITIONS, value);
  }

  protected doctor(physician: PortalPhysicianRef): string {
    return `Dr. ${physician.firstName} ${physician.lastName}`;
  }

  protected when(a: PortalAppointmentResponse): string {
    return `${formatLongDay(hospitalDateOf(a.startsAt))}, ${formatTimeRange(a.startsAt, a.endsAt)}`;
  }

  /** The values this set has, in plain words. */
  protected readings(v: PortalVitalsResponse): Reading[] {
    const f = v.flags;
    const out: Reading[] = [];
    if (v.temperatureC != null) {
      out.push({
        label: 'Temperature',
        value: String(v.temperatureC),
        unit: '°C',
        flag: f.temperatureC,
      });
    }
    if (v.systolicBp != null && v.diastolicBp != null) {
      out.push({
        label: 'Blood pressure',
        value: `${v.systolicBp}/${v.diastolicBp}`,
        unit: 'mmHg',
        flag: worstFlag(f.systolicBp, f.diastolicBp),
      });
    }
    if (v.heartRate != null) {
      out.push({ label: 'Heart rate', value: String(v.heartRate), unit: 'bpm', flag: f.heartRate });
    }
    if (v.respiratoryRate != null) {
      out.push({
        label: 'Breathing',
        value: String(v.respiratoryRate),
        unit: '/min',
        flag: f.respiratoryRate,
      });
    }
    if (v.oxygenSaturation != null) {
      out.push({
        label: 'Oxygen',
        value: String(v.oxygenSaturation),
        unit: '%',
        flag: f.oxygenSaturation,
      });
    }
    if (v.painScore != null) {
      out.push({ label: 'Pain', value: `${v.painScore}/10` });
    }
    if (v.weightKg != null) {
      out.push({ label: 'Weight', value: String(v.weightKg), unit: 'kg' });
    }
    if (v.heightCm != null) {
      out.push({ label: 'Height', value: String(v.heightCm), unit: 'cm' });
    }
    return out;
  }
}
