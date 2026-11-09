import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { STATUS_TONE_CLASSES, type StatusKind, statusAppearance } from './status-map';

/**
 * A status label with its shared color, e.g. `<ui-status-pill kind="appointment" status="confirmed" />`.
 * The text always names the status, so color is never the only signal.
 */
@Component({
  selector: 'ui-status-pill',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class:
      'inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap',
    '[class]': 'toneClass()',
    '[attr.data-tone]': 'appearance().tone',
  },
  template: `<span class="size-1.5 rounded-full bg-current" aria-hidden="true"></span
    >{{ appearance().label }}`,
})
export class StatusPill {
  readonly kind = input.required<StatusKind>();
  readonly status = input.required<string>();

  protected readonly appearance = computed(() => statusAppearance(this.kind(), this.status()));
  protected readonly toneClass = computed(() => STATUS_TONE_CLASSES[this.appearance().tone]);
}
