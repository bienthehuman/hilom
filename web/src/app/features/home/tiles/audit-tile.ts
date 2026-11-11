import { ChangeDetectionStrategy, Component, computed, inject, resource } from '@angular/core';
import { DashboardTile, Stat } from '@hilom/ui/dashboard-tile';
import { EmptyState } from '@hilom/ui/empty-state';
import { provideIcons } from '@ng-icons/core';
import { lucideShieldCheck } from '@ng-icons/lucide';

import { Api } from '../../../api/api';
import { listAuditEntries } from '../../../api/fn/access/list-audit-entries';
import { type AuditEntryResponse } from '../../../api/models/audit-entry-response';
import { formatDateTime, hospitalDateOf } from '../../../core/format/date-time';
import { tileError } from './tile-error';

/** Critical audit events from the last 24 hours. */
@Component({
  selector: 'app-audit-tile',
  imports: [DashboardTile, Stat, EmptyState],
  providers: [provideIcons({ lucideShieldCheck })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'contents' },
  template: `
    <ui-dashboard-tile
      title="Critical audit events"
      description="The last 24 hours."
      link="/admin/audit"
      [linkQuery]="logQuery"
      linkLabel="Open audit log"
      [loading]="entries.isLoading() && !entries.hasValue()"
      [error]="error()"
      (retry)="entries.reload()"
    >
      @if (total() === 0) {
        <ui-empty-state
          icon="lucideShieldCheck"
          title="No critical events"
          description="Nothing critical was logged in the last 24 hours."
        />
      } @else {
        <ui-stat
          [value]="total()"
          label="Critical events"
          tone="danger"
          hint="in the last 24 hours"
        />
        <ul class="divide-y" aria-label="Latest critical events">
          @for (entry of items(); track entry.id) {
            <li class="flex flex-wrap items-baseline justify-between gap-x-3 py-2 text-sm">
              <span class="min-w-0">
                <span class="font-medium">{{ entry.label }}</span>
                <span class="text-muted-foreground"> · {{ who(entry) }}</span>
              </span>
              <span class="text-xs text-muted-foreground" data-numeric>{{
                when(entry.occurredAt)
              }}</span>
            </li>
          }
        </ul>
        @if (total() > items().length) {
          <p class="text-sm text-muted-foreground">And {{ total() - items().length }} more.</p>
        }
      }
    </ui-dashboard-tile>
  `,
})
export class AuditTile {
  private readonly api = inject(Api);

  protected readonly entries = resource({
    loader: () =>
      this.api.invoke(listAuditEntries, {
        severity: 'critical',
        from: new Date(Date.now() - 24 * 3_600_000).toISOString(),
        pageSize: 5,
      }),
  });
  protected readonly error = computed(() => tileError(this.entries.error()));
  protected readonly total = computed(() =>
    this.entries.hasValue() ? this.entries.value().total : 0,
  );
  protected readonly items = computed(() =>
    this.entries.hasValue() ? this.entries.value().items : [],
  );

  /** The log filtered to critical events since yesterday (the log filters by day, so a little wider than 24 hours). */
  protected readonly logQuery = {
    severity: 'critical',
    from: hospitalDateOf(new Date(Date.now() - 24 * 3_600_000).toISOString()),
  };

  protected readonly when = (iso: string) => formatDateTime(iso);
  protected who(entry: AuditEntryResponse): string {
    return entry.userName ?? entry.attemptedIdentifier ?? 'System';
  }
}
