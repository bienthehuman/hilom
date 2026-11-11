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
import { EmptyState } from '@hilom/ui/empty-state';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { HlmTableImports } from '@hilom/ui/table';
import {
  type ChartColor,
  type TrendBand,
  TrendChart,
  type TrendSeries,
} from '@hilom/ui/trend-chart';
import { flagText, VitalValue, type VitalFlagValue, worstFlag } from '@hilom/ui/vital-value';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideActivity, lucideCircleAlert, lucidePlus } from '@ng-icons/lucide';

import { Api } from '../../api/api';
import { getVitalRanges } from '../../api/fn/clinical/get-vital-ranges';
import { listVitalSigns } from '../../api/fn/clinical/list-vital-signs';
import { type VitalRangeResponse } from '../../api/models/vital-range-response';
import { type VitalRangesResponse } from '../../api/models/vital-ranges-response';
import { type VitalSignsResponse } from '../../api/models/vital-signs-response';
import { formatDateTime } from '../../core/format/date-time';
import { toProblem } from '../../core/http/problem';
import { RecordVitalsDialog } from './record-vitals-dialog';

type FlagKey = keyof VitalSignsResponse['flags'];
type ValueKey = FlagKey;

interface Measure {
  id: string;
  label: string;
  unit: string;
  lines: { name: string; key: ValueKey; color: ChartColor }[];
  /** The range drawn as the normal band; blood pressure has two, so none. */
  band?: keyof VitalRangesResponse;
}

const MEASURES: readonly Measure[] = [
  {
    id: 'temperature',
    label: 'Temperature',
    unit: '°C',
    lines: [{ name: 'Temperature', key: 'temperatureC', color: 'chart-1' }],
    band: 'temperatureC',
  },
  {
    id: 'bp',
    label: 'Blood pressure',
    unit: 'mmHg',
    lines: [
      { name: 'Systolic', key: 'systolicBp', color: 'chart-1' },
      { name: 'Diastolic', key: 'diastolicBp', color: 'chart-4' },
    ],
  },
  {
    id: 'heart-rate',
    label: 'Heart rate',
    unit: 'bpm',
    lines: [{ name: 'Heart rate', key: 'heartRate', color: 'chart-1' }],
    band: 'heartRate',
  },
  {
    id: 'respiratory-rate',
    label: 'Respiratory rate',
    unit: '/min',
    lines: [{ name: 'Respiratory rate', key: 'respiratoryRate', color: 'chart-1' }],
    band: 'respiratoryRate',
  },
  {
    id: 'spo2',
    label: 'SpO₂',
    unit: '%',
    lines: [{ name: 'SpO₂', key: 'oxygenSaturation', color: 'chart-1' }],
    band: 'oxygenSaturation',
  },
];

function toneOf(flag: VitalFlagValue | null | undefined): 'warning' | 'danger' | null {
  return flag === 'critical_low' || flag === 'critical_high'
    ? 'danger'
    : flag === 'low' || flag === 'high'
      ? 'warning'
      : null;
}

/**
 * The encounter's Vitals tab: a trend chart of one measure at a time, the readings as a table (the
 * record; the chart only helps), and recording a new set for those who may.
 */
@Component({
  selector: 'app-encounter-vitals',
  imports: [
    NgIcon,
    EmptyState,
    TrendChart,
    VitalValue,
    RecordVitalsDialog,
    HlmAlertImports,
    HlmButtonImports,
    HlmSkeletonImports,
    HlmTableImports,
  ],
  providers: [provideIcons({ lucideActivity, lucideCircleAlert, lucidePlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    @if (canRecord()) {
      <div class="flex justify-end">
        <button hlmBtn type="button" (click)="recording.set(true)">
          <ng-icon name="lucidePlus" aria-hidden="true" data-icon="inline-start" />Record vital
          signs
        </button>
      </div>
    }

    @if (loadError(); as message) {
      <div hlmAlert variant="destructive">
        <ng-icon name="lucideCircleAlert" aria-hidden="true" />
        <h3 hlmAlertTitle>Couldn't load vital signs</h3>
        <p hlmAlertDescription>{{ message }}</p>
        <div hlmAlertAction>
          <button hlmBtn variant="outline" size="sm" type="button" (click)="vitals.reload()">
            Try again
          </button>
        </div>
      </div>
    } @else if (readings() === undefined) {
      <div class="flex flex-col gap-2" aria-busy="true">
        <div hlmSkeleton class="h-56 w-full rounded-lg"></div>
        <div hlmSkeleton class="h-24 w-full rounded-lg"></div>
      </div>
    } @else if (readings()!.length === 0) {
      <div class="rounded-lg border bg-card">
        <ui-empty-state
          icon="lucideActivity"
          title="No vital signs yet"
          [description]="
            canRecord()
              ? 'Record the first set to start the trend.'
              : 'Vital signs recorded during this encounter appear here.'
          "
        />
      </div>
    } @else {
      <section class="rounded-lg border bg-card p-4" aria-labelledby="vitals-trend-heading">
        <div class="mb-3 flex flex-wrap items-center justify-between gap-3">
          <h3 id="vitals-trend-heading" class="font-semibold">{{ measure().label }} trend</h3>
          <div class="flex flex-wrap gap-1" role="group" aria-label="Measure to chart">
            @for (m of measures; track m.id) {
              <button
                hlmBtn
                size="sm"
                [variant]="m.id === measure().id ? 'secondary' : 'ghost'"
                type="button"
                [attr.aria-pressed]="m.id === measure().id"
                (click)="measureId.set(m.id)"
              >
                {{ m.label }}
              </button>
            }
          </div>
        </div>
        @if (series().length > 0) {
          <ui-trend-chart
            [label]="measure().label"
            [unit]="measure().unit"
            [series]="series()"
            [band]="band()"
            [formatTime]="formatDateTime"
          />
        } @else {
          <p class="py-8 text-center text-sm text-muted-foreground">
            No {{ measure().label.toLowerCase() }} readings yet.
          </p>
        }
      </section>

      <div class="overflow-x-auto rounded-lg border bg-card">
        <table hlmTable>
          <caption class="sr-only">
            Vital signs, newest first
          </caption>
          <thead hlmTHead>
            <tr hlmTr>
              <th hlmTh scope="col">Time</th>
              <th hlmTh scope="col">Temp</th>
              <th hlmTh scope="col">BP</th>
              <th hlmTh scope="col">HR</th>
              <th hlmTh scope="col">RR</th>
              <th hlmTh scope="col">SpO₂</th>
              <th hlmTh scope="col">Pain</th>
              <th hlmTh scope="col">Weight · Height · BMI</th>
              <th hlmTh scope="col">Recorded by</th>
            </tr>
          </thead>
          <tbody hlmTBody>
            @for (v of readings(); track v.id) {
              <tr hlmTr>
                <td hlmTd class="tabular-nums">{{ formatDateTime(v.recordedAt) }}</td>
                <td hlmTd>
                  @if (v.temperatureC !== null) {
                    <ui-vital-value
                      [value]="v.temperatureC.toFixed(1)"
                      unit="°C"
                      [flag]="v.flags.temperatureC"
                    />
                  } @else {
                    <span class="text-muted-foreground">—</span>
                  }
                </td>
                <td hlmTd>
                  @if (v.systolicBp !== null) {
                    <ui-vital-value
                      [value]="v.systolicBp + '/' + v.diastolicBp"
                      unit="mmHg"
                      [flag]="worstFlag(v.flags.systolicBp, v.flags.diastolicBp)"
                    />
                  } @else {
                    <span class="text-muted-foreground">—</span>
                  }
                </td>
                <td hlmTd>
                  @if (v.heartRate !== null) {
                    <ui-vital-value [value]="v.heartRate" unit="bpm" [flag]="v.flags.heartRate" />
                  } @else {
                    <span class="text-muted-foreground">—</span>
                  }
                </td>
                <td hlmTd>
                  @if (v.respiratoryRate !== null) {
                    <ui-vital-value
                      [value]="v.respiratoryRate"
                      unit="/min"
                      [flag]="v.flags.respiratoryRate"
                    />
                  } @else {
                    <span class="text-muted-foreground">—</span>
                  }
                </td>
                <td hlmTd>
                  @if (v.oxygenSaturation !== null) {
                    <ui-vital-value
                      [value]="v.oxygenSaturation"
                      unit="%"
                      [flag]="v.flags.oxygenSaturation"
                    />
                  } @else {
                    <span class="text-muted-foreground">—</span>
                  }
                </td>
                <td hlmTd class="tabular-nums">
                  {{ v.painScore !== null ? v.painScore + '/10' : '—' }}
                </td>
                <td hlmTd class="tabular-nums">{{ bodyText(v) }}</td>
                <td hlmTd>
                  {{ v.recordedBy }}
                  @if (v.notes) {
                    <p class="max-w-64 text-xs whitespace-normal text-muted-foreground">
                      {{ v.notes }}
                    </p>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }

    <app-record-vitals-dialog
      [encounterId]="encounterId()"
      [open]="recording()"
      (closed)="recording.set(false)"
      (recorded)="onRecorded()"
    />
  `,
})
export class EncounterVitals {
  private readonly api = inject(Api);

  readonly encounterId = input.required<string>();
  readonly canRecord = input(false);

  protected readonly measures = MEASURES;
  protected readonly measureId = signal(MEASURES[0]!.id);
  protected readonly recording = signal(false);
  protected readonly formatDateTime = formatDateTime;
  protected readonly worstFlag = worstFlag;

  protected readonly vitals = resource({
    params: () => this.encounterId(),
    loader: ({ params }) => this.api.invoke(listVitalSigns, { encounterId: params }),
  });
  private readonly ranges = resource({
    loader: () => this.api.invoke(getVitalRanges, {}),
  });

  /** Kept while reloading after recording, so the table doesn't flash back to skeletons. */
  protected readonly readings = linkedSignal<
    VitalSignsResponse[] | undefined,
    VitalSignsResponse[] | undefined
  >({
    source: () => (this.vitals.hasValue() ? this.vitals.value() : undefined),
    computation: (next, previous) => next ?? (this.vitals.error() ? undefined : previous?.value),
  });

  protected readonly loadError = computed(() =>
    this.vitals.error()
      ? (toProblem(this.vitals.error()).detail ?? "Couldn't load vital signs.")
      : null,
  );

  protected readonly measure = computed(
    () => MEASURES.find((m) => m.id === this.measureId()) ?? MEASURES[0]!,
  );

  protected readonly series = computed<TrendSeries[]>(() => {
    const readings = this.readings() ?? [];
    return this.measure()
      .lines.map((line) => ({
        name: line.name,
        color: line.color,
        points: readings.flatMap((v) => {
          const value = v[line.key];
          const flag = v.flags[line.key];
          return value === null
            ? []
            : [{ at: v.recordedAt, value, note: flagText(flag) || null, tone: toneOf(flag) }];
        }),
      }))
      .filter((s) => s.points.length > 0);
  });

  protected readonly band = computed<TrendBand | null>(() => {
    const key = this.measure().band;
    const range: VitalRangeResponse | undefined =
      key && this.ranges.hasValue() ? this.ranges.value()[key] : undefined;
    return range
      ? {
          low: range.normalLow,
          high: range.normalHigh,
          label: `Normal ${range.normalLow}–${range.normalHigh} ${this.measure().unit}`,
        }
      : null;
  });

  protected bodyText(v: VitalSignsResponse): string {
    const parts = [
      v.weightKg !== null ? `${v.weightKg} kg` : null,
      v.heightCm !== null ? `${v.heightCm} cm` : null,
      v.bmi !== null ? `BMI ${v.bmi}` : null,
    ].filter(Boolean);
    return parts.length ? parts.join(' · ') : '—';
  }

  protected onRecorded(): void {
    this.recording.set(false);
    this.vitals.reload();
  }
}
