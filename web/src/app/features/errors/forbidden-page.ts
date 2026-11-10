import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HlmButtonImports } from '@hilom/ui/button';
import { EmptyState } from '@hilom/ui/empty-state';
import { provideIcons } from '@ng-icons/core';
import { lucideShieldX } from '@ng-icons/lucide';

/** Shown when a signed-in user opens a page their roles don't allow. */
@Component({
  selector: 'app-forbidden-page',
  imports: [EmptyState, HlmButtonImports, RouterLink],
  providers: [provideIcons({ lucideShieldX })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-2xl pt-10">
      <h1 class="sr-only">No access</h1>
      <ui-empty-state
        icon="lucideShieldX"
        title="You don't have access to this page"
        description="Your roles don't include it. If you need it for your work, ask your System Administrator."
      >
        <a hlmBtn variant="outline" routerLink="/">Go to home</a>
      </ui-empty-state>
    </div>
  `,
})
export class ForbiddenPage {}
