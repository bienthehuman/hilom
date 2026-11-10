import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type ShiftChipColor = 'am' | 'pm' | 'noc';

/** Spelled out so Tailwind finds the classes. */
const DOT_CLASSES: Record<ShiftChipColor, string> = {
  am: 'bg-shift-am',
  pm: 'bg-shift-pm',
  noc: 'bg-shift-noc',
};

/**
 * A shift's code with its color, e.g. `<ui-shift-chip code="NOC" color="noc" name="Night" />`.
 * The color is a dot beside normal text, never a text background (docs/DESIGN_SYSTEM.md: shift colors fail
 * contrast as backgrounds). The code is always written out, so color is never the only signal.
 */
@Component({
  selector: 'ui-shift-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class:
      'inline-flex items-center gap-1.5 rounded-md border bg-card px-2 py-0.5 text-xs font-semibold whitespace-nowrap text-foreground',
    '[attr.title]': 'name() || null',
    '[attr.data-shift]': 'code()',
  },
  template: `
    <span class="size-2 shrink-0 rounded-full" [class]="dotClass()" aria-hidden="true"></span>
    <span data-numeric>{{ code() }}</span>
    @if (name()) {
      <span class="sr-only">{{ ' (' + name() + ')' }}</span>
    }
    @if (detail()) {
      <!-- The leading space keeps the text readable as one phrase; flex layout hides it visually. -->
      <span class="font-normal text-muted-foreground" data-numeric>{{ ' ' + detail() }}</span>
    }
  `,
})
export class ShiftChip {
  readonly code = input.required<string>();
  readonly color = input.required<ShiftChipColor>();
  /** Full name, for the tooltip and screen readers, e.g. "Night". */
  readonly name = input<string>();
  /** Extra text after the code, e.g. the hours. */
  readonly detail = input<string>();

  protected readonly dotClass = computed(() => DOT_CLASSES[this.color()] ?? 'bg-muted-foreground');
}
