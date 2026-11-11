import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export type StatTone = 'default' | 'warning' | 'danger';

const TONE_CLASSES: Record<StatTone, string> = {
  default: 'text-foreground',
  warning: 'text-warning',
  danger: 'text-destructive',
};

/**
 * One headline number with its label, for dashboards (`text-3xl` bold, as in docs/DESIGN_SYSTEM.md §5).
 * The tone tints the number, but the label always says what it counts, so color is never the only signal.
 *
 * ```html
 * <ui-stat [value]="3" label="Critical events" tone="danger" hint="in the last 24 hours" />
 * ```
 */
@Component({
  selector: 'ui-stat',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex min-w-0 flex-col' },
  template: `
    <span class="text-3xl leading-9 font-bold" [class]="toneClass()" data-numeric>{{
      value()
    }}</span>
    <span class="text-sm font-medium">{{ label() }}</span>
    @if (hint()) {
      <span class="text-xs text-muted-foreground">{{ hint() }}</span>
    }
  `,
})
export class Stat {
  readonly value = input.required<number | string>();
  readonly label = input.required<string>();
  readonly hint = input<string>();
  readonly tone = input<StatTone>('default');

  protected toneClass(): string {
    return TONE_CLASSES[this.tone()];
  }
}
