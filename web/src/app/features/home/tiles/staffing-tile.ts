import { ChangeDetectionStrategy, Component, computed, inject, resource } from '@angular/core';
import { DashboardTile, Stat } from '@hilom/ui/dashboard-tile';
import { EmptyState } from '@hilom/ui/empty-state';
import { provideIcons } from '@ng-icons/core';
import { lucideUsers } from '@ng-icons/lucide';

import { Api } from '../../../api/api';
import { getStaffing } from '../../../api/fn/workforce/get-staffing';
import { hospitalToday } from '../../../core/format/date-time';
import { tileError } from './tile-error';

interface WardStaffing {
  id: string;
  code: string;
  name: string;
  onDuty: number;
  /** "D 2 · N 1": people per shift today. */
  byShift: string;
}

/**
 * Who is on duty today, ward by ward, from the ward rosters the user can reach (all wards, or their own).
 * Wards with no one rostered are called out in words.
 */
@Component({
  selector: 'app-staffing-tile',
  imports: [DashboardTile, Stat, EmptyState],
  providers: [provideIcons({ lucideUsers })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'contents' },
  template: `
    <ui-dashboard-tile
      title="Staffing today"
      description="Who is rostered on each ward."
      link="/workforce/roster"
      linkLabel="Open duty roster"
      [loading]="staffing.isLoading() && !staffing.hasValue()"
      [error]="error()"
      (retry)="staffing.reload()"
    >
      @if (wards().length === 0) {
        <ui-empty-state
          icon="lucideUsers"
          title="No wards to show"
          description="Rosters appear here for the wards you reach."
        />
      } @else {
        <div class="flex flex-wrap gap-x-8 gap-y-3">
          <ui-stat [value]="total()" label="On duty today" />
          @if (uncovered() > 0) {
            <ui-stat
              [value]="uncovered()"
              [label]="
                uncovered() === 1 ? 'Ward with no one rostered' : 'Wards with no one rostered'
              "
              tone="danger"
            />
          }
        </div>
        <ul class="divide-y" aria-label="Staffing by ward">
          @for (ward of wards(); track ward.id) {
            <li class="flex flex-wrap items-baseline justify-between gap-x-3 py-2 text-sm">
              <span class="min-w-0"
                ><span class="font-medium" data-numeric>{{ ward.code }}</span> ·
                {{ ward.name }}</span
              >
              @if (ward.onDuty === 0) {
                <span class="font-medium text-destructive">No one rostered</span>
              } @else {
                <span data-numeric
                  >{{ ward.onDuty }} on duty
                  <span class="text-muted-foreground">({{ ward.byShift }})</span></span
                >
              }
            </li>
          }
        </ul>
      }
    </ui-dashboard-tile>
  `,
})
export class StaffingTile {
  private readonly api = inject(Api);

  /** Today's counts for every ward in reach, in one request. */
  protected readonly staffing = resource({
    loader: async (): Promise<WardStaffing[]> => {
      const { wards } = await this.api.invoke(getStaffing, { date: hospitalToday() });
      return wards.map(({ ward, onDuty, shifts }) => ({
        id: ward.id,
        code: ward.code,
        name: ward.name,
        onDuty,
        byShift: shifts.map((s) => `${s.code} ${s.onDuty}`).join(' · '),
      }));
    },
  });

  protected readonly error = computed(() => tileError(this.staffing.error()));
  protected readonly wards = computed(() =>
    this.staffing.hasValue() ? this.staffing.value() : [],
  );
  protected readonly total = computed(() => this.wards().reduce((n, w) => n + w.onDuty, 0));
  protected readonly uncovered = computed(() => this.wards().filter((w) => w.onDuty === 0).length);
}
