import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  linkedSignal,
  resource,
  signal,
} from '@angular/core';
import { DataTable, type DataTableColumn } from '@hilom/ui/data-table';
import { HlmLabelImports } from '@hilom/ui/label';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSelectImports } from '@hilom/ui/select';
import { provideIcons } from '@ng-icons/core';
import { lucideStethoscope } from '@ng-icons/lucide';
import { flexRenderComponent } from '@tanstack/angular-table';

import { Api } from '../../api/api';
import { listEncounters } from '../../api/fn/clinical/list-encounters';
import { type EncounterStatus } from '../../api/models/encounter-status';
import { type EncounterSummaryResponse } from '../../api/models/encounter-summary-response';
import { AuthService } from '../../core/auth/auth.service';
import { formatDateTime } from '../../core/format/date-time';
import { toProblem } from '../../core/http/problem';
import { EncounterPatientCell, EncounterStatusCell } from './encounter-cells';
import {
  ENCOUNTER_STATUS_FILTERS,
  ENCOUNTER_TYPES,
  formatPhysician,
  labelOf,
} from './encounter-labels';

const PAGE_SIZE = 25;

type StatusFilter = EncounterStatus | 'any';

interface EncounterPageData {
  items: EncounterSummaryResponse[];
  total: number;
}

/**
 * Encounters the user reaches, newest first: a physician's own patients (as attending), or everyone's
 * for roles with `all`. Open ones by default, the visits still in progress.
 */
@Component({
  selector: 'app-encounters-page',
  imports: [PageHeader, DataTable, HlmLabelImports, HlmSelectImports],
  providers: [provideIcons({ lucideStethoscope })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-6xl">
      <ui-page-header
        title="Encounters"
        [description]="
          seesEveryone()
            ? 'Clinic visits and ward stays, newest first.'
            : 'Clinic visits and ward stays where you are the attending physician.'
        "
      />

      <div class="mb-4 flex flex-col gap-1.5">
        <label hlmLabel for="encounter-status">Status</label>
        <hlm-select
          class="w-44"
          [value]="status()"
          [itemToString]="statusLabel"
          (valueChange)="setStatus($any($event))"
        >
          <hlm-select-trigger buttonId="encounter-status" class="w-full">
            <hlm-select-value />
          </hlm-select-trigger>
          <hlm-select-content *hlmSelectPortal>
            @for (option of statusOptions; track option.value) {
              <hlm-select-item [value]="option.value">{{ option.label }}</hlm-select-item>
            }
          </hlm-select-content>
        </hlm-select>
      </div>

      <ui-data-table
        caption="Encounters"
        [emptyTitle]="status() === 'open' ? 'No open encounters' : 'No encounters'"
        [emptyDescription]="
          status() === 'open'
            ? 'Visits appear here when the front desk checks a patient in.'
            : 'Encounters you can see will appear here.'
        "
        emptyIcon="lucideStethoscope"
        [columns]="columns"
        [rows]="loaded().items"
        [total]="loaded().total"
        [page]="page()"
        [pageSize]="pageSize"
        [loading]="encounters.isLoading()"
        [error]="loadError()"
        [rowId]="rowId"
        (pageChange)="page.set($event)"
        (retry)="encounters.reload()"
      />
    </div>
  `,
})
export class EncountersPage {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);

  protected readonly pageSize = PAGE_SIZE;
  protected readonly page = signal(1);
  protected readonly status = signal<StatusFilter>('open');
  protected readonly statusOptions = ENCOUNTER_STATUS_FILTERS;
  protected readonly statusLabel = (value: StatusFilter) =>
    labelOf(ENCOUNTER_STATUS_FILTERS, value);

  protected readonly seesEveryone = computed(() =>
    this.auth.scopes('clinical.encounters.view').includes('all'),
  );

  protected readonly encounters = resource({
    params: () => ({ page: this.page(), status: this.status() }),
    loader: ({ params }): Promise<EncounterPageData> =>
      this.api.invoke(listEncounters, {
        page: params.page,
        pageSize: PAGE_SIZE,
        status: params.status === 'any' ? undefined : params.status,
      }),
  });

  /** Last loaded page, kept while the next loads so the pager doesn't jump. */
  protected readonly loaded = linkedSignal<EncounterPageData | undefined, EncounterPageData>({
    source: () => (this.encounters.hasValue() ? this.encounters.value() : undefined),
    computation: (next, previous) => next ?? previous?.value ?? { items: [], total: 0 },
  });

  protected readonly loadError = computed(() =>
    this.encounters.error()
      ? (toProblem(this.encounters.error()).detail ?? "Couldn't load encounters.")
      : null,
  );

  protected setStatus(value: StatusFilter): void {
    this.status.set(value);
    this.page.set(1);
  }

  protected readonly rowId = (row: EncounterSummaryResponse) => row.id;

  protected readonly columns: DataTableColumn<EncounterSummaryResponse>[] = [
    {
      id: 'patient',
      header: 'Patient',
      cell: ({ row }) =>
        flexRenderComponent(EncounterPatientCell, { inputs: { encounter: row.original } }),
    },
    {
      id: 'type',
      header: 'Type',
      cell: ({ row }) => labelOf(ENCOUNTER_TYPES, row.original.type),
    },
    {
      id: 'startedAt',
      header: 'Started',
      cell: ({ row }) => formatDateTime(row.original.startedAt),
    },
    {
      id: 'attending',
      header: 'Attending',
      cell: ({ row }) => formatPhysician(row.original.attendingPhysician),
    },
    {
      id: 'chiefComplaint',
      header: 'Chief complaint',
      cell: ({ row }) => row.original.chiefComplaint,
    },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) =>
        flexRenderComponent(EncounterStatusCell, { inputs: { status: row.original.status } }),
    },
  ];
}
