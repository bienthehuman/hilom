import { ChangeDetectionStrategy, Component, computed, inject, resource } from '@angular/core';
import { DashboardTile, Stat } from '@hilom/ui/dashboard-tile';
import { EmptyState } from '@hilom/ui/empty-state';
import { provideIcons } from '@ng-icons/core';
import { lucideLink } from '@ng-icons/lucide';

import { Api } from '../../../api/api';
import { listLinks } from '../../../api/fn/patients/list-links';
import { formatDateTime } from '../../../core/format/date-time';
import { tileError } from './tile-error';

/** Portal accounts asking to be linked to a patient, waiting for the front desk to verify. */
@Component({
  selector: 'app-pending-links-tile',
  imports: [DashboardTile, Stat, EmptyState],
  providers: [provideIcons({ lucideLink })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'contents' },
  template: `
    <ui-dashboard-tile
      title="Pending link requests"
      description="Portal accounts waiting to be verified."
      link="/link-requests"
      linkLabel="Review requests"
      [loading]="links.isLoading() && !links.hasValue()"
      [error]="error()"
      (retry)="links.reload()"
    >
      @if (total() === 0) {
        <ui-empty-state
          icon="lucideLink"
          title="No requests waiting"
          description="New requests to link a portal account to a patient appear here."
        />
      } @else {
        <ui-stat [value]="total()" label="Waiting for review" tone="warning" />
        <ul class="divide-y" aria-label="Requests waiting for review">
          @for (link of items(); track link.id) {
            <li class="flex flex-wrap items-baseline justify-between gap-x-3 py-2 text-sm">
              <span class="min-w-0">
                <span class="font-medium">{{ link.requester.displayName }}</span>
                <span class="text-muted-foreground"> for {{ link.requestedPatientName }}</span>
              </span>
              <span class="text-xs text-muted-foreground" data-numeric>{{
                when(link.requestedAt)
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
export class PendingLinksTile {
  private readonly api = inject(Api);

  protected readonly links = resource({
    loader: () => this.api.invoke(listLinks, { status: 'pending', pageSize: 5 }),
  });
  protected readonly error = computed(() => tileError(this.links.error()));
  protected readonly total = computed(() => (this.links.hasValue() ? this.links.value().total : 0));
  protected readonly items = computed(() =>
    this.links.hasValue() ? this.links.value().items : [],
  );
  protected readonly when = (iso: string) => formatDateTime(iso);
}
