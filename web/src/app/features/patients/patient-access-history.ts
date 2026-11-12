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
import { HlmButtonImports } from '@hilom/ui/button';
import { DataTable, type DataTableColumn } from '@hilom/ui/data-table';
import { HlmLabelImports } from '@hilom/ui/label';
import { HlmSelectImports } from '@hilom/ui/select';
import { StatusPill } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideScrollText } from '@ng-icons/lucide';
import { flexRenderComponent } from '@tanstack/angular-table';

import { Api } from '../../api/api';
import { listPatientAccessHistory } from '../../api/fn/patients/list-patient-access-history';
import { type AuditKind } from '../../api/models/audit-kind';
import { type PatientAccessEntryResponse } from '../../api/models/patient-access-entry-response';
import { HasPermission } from '../../core/auth/has-permission.directive';
import { formatDateTime } from '../../core/format/date-time';
import { toProblem } from '../../core/http/problem';

const PAGE_SIZE = 25;

type KindFilter = AuditKind | 'any';

interface HistoryPage {
  items: PatientAccessEntryResponse[];
  total: number;
}

/** "Patient record viewed", or for a data change "PatientAllergy changed" with the field names (never values). */
@Component({
  selector: 'app-access-history-what-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex max-w-md min-w-48 flex-col whitespace-normal">
      <span class="text-foreground">{{ title() }}</span>
      @if (entry().changedFields.length > 0) {
        <span class="text-xs text-muted-foreground">{{ entry().changedFields.join(', ') }}</span>
      }
    </div>
  `,
})
export class AccessHistoryWhatCell {
  readonly entry = input.required<PatientAccessEntryResponse>();
  protected readonly title = computed(() => accessHistoryTitle(this.entry()));
}

/** Who acted, and whether through a patient portal link. */
@Component({
  selector: 'app-access-history-who-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col">
      <span class="text-foreground">{{ entry().userName ?? entry().userEmail ?? 'System' }}</span>
      @if (entry().viaPortal) {
        <span class="text-xs text-muted-foreground">Through a patient portal link</span>
      } @else if (entry().userName && entry().userEmail) {
        <span class="text-xs text-muted-foreground">{{ entry().userEmail }}</span>
      }
    </div>
  `,
})
export class AccessHistoryWhoCell {
  readonly entry = input.required<PatientAccessEntryResponse>();
}

@Component({
  selector: 'app-access-history-outcome-cell',
  imports: [StatusPill],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<ui-status-pill kind="auditOutcome" [status]="entry().outcome" />`,
})
export class AccessHistoryOutcomeCell {
  readonly entry = input.required<PatientAccessEntryResponse>();
}

export function accessHistoryTitle(entry: PatientAccessEntryResponse): string {
  if (entry.kind !== 'data_change') {
    return entry.label;
  }
  const verb =
    entry.operation === 'added' ? 'created' : entry.operation === 'deleted' ? 'deleted' : 'changed';
  return `${entry.entityType ?? 'Record'} ${verb}`;
}

/**
 * The patient's "Access history" tab: who opened or changed this patient's records, and when
 * (`patients.access_history.view`). Opening it is itself recorded as reading the audit log.
 */
@Component({
  selector: 'app-patient-access-history',
  imports: [
    RouterLink,
    NgIcon,
    DataTable,
    HasPermission,
    HlmButtonImports,
    HlmLabelImports,
    HlmSelectImports,
  ],
  providers: [provideIcons({ lucideScrollText })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-4 flex flex-wrap items-end justify-between gap-3">
      <div class="flex w-48 flex-col gap-1.5">
        <label hlmLabel for="access-history-kind">Show</label>
        <hlm-select
          [value]="kind()"
          [itemToString]="kindLabel"
          (valueChange)="setKind($any($event))"
        >
          <hlm-select-trigger buttonId="access-history-kind" class="w-full">
            <hlm-select-value />
          </hlm-select-trigger>
          <hlm-select-content *hlmSelectPortal>
            @for (option of kindOptions; track option.value) {
              <hlm-select-item [value]="option.value">{{ option.label }}</hlm-select-item>
            }
          </hlm-select-content>
        </hlm-select>
      </div>
      <a
        *appHasPermission="'access.audit.view'"
        hlmBtn
        variant="outline"
        routerLink="/admin/audit"
        [queryParams]="{ patientId: patientId(), patientLabel: patientLabel() }"
      >
        <ng-icon name="lucideScrollText" aria-hidden="true" data-icon="inline-start" />Open in audit
        log
      </a>
    </div>

    <ui-data-table
      caption="Access history"
      emptyTitle="Nothing recorded yet"
      emptyDescription="Views and changes of this patient's records appear here."
      emptyIcon="lucideScrollText"
      [columns]="columns"
      [rows]="loaded().items"
      [total]="loaded().total"
      [page]="page()"
      [pageSize]="pageSize"
      [loading]="history.isLoading()"
      [error]="loadError()"
      [rowId]="rowId"
      (pageChange)="page.set($event)"
      (retry)="history.reload()"
    />
  `,
})
export class PatientAccessHistory {
  private readonly api = inject(Api);

  readonly patientId = input.required<string>();
  /** Shown on the audit log's patient filter, e.g. the MRN. */
  readonly patientLabel = input.required<string>();

  protected readonly pageSize = PAGE_SIZE;
  protected readonly page = signal(1);
  protected readonly kind = signal<KindFilter>('any');

  protected readonly kindOptions: readonly { value: KindFilter; label: string }[] = [
    { value: 'any', label: 'Everything' },
    { value: 'read', label: 'Views' },
    { value: 'event', label: 'Events' },
    { value: 'data_change', label: 'Data changes' },
  ];
  protected readonly kindLabel = (value: string) =>
    this.kindOptions.find((o) => o.value === value)?.label ?? value;

  protected readonly history = resource({
    params: () => ({ id: this.patientId(), page: this.page(), kind: this.kind() }),
    loader: ({ params }): Promise<HistoryPage> =>
      this.api.invoke(listPatientAccessHistory, {
        id: params.id,
        page: params.page,
        pageSize: PAGE_SIZE,
        kind: params.kind === 'any' ? undefined : params.kind,
      }),
  });

  protected readonly loaded = linkedSignal<HistoryPage | undefined, HistoryPage>({
    source: () => (this.history.hasValue() ? this.history.value() : undefined),
    computation: (next, previous) => next ?? previous?.value ?? { items: [], total: 0 },
  });

  protected readonly loadError = computed(() =>
    this.history.error()
      ? (toProblem(this.history.error()).detail ?? "Couldn't load the access history.")
      : null,
  );

  protected readonly columns: DataTableColumn<PatientAccessEntryResponse>[] = [
    {
      id: 'occurredAt',
      header: 'When',
      cell: ({ row }) => formatDateTime(row.original.occurredAt),
    },
    {
      id: 'who',
      header: 'Who',
      cell: ({ row }) =>
        flexRenderComponent(AccessHistoryWhoCell, { inputs: { entry: row.original } }),
    },
    {
      id: 'what',
      header: 'What',
      cell: ({ row }) =>
        flexRenderComponent(AccessHistoryWhatCell, { inputs: { entry: row.original } }),
    },
    {
      id: 'outcome',
      header: 'Outcome',
      cell: ({ row }) =>
        flexRenderComponent(AccessHistoryOutcomeCell, { inputs: { entry: row.original } }),
    },
  ];

  protected readonly rowId = (entry: PatientAccessEntryResponse) => entry.id;

  protected setKind(value: KindFilter): void {
    this.kind.set(value);
    this.page.set(1);
  }
}
