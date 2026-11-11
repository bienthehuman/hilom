import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  linkedSignal,
  resource,
  signal,
} from '@angular/core';
import { HlmAlertImports } from '@hilom/ui/alert';
import { BedTile, type BedTilePatient } from '@hilom/ui/bed-tile';
import { HlmButtonImports } from '@hilom/ui/button';
import { EmptyState } from '@hilom/ui/empty-state';
import { HlmLabelImports } from '@hilom/ui/label';
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBedDouble, lucideCircleAlert, lucideRefreshCw } from '@ng-icons/lucide';

import { Api } from '../../api/api';
import { getCensus } from '../../api/fn/clinical/get-census';
import { type CensusPatientResponse } from '../../api/models/census-patient-response';
import { type CensusWardResponse } from '../../api/models/census-ward-response';
import { AuthService } from '../../core/auth/auth.service';
import { toProblem } from '../../core/http/problem';
import { formatPhysician } from './encounter-labels';
import { vitalName } from './encounter-vitals';

/** "5 h" on the first day, then "Day 2", "Day 3"… (the admission day is day 1). */
export function formatStay(admittedHours: number): string {
  return admittedHours < 24 ? `${admittedHours} h` : `Day ${Math.floor(admittedHours / 24) + 1}`;
}

export function toBedTilePatient(p: CensusPatientResponse): BedTilePatient {
  return {
    name: `${p.lastName}, ${p.firstName}`,
    mrn: p.mrn,
    physician: formatPhysician(p.attendingPhysician),
    stay: formatStay(p.admittedHours),
    severeAllergies: p.severeAllergies,
    criticalVitals: p.criticalVitals.map(vitalName),
    link: p.canOpen ? `/clinical/encounters/${p.encounterId}` : null,
  };
}

interface BoardWard {
  id: string;
  code: string;
  name: string;
  summary: string;
  beds: { id: string; label: string; patient: BedTilePatient | null }[];
}

const ALL_WARDS = 'all';

/**
 * The ward board (`facility.census.view`): a `bed-tile` grid per ward the user reaches, with who is in each
 * bed and their flags. Used by the census page and as the nurse home screen.
 */
@Component({
  selector: 'app-census-board',
  imports: [
    BedTile,
    EmptyState,
    NgIcon,
    HlmAlertImports,
    HlmButtonImports,
    HlmLabelImports,
    HlmSelectImports,
    HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideBedDouble, lucideCircleAlert, lucideRefreshCw })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-6' },
  template: `
    <div class="flex flex-wrap items-end justify-between gap-3">
      @if (allWards().length > 1) {
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="census-ward">Ward</label>
          <hlm-select
            class="w-56"
            [value]="wardFilter()"
            [itemToString]="wardLabel"
            (valueChange)="wardFilter.set($any($event))"
          >
            <hlm-select-trigger buttonId="census-ward" class="w-full">
              <hlm-select-value />
            </hlm-select-trigger>
            <hlm-select-content *hlmSelectPortal>
              <hlm-select-item [value]="allValue">All wards</hlm-select-item>
              @for (ward of allWards(); track ward.id) {
                <hlm-select-item [value]="ward.id"
                  >{{ ward.code }} · {{ ward.name }}</hlm-select-item
                >
              }
            </hlm-select-content>
          </hlm-select>
        </div>
      }
      <button
        hlmBtn
        class="ml-auto"
        variant="outline"
        size="sm"
        type="button"
        [disabled]="census.isLoading()"
        (click)="census.reload()"
      >
        <ng-icon name="lucideRefreshCw" aria-hidden="true" />
        Refresh
      </button>
    </div>

    @if (census.error() && !loaded()) {
      <div hlmAlert variant="destructive">
        <ng-icon name="lucideCircleAlert" aria-hidden="true" />
        <h2 hlmAlertTitle>Couldn't load the ward board</h2>
        <p hlmAlertDescription>{{ loadError() }}</p>
        <div hlmAlertAction>
          <button hlmBtn variant="outline" size="sm" type="button" (click)="census.reload()">
            Try again
          </button>
        </div>
      </div>
    } @else if (!loaded()) {
      <div class="flex flex-col gap-3" aria-busy="true" aria-label="Loading the ward board">
        <div hlmSkeleton class="h-6 w-56"></div>
        <div class="grid grid-cols-[repeat(auto-fill,minmax(13rem,1fr))] gap-3">
          @for (i of placeholders; track i) {
            <div hlmSkeleton class="h-32"></div>
          }
        </div>
      </div>
    } @else if (wards().length === 0) {
      <ui-empty-state
        icon="lucideBedDouble"
        [title]="empty().title"
        [description]="empty().description"
      />
    } @else {
      @if (census.error()) {
        <div hlmAlert variant="destructive" role="alert">
          <ng-icon name="lucideCircleAlert" aria-hidden="true" />
          <h2 hlmAlertTitle>Couldn't refresh the ward board</h2>
          <p hlmAlertDescription>Showing what was loaded last. {{ loadError() }}</p>
        </div>
      }
      @for (ward of wards(); track ward.id) {
        <section [attr.aria-labelledby]="'census-' + ward.id">
          <h2 [id]="'census-' + ward.id" class="mb-3 text-lg font-medium">
            <span data-numeric>{{ ward.code }}</span>
            <span class="text-muted-foreground"> · {{ ward.name }}</span>
            <span class="text-sm font-normal text-muted-foreground" data-numeric>
              · {{ ward.summary }}</span
            >
          </h2>
          @if (ward.beds.length === 0) {
            <p class="text-sm text-muted-foreground">This ward has no beds yet.</p>
          } @else {
            <ul class="grid grid-cols-[repeat(auto-fill,minmax(13rem,1fr))] gap-3">
              @for (bed of ward.beds; track bed.id) {
                <li class="flex">
                  <ui-bed-tile class="flex-1" [label]="bed.label" [patient]="bed.patient" />
                </li>
              }
            </ul>
          }
        </section>
      }
    }
  `,
})
export class CensusBoard {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);

  protected readonly allValue = ALL_WARDS;
  protected readonly placeholders = [1, 2, 3, 4];
  protected readonly wardFilter = signal<string>(ALL_WARDS);

  /** The board reaches only the user's wards today (`ward` scope, not `all`). */
  protected readonly onWardsOnly = computed(
    () => !this.auth.scopes('facility.census.view').includes('all'),
  );

  protected readonly empty = computed(() =>
    this.onWardsOnly()
      ? {
          title: 'You are not on a ward today',
          description: "Your ward's beds appear here once you are assigned to a ward for today.",
        }
      : {
          title: 'No wards yet',
          description: 'Wards and their beds appear here once they are set up under Facility.',
        },
  );

  protected readonly census = resource({
    loader: () => this.api.invoke(getCensus, {}),
  });

  /** Last loaded board, kept on screen while refreshing or when a refresh fails. */
  protected readonly loaded = linkedSignal<
    CensusWardResponse[] | undefined,
    CensusWardResponse[] | undefined
  >({
    source: () => (this.census.hasValue() ? this.census.value() : undefined),
    computation: (next, previous) => next ?? previous?.value,
  });

  protected readonly loadError = computed(() =>
    this.census.error() ? (toProblem(this.census.error()).detail ?? 'Please try again.') : null,
  );

  protected readonly allWards = computed(() => this.loaded() ?? []);

  protected readonly wardLabel = (value: string) => {
    const ward = this.allWards().find((w) => w.id === value);
    return ward ? `${ward.code} · ${ward.name}` : 'All wards';
  };

  protected readonly wards = computed<BoardWard[]>(() => {
    const filter = this.wardFilter();
    return this.allWards()
      .filter((w) => filter === ALL_WARDS || w.id === filter)
      .map((w) => ({
        id: w.id,
        code: w.code,
        name: w.name,
        summary: `${w.occupied} of ${w.beds.length} ${w.beds.length === 1 ? 'bed' : 'beds'} occupied`,
        beds: w.beds.map((b) => ({
          id: b.id,
          label: b.label,
          patient: b.patient ? toBedTilePatient(b.patient) : null,
        })),
      }));
  });
}
