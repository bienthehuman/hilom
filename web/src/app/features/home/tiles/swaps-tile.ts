import { ChangeDetectionStrategy, Component, computed, inject, resource } from '@angular/core';
import { DashboardTile, Stat } from '@hilom/ui/dashboard-tile';
import { EmptyState } from '@hilom/ui/empty-state';
import { provideIcons } from '@ng-icons/core';
import { lucideArrowLeftRight } from '@ng-icons/lucide';

import { Api } from '../../../api/api';
import { listMySwaps } from '../../../api/fn/workforce/list-my-swaps';
import { formatMonthDay } from '../../../core/format/date-time';
import { tileError } from './tile-error';

/** Swap requests from colleagues that wait for the signed-in person's answer. */
@Component({
  selector: 'app-swaps-tile',
  imports: [DashboardTile, Stat, EmptyState],
  providers: [provideIcons({ lucideArrowLeftRight })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'contents' },
  template: `
    <ui-dashboard-tile
      title="Swap inbox"
      description="Colleagues asking to swap shifts with you."
      link="/workforce/swaps"
      linkLabel="Open shift swaps"
      [loading]="swaps.isLoading() && !swaps.hasValue()"
      [error]="error()"
      (retry)="swaps.reload()"
    >
      @if (waiting().length === 0) {
        <ui-empty-state
          icon="lucideArrowLeftRight"
          title="Nothing waiting for you"
          description="Requests to swap shifts with you appear here."
        />
      } @else {
        <ui-stat
          [value]="waiting().length"
          [label]="waiting().length === 1 ? 'Request to answer' : 'Requests to answer'"
          tone="warning"
        />
        <ul class="divide-y" aria-label="Swap requests waiting for you">
          @for (swap of waiting().slice(0, 3); track swap.id) {
            <li class="py-2 text-sm">
              <span class="font-medium"
                >{{ swap.requester.employee.firstName }}
                {{ swap.requester.employee.lastName }}</span
              >
              <span class="text-muted-foreground">
                offers {{ swap.requester.shift.code }} on {{ day(swap.requester.dutyDate) }} for
                your {{ swap.target.shift.code }} on {{ day(swap.target.dutyDate) }}
              </span>
            </li>
          }
        </ul>
      }
    </ui-dashboard-tile>
  `,
})
export class SwapsTile {
  private readonly api = inject(Api);

  protected readonly swaps = resource({
    loader: () => this.api.invoke(listMySwaps, { pageSize: 100 }),
  });
  protected readonly error = computed(() => tileError(this.swaps.error()));
  protected readonly waiting = computed(() =>
    (this.swaps.hasValue() ? this.swaps.value().items : []).filter(
      (s) => s.youAre === 'target' && s.status === 'pending_target',
    ),
  );
  protected readonly day = formatMonthDay;
}
