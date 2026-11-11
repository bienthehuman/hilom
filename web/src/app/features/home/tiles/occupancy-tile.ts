import { ChangeDetectionStrategy, Component, computed, inject, resource } from '@angular/core';
import { DashboardTile, Stat } from '@hilom/ui/dashboard-tile';
import { EmptyState } from '@hilom/ui/empty-state';
import { provideIcons } from '@ng-icons/core';
import { lucideBedDouble } from '@ng-icons/lucide';

import { Api } from '../../../api/api';
import { getCensus } from '../../../api/fn/clinical/get-census';
import { AuthService } from '../../../core/auth/auth.service';
import { tileError } from './tile-error';

/**
 * Beds taken per ward, from the same census the ward board uses, so it follows the user's scope: a nurse
 * sees the wards they are assigned to today ("My wards"), a chief nurse every ward ("Ward occupancy").
 * The full board stays on the census page.
 */
@Component({
  selector: 'app-occupancy-tile',
  imports: [DashboardTile, Stat, EmptyState],
  providers: [provideIcons({ lucideBedDouble })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'contents' },
  template: `
    <ui-dashboard-tile
      [title]="title()"
      [description]="mine() ? 'Who is in each bed today.' : 'Beds taken, ward by ward.'"
      link="/clinical/census"
      linkLabel="Open ward census"
      [loading]="census.isLoading() && !census.hasValue()"
      [error]="error()"
      (retry)="census.reload()"
    >
      @if (wards().length === 0) {
        <ui-empty-state
          icon="lucideBedDouble"
          [title]="mine() ? 'No ward assigned today' : 'No wards yet'"
          [description]="
            mine()
              ? 'Your ward board fills in when you are assigned to a ward.'
              : 'Wards and beds appear here once they are set up.'
          "
        />
      } @else {
        <div class="flex flex-wrap gap-x-8 gap-y-3">
          <ui-stat [value]="occupied()" label="Beds occupied" [hint]="'of ' + beds() + ' beds'" />
          <ui-stat
            [value]="flagged()"
            label="Need attention"
            hint="critical vitals or a severe allergy"
            [tone]="flagged() > 0 ? 'danger' : 'default'"
          />
        </div>
        <ul class="flex flex-col gap-3" aria-label="Occupancy by ward">
          @for (ward of wards(); track ward.id) {
            <li class="flex flex-col gap-1">
              <div class="flex items-baseline justify-between gap-3 text-sm">
                <span class="min-w-0 truncate"
                  ><span class="font-medium" data-numeric>{{ ward.code }}</span> ·
                  {{ ward.name }}</span
                >
                <span class="shrink-0" data-numeric>
                  {{ ward.occupied }} / {{ ward.beds }}
                  @if (ward.beds > 0 && ward.occupied >= ward.beds) {
                    <span class="font-medium text-warning">Full</span>
                  }
                </span>
              </div>
              <div class="h-1.5 overflow-hidden rounded-full bg-muted" aria-hidden="true">
                <div class="h-full rounded-full bg-primary" [style.width.%]="ward.percent"></div>
              </div>
            </li>
          }
        </ul>
      }
    </ui-dashboard-tile>
  `,
})
export class OccupancyTile {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);

  protected readonly census = resource({ loader: () => this.api.invoke(getCensus) });

  /** Only wards (not all of them) are in reach. */
  protected readonly mine = computed(
    () => !this.auth.scopes('facility.census.view').includes('all'),
  );
  protected readonly title = computed(() => (this.mine() ? 'My wards' : 'Ward occupancy'));
  protected readonly error = computed(() => tileError(this.census.error()));

  protected readonly wards = computed(() =>
    (this.census.hasValue() ? this.census.value() : []).map((ward) => ({
      id: ward.id,
      code: ward.code,
      name: ward.name,
      occupied: ward.occupied,
      beds: ward.beds.length,
      percent: ward.beds.length === 0 ? 0 : Math.min(100, (ward.occupied / ward.beds.length) * 100),
      flagged: ward.beds.filter(
        (b) =>
          b.patient &&
          (b.patient.criticalVitals.length > 0 || b.patient.severeAllergies.length > 0),
      ).length,
    })),
  );
  protected readonly occupied = computed(() => this.wards().reduce((n, w) => n + w.occupied, 0));
  protected readonly beds = computed(() => this.wards().reduce((n, w) => n + w.beds, 0));
  protected readonly flagged = computed(() => this.wards().reduce((n, w) => n + w.flagged, 0));
}
