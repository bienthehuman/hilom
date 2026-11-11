import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowDown, lucideArrowUp } from '@ng-icons/lucide';

/** How a reading compares with its range. Matches the API's `VitalFlag`. */
export type VitalFlagValue = 'normal' | 'low' | 'high' | 'critical_low' | 'critical_high';

const SEVERITY: Record<VitalFlagValue, number> = {
  normal: 0,
  low: 1,
  high: 1,
  critical_low: 2,
  critical_high: 2,
};

const FLAG_TEXT: Record<VitalFlagValue, string> = {
  normal: '',
  low: 'low',
  high: 'high',
  critical_low: 'critically low',
  critical_high: 'critically high',
};

/** The more serious of several flags, e.g. for blood pressure's two numbers. */
export function worstFlag(...flags: (VitalFlagValue | null | undefined)[]): VitalFlagValue | null {
  return flags.reduce<VitalFlagValue | null>(
    (worst, flag) => (flag && (!worst || SEVERITY[flag] > SEVERITY[worst]) ? flag : worst),
    null,
  );
}

/** "high", "critically low"; empty for normal. For labels and screen readers. */
export function flagText(flag: VitalFlagValue | null | undefined): string {
  return flag ? FLAG_TEXT[flag] : '';
}

/**
 * A vital sign reading (docs/DESIGN_SYSTEM.md §3): number, unit, and when out of range an arrow
 * and color. Out of normal range is warning; critical is destructive and bold. The direction is
 * also spoken, so color never carries the meaning alone.
 *
 * ```html
 * <ui-vital-value value="38.5" unit="°C" flag="high" />
 * <ui-vital-value value="185/95" unit="mmHg" [flag]="worstFlag(f.systolicBp, f.diastolicBp)" />
 * ```
 */
@Component({
  selector: 'ui-vital-value',
  imports: [NgIcon],
  providers: [provideIcons({ lucideArrowUp, lucideArrowDown })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'inline-flex items-center gap-0.5 whitespace-nowrap tabular-nums',
    '[class.text-warning]': 'severity() === 1',
    '[class.text-destructive]': 'severity() === 2',
    '[class.font-semibold]': 'severity() === 2',
    '[attr.data-flag]': 'flag() ?? null',
  },
  template: `
    <span>{{ value() }}</span>
    @if (unit()) {
      <span class="text-xs" [class.text-muted-foreground]="severity() === 0">{{ unit() }}</span>
    }
    @if (direction(); as d) {
      <ng-icon
        [name]="d === 'up' ? 'lucideArrowUp' : 'lucideArrowDown'"
        size="14"
        aria-hidden="true"
      />
      <span class="sr-only">({{ text() }})</span>
    }
  `,
})
export class VitalValue {
  /** Already formatted, e.g. `38.5` or `120/80`. */
  readonly value = input.required<string | number>();
  readonly unit = input<string>();
  readonly flag = input<VitalFlagValue | null>();

  protected readonly severity = computed(() => {
    const flag = this.flag();
    return flag ? SEVERITY[flag] : 0;
  });
  protected readonly direction = computed(() => {
    const flag = this.flag();
    if (flag === 'high' || flag === 'critical_high') {
      return 'up';
    }
    return flag === 'low' || flag === 'critical_low' ? 'down' : null;
  });
  protected readonly text = computed(() => flagText(this.flag()));
}
