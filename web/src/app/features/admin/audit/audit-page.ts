import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  linkedSignal,
  resource,
  signal,
} from '@angular/core';
import { HlmButtonImports } from '@hilom/ui/button';
import { DataTable, type DataTableColumn } from '@hilom/ui/data-table';
import { HlmInputImports } from '@hilom/ui/input';
import { HlmLabelImports } from '@hilom/ui/label';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmSheetImports } from '@hilom/ui/sheet';
import { StatusPill } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSearch, lucideX } from '@ng-icons/lucide';
import { flexRenderComponent } from '@tanstack/angular-table';

import { Api } from '../../../api/api';
import { listAuditEntries } from '../../../api/fn/access/list-audit-entries';
import { listAuditEvents } from '../../../api/fn/access/list-audit-events';
import { type AuditEntryResponse } from '../../../api/models/audit-entry-response';
import { type AuditKind } from '../../../api/models/audit-kind';
import { type AuditSeverity } from '../../../api/models/audit-severity';
import { formatDateTime } from '../../../core/format/date-time';
import { toProblem } from '../../../core/http/problem';
import { endOfHospitalDay, startOfHospitalDay } from './audit-dates';
import {
  AuditActorCell,
  AuditEventCell,
  AuditOpenCell,
  AuditStatusCell,
  operationOf,
} from './audit-cells';

const PAGE_SIZE = 50;

interface AuditPageData {
  items: AuditEntryResponse[];
  total: number;
}

type KindFilter = AuditKind | 'any';
type SeverityFilter = AuditSeverity | 'any';

/** The audit log: who did what, when, from where. Read-only; entries can't be edited or deleted. */
@Component({
  selector: 'app-audit-page',
  imports: [
    NgIcon,
    PageHeader,
    DataTable,
    StatusPill,
    HlmButtonImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSelectImports,
    HlmSheetImports,
  ],
  providers: [provideIcons({ lucideSearch, lucideX })],
  templateUrl: './audit-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditPage {
  private readonly api = inject(Api);

  // ---- Filters ----
  protected readonly pageSize = PAGE_SIZE;
  protected readonly page = signal(1);
  protected readonly search = signal('');
  protected readonly fromDate = signal('');
  protected readonly toDate = signal('');
  protected readonly event = signal('any');
  protected readonly severity = signal<SeverityFilter>('any');
  protected readonly kind = signal<KindFilter>('event');
  protected readonly correlationId = signal<string | null>(null);
  private searchTimer?: ReturnType<typeof setTimeout>;

  protected readonly kindOptions: readonly { value: KindFilter; label: string }[] = [
    { value: 'event', label: 'Events' },
    { value: 'data_change', label: 'Data changes' },
    { value: 'any', label: 'Everything' },
  ];
  protected readonly severityOptions: readonly { value: SeverityFilter; label: string }[] = [
    { value: 'any', label: 'Any severity' },
    { value: 'critical', label: 'Critical' },
    { value: 'notice', label: 'Notice' },
    { value: 'info', label: 'Info' },
  ];

  protected readonly catalog = resource({ loader: () => this.api.invoke(listAuditEvents) });
  protected readonly eventOptions = computed(() => [
    { value: 'any', label: 'All events' },
    ...(this.catalog.hasValue() ? this.catalog.value() : [])
      .filter((e) => e.key !== 'data.changed')
      .map((e) => ({ value: e.key, label: e.label }))
      .sort((a, b) => a.label.localeCompare(b.label)),
  ]);

  protected readonly labelOf =
    (options: readonly { value: string; label: string }[]) => (value: string) =>
      options.find((o) => o.value === value)?.label ?? value;
  protected readonly kindLabel = this.labelOf(this.kindOptions);
  protected readonly severityLabel = this.labelOf(this.severityOptions);
  protected readonly eventLabel = (value: string) => this.labelOf(this.eventOptions())(value);

  // ---- Data ----
  protected readonly entries = resource({
    params: () => ({
      page: this.page(),
      q: this.search(),
      from: this.fromDate(),
      to: this.toDate(),
      event: this.event(),
      severity: this.severity(),
      kind: this.kind(),
      correlationId: this.correlationId(),
    }),
    loader: ({ params }): Promise<AuditPageData> =>
      this.api.invoke(listAuditEntries, {
        page: params.page,
        pageSize: PAGE_SIZE,
        q: params.q || undefined,
        from: params.from ? startOfHospitalDay(params.from) : undefined,
        to: params.to ? endOfHospitalDay(params.to) : undefined,
        event: params.event === 'any' ? undefined : params.event,
        severity: params.severity === 'any' ? undefined : params.severity,
        kind: params.kind === 'any' || params.correlationId ? undefined : params.kind,
        correlationId: params.correlationId ?? undefined,
      }),
  });

  protected readonly loaded = linkedSignal<AuditPageData | undefined, AuditPageData>({
    source: () => (this.entries.hasValue() ? this.entries.value() : undefined),
    computation: (next, previous) => next ?? previous?.value ?? { items: [], total: 0 },
  });

  protected readonly loadError = computed(() =>
    this.entries.error()
      ? (toProblem(this.entries.error()).detail ?? "Couldn't load the audit log.")
      : null,
  );

  protected readonly columns: DataTableColumn<AuditEntryResponse>[] = [
    {
      id: 'occurredAt',
      header: 'When',
      cell: ({ row }) => formatDateTime(row.original.occurredAt),
    },
    {
      id: 'actor',
      header: 'Who',
      cell: ({ row }) => flexRenderComponent(AuditActorCell, { inputs: { entry: row.original } }),
    },
    {
      id: 'event',
      header: 'What',
      cell: ({ row }) => flexRenderComponent(AuditEventCell, { inputs: { entry: row.original } }),
    },
    {
      id: 'status',
      header: 'Severity',
      cell: ({ row }) => flexRenderComponent(AuditStatusCell, { inputs: { entry: row.original } }),
    },
    {
      id: 'open',
      header: () => '',
      cell: ({ row }) =>
        flexRenderComponent(AuditOpenCell, {
          inputs: { entry: row.original, time: formatDateTime(row.original.occurredAt) },
          outputs: { open: (entry: AuditEntryResponse) => this.selected.set(entry) },
        }),
    },
  ];

  protected readonly rowId = (entry: AuditEntryResponse) => entry.id;

  // ---- Filter handlers ----
  protected onSearch(value: string): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.set(this.search, value.trim()), 300);
  }

  protected set<T>(filter: { set: (value: T) => void }, value: T): void {
    filter.set(value);
    this.page.set(1);
  }

  protected showRelated(entry: AuditEntryResponse): void {
    this.selected.set(null);
    this.set(this.correlationId, entry.correlationId);
  }

  // ---- Details ----
  protected readonly selected = signal<AuditEntryResponse | null>(null);
  protected readonly formatDateTime = formatDateTime;

  protected readonly selectedTitle = computed(() => {
    const entry = this.selected();
    if (!entry) {
      return '';
    }
    return entry.kind === 'data_change'
      ? `${entry.entityType ?? 'Record'} ${operationOf(entry)}`
      : entry.label;
  });

  /** Data changes as rows of field, before, after. */
  protected readonly changeRows = computed(() => {
    const changes = this.selected()?.changes as
      Record<string, [unknown, unknown]> | null | undefined;
    return changes
      ? Object.entries(changes).map(([field, [before, after]]) => ({
          field,
          before: display(before),
          after: display(after),
        }))
      : [];
  });

  protected readonly detailsJson = computed(() => {
    const details = this.selected()?.details;
    return details ? JSON.stringify(details, null, 2) : null;
  });
}

function display(value: unknown): string {
  if (value === null || value === undefined) {
    return '—';
  }
  return typeof value === 'string' ? value : JSON.stringify(value);
}
