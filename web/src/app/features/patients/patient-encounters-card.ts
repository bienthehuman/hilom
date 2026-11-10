import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  resource,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { EmptyState } from '@hilom/ui/empty-state';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { StatusPill } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideStethoscope } from '@ng-icons/lucide';

import { Api } from '../../api/api';
import { listEncounters } from '../../api/fn/clinical/list-encounters';
import { formatDateTime } from '../../core/format/date-time';
import { toProblem } from '../../core/http/problem';
import { ENCOUNTER_TYPES, formatPhysician, labelOf } from '../clinical/encounter-labels';

/** The patient's encounters the user can see, newest first (`clinical.encounters.view`). */
@Component({
  selector: 'app-patient-encounters-card',
  imports: [
    RouterLink,
    NgIcon,
    EmptyState,
    StatusPill,
    HlmAlertImports,
    HlmButtonImports,
    HlmCardImports,
    HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideCircleAlert, lucideStethoscope })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section hlmCard aria-labelledby="encounters-heading">
      <div hlmCardHeader>
        <h2 id="encounters-heading" hlmCardTitle>Encounters</h2>
      </div>
      <div hlmCardContent>
        @if (loadError(); as message) {
          <div hlmAlert variant="destructive">
            <ng-icon name="lucideCircleAlert" aria-hidden="true" />
            <h3 hlmAlertTitle>Couldn't load encounters</h3>
            <p hlmAlertDescription>{{ message }}</p>
            <div hlmAlertAction>
              <button
                hlmBtn
                variant="outline"
                size="sm"
                type="button"
                (click)="encounters.reload()"
              >
                Try again
              </button>
            </div>
          </div>
        } @else if (!encounters.hasValue()) {
          <div class="flex flex-col gap-2" aria-busy="true">
            <div hlmSkeleton class="h-12 w-full rounded-lg"></div>
            <div hlmSkeleton class="h-12 w-full rounded-lg"></div>
          </div>
        } @else if (encounters.value().items.length === 0) {
          <ui-empty-state
            icon="lucideStethoscope"
            title="No encounters"
            description="Visits appear here once the patient is checked in, if you can see them."
          />
        } @else {
          <ul class="flex flex-col divide-y" aria-label="Encounters">
            @for (e of encounters.value().items; track e.id) {
              <li
                class="flex flex-wrap items-center justify-between gap-3 py-3 first:pt-0 last:pb-0"
              >
                <div class="min-w-0">
                  <a
                    class="font-medium underline-offset-4 hover:underline"
                    [routerLink]="['/clinical/encounters', e.id]"
                    >{{ typeLabel(e.type) }}
                    <span data-numeric>{{ formatDateTime(e.startedAt) }}</span></a
                  >
                  <p class="text-sm text-muted-foreground">
                    {{ formatPhysician(e.attendingPhysician) }} · {{ e.chiefComplaint }}
                  </p>
                </div>
                <ui-status-pill kind="encounter" [status]="e.status" />
              </li>
            }
          </ul>
          @if (encounters.value().total > encounters.value().items.length) {
            <p class="mt-3 text-sm text-muted-foreground">
              Showing the latest {{ encounters.value().items.length }} of
              {{ encounters.value().total }}.
            </p>
          }
        }
      </div>
    </section>
  `,
})
export class PatientEncountersCard {
  private readonly api = inject(Api);

  readonly patientId = input.required<string>();

  protected readonly encounters = resource({
    params: () => this.patientId(),
    loader: ({ params }) =>
      this.api.invoke(listEncounters, { patientId: params, page: 1, pageSize: 10 }),
  });

  protected readonly loadError = computed(() =>
    this.encounters.error()
      ? (toProblem(this.encounters.error()).detail ?? "Couldn't load encounters.")
      : null,
  );

  protected readonly formatDateTime = formatDateTime;
  protected readonly formatPhysician = formatPhysician;
  protected typeLabel(type: string): string {
    return labelOf(ENCOUNTER_TYPES, type);
  }
}
