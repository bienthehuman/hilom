import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { STATUS_TONE_CLASSES } from '@hilom/ui/status';
import { HlmTooltipImports } from '@hilom/ui/tooltip';

/**
 * Marks a clinical entry voided as entered in error (docs/DESIGN_SYSTEM.md §3). The reason shows on
 * hover and on keyboard focus, and screen readers hear it with the pill. Strike through the entry
 * itself next to it (`line-through text-muted-foreground`).
 */
@Component({
  selector: 'ui-voided-pill',
  imports: [HlmTooltipImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span
    tabindex="0"
    class="inline-flex cursor-default items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap no-underline focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
    [class]="toneClass"
    data-tone="neutral"
    [hlmTooltip]="detail()"
    ><span class="size-1.5 rounded-full bg-current" aria-hidden="true"></span>Voided<span
      class="sr-only"
      >: {{ detail() }}</span
    ></span
  >`,
})
export class VoidedPill {
  readonly reason = input.required<string>();
  /** Who voided it and when, already formatted, e.g. "Ana Reyes, 11 Nov 14:05". */
  readonly by = input<string | null>(null);

  protected readonly toneClass = STATUS_TONE_CLASSES.neutral;
  protected readonly detail = computed(() =>
    this.by() ? `${this.reason()} (${this.by()})` : this.reason(),
  );
}
