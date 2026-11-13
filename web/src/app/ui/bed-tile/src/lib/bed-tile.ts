import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideActivity, lucideTriangleAlert } from '@ng-icons/lucide';

/** Who is in the bed, already formatted for display. */
export interface BedTilePatient {
  /** "DelaCruz, Juan" */
  name: string;
  mrn: string;
  /** "Dr. Jose Santos" */
  physician: string;
  /** How long they've been in, e.g. "5 h" or "Day 3". */
  stay: string;
  /** Active severe allergens. */
  severeAllergies: readonly string[];
  /** Measures in a critical range in the latest vital signs, e.g. "Heart rate". */
  criticalVitals: readonly string[];
  /** Where the tile leads (the stay); none when the viewer can't open it. */
  link?: string | null;
}

/**
 * A census board tile (docs/DESIGN_SYSTEM.md §7): the bed, who is in it, and flags that need attention.
 * Severe allergies and critical vital signs use `destructive` with an icon and words, so color never carries
 * the meaning alone. A free bed is dashed and quiet. When `patient.link` is set the patient's name is a link
 * and the whole tile is its click target.
 *
 * ```html
 * <ui-bed-tile label="Bed 12" [patient]="{ name: 'DelaCruz, Juan', mrn: 'P-000123', … }" />
 * <ui-bed-tile label="Bed 13" [patient]="null" />
 * ```
 */
@Component({
  selector: 'ui-bed-tile',
  imports: [NgIcon, RouterLink],
  providers: [provideIcons({ lucideActivity, lucideTriangleAlert })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class:
      'relative flex min-h-32 flex-col gap-2 rounded-lg border bg-card p-3 text-sm transition-colors data-free:border-dashed data-free:bg-muted data-flagged:border-destructive data-link:hover:bg-accent data-link:has-focus-visible:border-ring data-link:has-focus-visible:ring-[3px] data-link:has-focus-visible:ring-ring/50',
    '[attr.data-free]': "patient() ? null : ''",
    '[attr.data-flagged]': "flagged() ? '' : null",
    '[attr.data-link]': "patient()?.link ? '' : null",
  },
  template: `
    <div class="flex items-baseline justify-between gap-2">
      <span class="font-semibold" data-numeric>{{ label() }}</span>
      @if (patient(); as p) {
        <span class="text-xs text-muted-foreground" data-numeric>
          <span class="sr-only">Admitted </span>{{ p.stay }}
        </span>
      } @else {
        <span class="text-xs text-muted-foreground">Free</span>
      }
    </div>
    @if (patient(); as p) {
      <div class="min-w-0">
        @if (p.link) {
          <a
            class="block truncate font-medium text-foreground outline-none after:absolute after:inset-0 after:rounded-lg hover:underline"
            [routerLink]="p.link"
            >{{ p.name }}</a
          >
        } @else {
          <p class="truncate font-medium">{{ p.name }}</p>
        }
        <p class="truncate text-xs text-muted-foreground">
          <span data-numeric>{{ p.mrn }}</span> · {{ p.physician }}
        </p>
      </div>
      @if (p.severeAllergies.length > 0 || p.criticalVitals.length > 0) {
        <ul class="mt-auto flex flex-col gap-1" aria-label="Flags">
          @if (p.severeAllergies.length > 0) {
            <li
              class="flex items-start gap-1 rounded-sm bg-destructive-soft px-1.5 py-0.5 text-xs font-medium text-destructive"
            >
              <ng-icon
                name="lucideTriangleAlert"
                size="14"
                class="mt-px shrink-0"
                aria-hidden="true"
              />
              <span>Allergy: {{ p.severeAllergies.join(', ') }}</span>
            </li>
          }
          @if (p.criticalVitals.length > 0) {
            <li
              class="flex items-start gap-1 rounded-sm bg-destructive-soft px-1.5 py-0.5 text-xs font-semibold text-destructive"
            >
              <ng-icon name="lucideActivity" size="14" class="mt-px shrink-0" aria-hidden="true" />
              <span>Critical: {{ p.criticalVitals.join(', ') }}</span>
            </li>
          }
        </ul>
      }
    }
  `,
})
export class BedTile {
  /** The bed's label, e.g. "Bed 12". */
  readonly label = input.required<string>();
  /** Who is in the bed; null when it's free. */
  readonly patient = input<BedTilePatient | null>(null);

  protected readonly flagged = computed(() => {
    const p = this.patient();
    return !!p && (p.severeAllergies.length > 0 || p.criticalVitals.length > 0);
  });
}
