import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { HlmEmptyImports } from '@hilom/ui/empty';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideInbox, lucideSearchX } from '@ng-icons/lucide';

/**
 * Shown when a list has nothing to display. Always says why, and offers the next step when there is one.
 *
 * ```html
 * <ui-empty-state title="No appointments today" description="Booked appointments appear here.">
 *   <button hlmBtn>Book appointment</button>
 * </ui-empty-state>
 * ```
 * Register any non-default icon with `provideIcons` in the host component.
 */
@Component({
  selector: 'ui-empty-state',
  imports: [HlmEmptyImports, NgIcon],
  providers: [provideIcons({ lucideInbox, lucideSearchX })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div hlmEmpty>
      <hlm-empty-header>
        <div hlmEmptyMedia variant="icon">
          <ng-icon [name]="icon()" aria-hidden="true" />
        </div>
        <div hlmEmptyTitle>{{ title() }}</div>
        @if (description()) {
          <div hlmEmptyDescription>{{ description() }}</div>
        }
      </hlm-empty-header>
      <div hlmEmptyContent>
        <ng-content />
      </div>
    </div>
  `,
})
export class EmptyState {
  readonly title = input.required<string>();
  readonly description = input<string>();
  /** Lucide icon name. Use `lucideSearchX` when filters returned nothing. */
  readonly icon = input('lucideInbox');
}
