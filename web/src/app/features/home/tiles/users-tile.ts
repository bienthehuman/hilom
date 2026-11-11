import { ChangeDetectionStrategy, Component, computed, inject, resource } from '@angular/core';
import { DashboardTile, Stat } from '@hilom/ui/dashboard-tile';

import { Api } from '../../../api/api';
import { listUsers } from '../../../api/fn/access/list-users';
import { tileError } from './tile-error';

/** Sign-in accounts by state. Locked accounts are called out, since someone is waiting on them. */
@Component({
  selector: 'app-users-tile',
  imports: [DashboardTile, Stat],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'contents' },
  template: `
    <ui-dashboard-tile
      title="Users"
      description="Sign-in accounts."
      link="/admin/users"
      linkLabel="Open users"
      [loading]="counts.isLoading() && !counts.hasValue()"
      [error]="error()"
      (retry)="counts.reload()"
    >
      @if (counts.hasValue()) {
        <div class="grid grid-cols-3 gap-4">
          <ui-stat [value]="counts.value().active" label="Active" />
          <ui-stat
            [value]="counts.value().locked"
            label="Locked out"
            [tone]="counts.value().locked > 0 ? 'warning' : 'default'"
          />
          <ui-stat [value]="counts.value().disabled" label="Disabled" />
        </div>
      }
    </ui-dashboard-tile>
  `,
})
export class UsersTile {
  private readonly api = inject(Api);

  /** One count per state: each request asks for a single row and reads the total. */
  protected readonly counts = resource({
    loader: async () => {
      const total = async (status: 'active' | 'locked' | 'disabled') =>
        (await this.api.invoke(listUsers, { status, pageSize: 1 })).total;
      const [active, locked, disabled] = await Promise.all([
        total('active'),
        total('locked'),
        total('disabled'),
      ]);
      return { active, locked, disabled };
    },
  });
  protected readonly error = computed(() => tileError(this.counts.error()));
}
