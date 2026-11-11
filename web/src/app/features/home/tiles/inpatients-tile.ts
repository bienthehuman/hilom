import { ChangeDetectionStrategy, Component, computed, inject, resource } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DashboardTile, Stat } from '@hilom/ui/dashboard-tile';
import { EmptyState } from '@hilom/ui/empty-state';
import { provideIcons } from '@ng-icons/core';
import { lucideBedDouble } from '@ng-icons/lucide';

import { Api } from '../../../api/api';
import { listEncounters } from '../../../api/fn/clinical/list-encounters';
import { AuthService } from '../../../core/auth/auth.service';
import { formatStay } from '../../clinical/census-board';
import { formatPatientName } from '../../patients/patient-labels';
import { tileError } from './tile-error';

/** Open ward stays in the user's reach: a physician's own admitted patients. */
@Component({
  selector: 'app-inpatients-tile',
  imports: [DashboardTile, Stat, EmptyState, RouterLink],
  providers: [provideIcons({ lucideBedDouble })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'contents' },
  template: `
    <ui-dashboard-tile
      [title]="mine() ? 'My inpatients' : 'Inpatients'"
      [description]="mine() ? 'Patients admitted under your care.' : 'Everyone currently admitted.'"
      link="/clinical/encounters"
      linkLabel="Open encounters"
      [loading]="stays.isLoading() && !stays.hasValue()"
      [error]="error()"
      (retry)="stays.reload()"
    >
      @if (total() === 0) {
        <ui-empty-state
          icon="lucideBedDouble"
          title="No inpatients"
          description="Admitted patients appear here while their stay is open."
        />
      } @else {
        <ui-stat [value]="total()" label="Admitted now" />
        <ul class="divide-y" aria-label="Admitted patients">
          @for (stay of items(); track stay.id) {
            <li class="flex flex-wrap items-center justify-between gap-x-3 py-2 text-sm">
              <a
                class="font-medium underline-offset-4 hover:underline"
                [routerLink]="['/clinical/encounters', stay.id]"
                >{{ name(stay.patient) }}</a
              >
              <span class="text-muted-foreground"
                ><span data-numeric>{{ stay.patient.mrn }}</span> ·
                {{ stayLength(stay.startedAt) }}</span
              >
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
export class InpatientsTile {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);

  protected readonly stays = resource({
    loader: () =>
      this.api.invoke(listEncounters, { status: 'open', type: 'inpatient', pageSize: 6 }),
  });

  protected readonly mine = computed(
    () => !this.auth.scopes('clinical.encounters.view').includes('all'),
  );
  protected readonly error = computed(() => tileError(this.stays.error()));
  protected readonly total = computed(() => (this.stays.hasValue() ? this.stays.value().total : 0));
  protected readonly items = computed(() =>
    this.stays.hasValue() ? this.stays.value().items : [],
  );

  protected readonly name = formatPatientName;

  protected stayLength(startedAt: string): string {
    return formatStay(Math.floor((Date.now() - new Date(startedAt).getTime()) / 3_600_000));
  }
}
