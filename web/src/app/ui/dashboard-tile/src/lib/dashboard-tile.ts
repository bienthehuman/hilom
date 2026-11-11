import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { type Params, RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowRight, lucideCircleAlert } from '@ng-icons/lucide';

let nextId = 0;

/**
 * A dashboard card: title, an optional link to the full page, and the four data states. Loading shows a
 * skeleton and an error shows an alert with retry; otherwise the projected content is the success (or
 * empty) state, which the caller draws.
 *
 * ```html
 * <ui-dashboard-tile title="Pending link requests" link="/link-requests" linkLabel="Review requests"
 *   [loading]="links.isLoading()" [error]="failed()" (retry)="links.reload()">
 *   <ui-stat [value]="total()" label="Waiting for review" />
 * </ui-dashboard-tile>
 * ```
 */
@Component({
  selector: 'ui-dashboard-tile',
  imports: [
    NgIcon,
    RouterLink,
    HlmAlertImports,
    HlmButtonImports,
    HlmCardImports,
    HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideArrowRight, lucideCircleAlert })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <section hlmCard class="h-full" [attr.aria-labelledby]="titleId">
      <div hlmCardHeader>
        <h2 hlmCardTitle [id]="titleId">{{ title() }}</h2>
        @if (description()) {
          <p hlmCardDescription>{{ description() }}</p>
        }
      </div>
      <div hlmCardContent class="flex flex-1 flex-col gap-4">
        @if (error()) {
          <div hlmAlert variant="destructive">
            <ng-icon name="lucideCircleAlert" aria-hidden="true" />
            <h3 hlmAlertTitle>Couldn't load this</h3>
            <p hlmAlertDescription>{{ error() }}</p>
            <div hlmAlertAction>
              <button hlmBtn variant="outline" size="sm" type="button" (click)="retry.emit()">
                Try again
              </button>
            </div>
          </div>
        } @else if (loading()) {
          <div
            class="flex flex-col gap-3"
            role="status"
            aria-busy="true"
            [attr.aria-label]="'Loading ' + title()"
          >
            <div hlmSkeleton class="h-9 w-24"></div>
            <div hlmSkeleton class="h-5 w-full"></div>
            <div hlmSkeleton class="h-5 w-4/5"></div>
          </div>
        } @else {
          <ng-content />
        }
      </div>
      @if (link()) {
        <div hlmCardFooter class="mt-auto">
          <a hlmBtn variant="outline" size="sm" [routerLink]="link()" [queryParams]="linkQuery()">
            {{ linkLabel() }}
            <ng-icon name="lucideArrowRight" aria-hidden="true" />
          </a>
        </div>
      }
    </section>
  `,
})
export class DashboardTile {
  protected readonly titleId = `dashboard-tile-${nextId++}`;

  readonly title = input.required<string>();
  readonly description = input<string>();
  /** Route of the full page this tile summarizes. */
  readonly link = input<string>();
  /** Query parameters for the link, e.g. a filter preset. */
  readonly linkQuery = input<Params>();
  /** Says where the link goes ("Open ward census"), since several tiles sit on one page. */
  readonly linkLabel = input('View all');
  readonly loading = input(false);
  /** A message when loading failed; shows an alert with a retry button. */
  readonly error = input<string | null>(null);
  readonly retry = output();
}
