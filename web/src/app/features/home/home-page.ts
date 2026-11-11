import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { HlmCardImports } from '@hilom/ui/card';
import { EmptyState } from '@hilom/ui/empty-state';
import { PageHeader } from '@hilom/ui/page-header';
import { provideIcons } from '@ng-icons/core';
import { lucideLayoutDashboard } from '@ng-icons/lucide';

import { AuthService } from '../../core/auth/auth.service';
import { CensusBoard } from '../clinical/census-board';

/**
 * Landing page after sign-in. Ward staff (the census at `ward` scope, as nurses have) land on their ward
 * board. Everyone else gets a per-role dashboard in phase 4.
 */
@Component({
  selector: 'app-home-page',
  imports: [PageHeader, EmptyState, HlmCardImports, CensusBoard],
  providers: [provideIcons({ lucideLayoutDashboard })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-6xl">
      @if (wardBoard()) {
        <ui-page-header
          [title]="greeting()"
          description="Your ward today: who is in each bed, and who needs attention."
        />
        <app-census-board />
      } @else {
        <ui-page-header
          [title]="greeting()"
          description="Here's what needs your attention today."
        />
        <div hlmCard>
          <div hlmCardContent>
            <ui-empty-state
              icon="lucideLayoutDashboard"
              title="Nothing needs your attention"
              description="Your appointments and tasks will appear here as Hilom's modules come online."
            />
          </div>
        </div>
      }
    </div>
  `,
})
export class HomePage {
  private readonly auth = inject(AuthService);

  /** The census reaches only the user's own wards: show their board. */
  protected readonly wardBoard = computed(() => {
    const scopes = this.auth.scopes('facility.census.view');
    return scopes.includes('ward') && !scopes.includes('all');
  });

  protected readonly greeting = computed(() => {
    const name = this.auth.user()?.displayName ?? '';
    const hour = new Date().getHours();
    const part = hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening';
    return name ? `${part}, ${name}` : part;
  });
}
