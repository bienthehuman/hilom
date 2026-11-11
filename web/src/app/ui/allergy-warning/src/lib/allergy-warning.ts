import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { HlmAlertImports } from '@hilom/ui/alert';
import { StatusPill } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideInfo, lucideTriangleAlert } from '@ng-icons/lucide';

/** An active allergy as the warning shows it. */
export interface WarningAllergy {
  allergen: string;
  severity: 'mild' | 'moderate' | 'severe';
  reaction?: string | null;
}

/**
 * The allergy check before giving a medication (docs/DESIGN_SYSTEM.md §3). Lists every active
 * allergy with its severity and reaction. Allergens whose name matches the medication's are marked
 * and turn the warning destructive; with no match it stays a warning, because a name match can't
 * see drug classes (penicillin vs amoxicillin). With no allergies on record it says so.
 *
 * <ui-allergy-warning [allergies]="activeAllergies" [matches]="order.allergyMatches" medication="Amoxicillin" />
 */
@Component({
  selector: 'ui-allergy-warning',
  imports: [NgIcon, StatusPill, HlmAlertImports],
  providers: [provideIcons({ lucideCircleAlert, lucideInfo, lucideTriangleAlert })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (allergies().length === 0) {
      <div hlmAlert variant="info" data-testid="allergy-warning" data-level="none">
        <ng-icon name="lucideInfo" aria-hidden="true" />
        <p hlmAlertTitle>No active allergies on record</p>
        <p hlmAlertDescription>Ask the patient if you're unsure.</p>
      </div>
    } @else {
      <div
        hlmAlert
        [variant]="matched().size > 0 ? 'destructive' : 'warning'"
        data-testid="allergy-warning"
        [attr.data-level]="matched().size > 0 ? 'match' : 'check'"
      >
        <ng-icon
          [name]="matched().size > 0 ? 'lucideCircleAlert' : 'lucideTriangleAlert'"
          aria-hidden="true"
        />
        <p hlmAlertTitle>{{ title() }}</p>
        <div hlmAlertDescription>
          <ul class="mt-1 flex flex-col gap-1.5">
            @for (a of sorted(); track a.allergen) {
              <li class="flex flex-wrap items-center gap-x-2 gap-y-1">
                <span class="font-semibold" data-allergen>{{ a.allergen }}</span>
                <ui-status-pill kind="allergySeverity" [status]="a.severity" />
                @if (isMatch(a)) {
                  <span class="text-xs font-semibold text-destructive">
                    Matches {{ medication() || 'this medication' }}
                  </span>
                }
                @if (a.reaction) {
                  <span class="w-full text-xs text-muted-foreground"
                    >Reaction: {{ a.reaction }}</span
                  >
                }
              </li>
            }
          </ul>
          <p class="mt-2 text-xs text-muted-foreground">
            Matched by name only. Check related drugs too.
          </p>
        </div>
      </div>
    }
  `,
})
export class AllergyWarning {
  /** The patient's active allergies. */
  readonly allergies = input<readonly WarningAllergy[]>([]);
  /** Allergens whose name matches the medication (from the API). */
  readonly matches = input<readonly string[]>([]);
  /** The medication being given, for the match label. */
  readonly medication = input<string | null>(null);

  protected readonly matched = computed(
    () => new Set(this.matches().map((m) => m.trim().toLowerCase())),
  );

  /** Matches first, then most severe, then by name. */
  protected readonly sorted = computed(() => {
    const rank = { severe: 0, moderate: 1, mild: 2 } as const;
    return [...this.allergies()].sort(
      (a, b) =>
        Number(this.isMatch(b)) - Number(this.isMatch(a)) ||
        rank[a.severity] - rank[b.severity] ||
        a.allergen.localeCompare(b.allergen),
    );
  });

  protected readonly title = computed(() => {
    const matches = this.allergies().filter((a) => this.isMatch(a));
    if (matches.length > 0) {
      return `Possible allergy: ${matches.map((a) => a.allergen).join(', ')}`;
    }
    return this.allergies().length === 1
      ? 'Check this allergy before giving'
      : `Check these ${this.allergies().length} allergies before giving`;
  });

  protected isMatch(allergy: WarningAllergy): boolean {
    return this.matched().has(allergy.allergen.trim().toLowerCase());
  }
}
