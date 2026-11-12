import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  linkedSignal,
  resource,
  signal,
} from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { DataTable, type DataTableColumn } from '@hilom/ui/data-table';
import { HlmInputImports } from '@hilom/ui/input';
import { HlmLabelImports } from '@hilom/ui/label';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmSheetImports } from '@hilom/ui/sheet';
import { StatusPill } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCircleAlert,
  lucideDownload,
  lucideSearch,
  lucideShieldAlert,
  lucideShieldCheck,
  lucideX,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { flexRenderComponent } from '@tanstack/angular-table';

import { Api } from '../../../api/api';
import { exportAuditEntries } from '../../../api/fn/access/export-audit-entries';
import {
  listAuditEntries,
  type ListAuditEntries$Params,
} from '../../../api/fn/access/list-audit-entries';
import { listAuditEvents } from '../../../api/fn/access/list-audit-events';
import { listRelatedAuditEntries } from '../../../api/fn/access/list-related-audit-entries';
import { verifyAuditChain } from '../../../api/fn/access/verify-audit-chain';
import { type AuditChainReport } from '../../../api/models/audit-chain-report';
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
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

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
    HlmAlertImports,
    HlmButtonImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSelectImports,
    HlmSheetImports,
  ],
  providers: [
    provideIcons({
      lucideCircleAlert,
      lucideDownload,
      lucideSearch,
      lucideShieldAlert,
      lucideShieldCheck,
      lucideX,
    }),
  ],
  templateUrl: './audit-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditPage {
  private readonly api = inject(Api);
  /**
   * Presets from a link: the home dashboard's critical-events tile (`?severity=critical&from=2026-11-10`)
   * or a patient's access history (`?patientId=…&patientLabel=MRN-000123`).
   */
  private readonly preset = inject(ActivatedRoute).snapshot.queryParamMap;

  // ---- Filters ----
  protected readonly pageSize = PAGE_SIZE;
  protected readonly page = signal(1);
  protected readonly search = signal('');
  protected readonly fromDate = signal(
    /^\d{4}-\d{2}-\d{2}$/.test(this.preset.get('from') ?? '') ? this.preset.get('from')! : '',
  );
  protected readonly toDate = signal('');
  protected readonly event = signal('any');
  protected readonly severity = signal<SeverityFilter>(
    (['critical', 'notice', 'info'] as const).find((v) => v === this.preset.get('severity')) ??
      'any',
  );
  protected readonly patientId = signal<string | null>(
    GUID.test(this.preset.get('patientId') ?? '') ? this.preset.get('patientId') : null,
  );
  /** Display only: names the patient on the filter chip. */
  protected readonly patientLabel = this.preset.get('patientLabel') ?? 'one patient';
  // A patient's entries are mostly views and data changes, so show everything for them.
  protected readonly kind = signal<KindFilter>(this.patientId() ? 'any' : 'event');
  protected readonly correlationId = signal<string | null>(null);
  private searchTimer?: ReturnType<typeof setTimeout>;

  protected readonly kindOptions: readonly { value: KindFilter; label: string }[] = [
    { value: 'event', label: 'Events' },
    { value: 'data_change', label: 'Data changes' },
    { value: 'read', label: 'Record views' },
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
  /** The current filters as API parameters, shared by the list and the CSV export. */
  private readonly filterParams = computed<ListAuditEntries$Params>(() => {
    const correlationId = this.correlationId();
    const event = this.event();
    const severity = this.severity();
    const kind = this.kind();
    return {
      q: this.search() || undefined,
      from: this.fromDate() ? startOfHospitalDay(this.fromDate()) : undefined,
      to: this.toDate() ? endOfHospitalDay(this.toDate()) : undefined,
      event: event === 'any' ? undefined : event,
      severity: severity === 'any' ? undefined : severity,
      kind: kind === 'any' || correlationId ? undefined : kind,
      patientId: this.patientId() ?? undefined,
      correlationId: correlationId ?? undefined,
    };
  });

  protected readonly entries = resource({
    params: () => ({ ...this.filterParams(), page: this.page() }),
    loader: ({ params }): Promise<AuditPageData> =>
      this.api.invoke(listAuditEntries, { ...params, pageSize: PAGE_SIZE }),
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

  // ---- Export ----
  protected readonly exporting = signal(false);

  /** Downloads the filtered log as CSV. The API records the export (critical). */
  protected async exportCsv(): Promise<void> {
    this.exporting.set(true);
    try {
      const csv = await this.api.invoke(exportAuditEntries, this.filterParams());
      const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
      const link = document.createElement('a');
      link.href = url;
      link.download = `audit-log-${new Date().toISOString().slice(0, 10)}.csv`;
      link.click();
      URL.revokeObjectURL(url);
      toast.success('Export downloaded', {
        description: `${this.loaded().total.toLocaleString()} entries. The export is recorded in the log.`,
      });
    } catch (error) {
      toast.error("Couldn't export", {
        description: toProblem(error).detail ?? 'Please try again.',
      });
    } finally {
      this.exporting.set(false);
    }
  }

  // ---- Integrity ----
  protected readonly verifying = signal(false);
  protected readonly verification = signal<AuditChainReport | null>(null);
  protected readonly verifyError = signal<string | null>(null);

  /** Re-checks the hash chain the sealing job builds every minute. */
  protected async verify(): Promise<void> {
    this.verifying.set(true);
    this.verifyError.set(null);
    try {
      this.verification.set(await this.api.invoke(verifyAuditChain));
    } catch (error) {
      this.verification.set(null);
      this.verifyError.set(toProblem(error).detail ?? "Couldn't check the log's integrity.");
    } finally {
      this.verifying.set(false);
    }
  }

  protected readonly breakReason = (reason: string) =>
    reason === 'entry_altered'
      ? 'its contents no longer match the hash taken when it was sealed.'
      : reason === 'link_broken'
        ? "it doesn't link to the entry before it, so an entry was removed, inserted or re-sealed."
        : 'it is marked sealed but has no hash.';

  // ---- Details ----
  protected readonly selected = signal<AuditEntryResponse | null>(null);

  /** The other entries from the selected entry's request, e.g. an event's data changes. */
  protected readonly related = resource({
    params: () => this.selected()?.id,
    loader: ({ params }) => this.api.invoke(listRelatedAuditEntries, { id: params }),
  });

  protected readonly relatedError = computed(() =>
    this.related.error()
      ? (toProblem(this.related.error()).detail ?? "Couldn't load related entries.")
      : null,
  );

  protected readonly titleOf = (entry: AuditEntryResponse) =>
    entry.kind === 'data_change'
      ? `${entry.entityType ?? 'Record'} ${operationOf(entry)}`
      : entry.label;
  protected readonly formatDateTime = formatDateTime;

  protected readonly selectedTitle = computed(() => {
    const entry = this.selected();
    return entry ? this.titleOf(entry) : '';
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
