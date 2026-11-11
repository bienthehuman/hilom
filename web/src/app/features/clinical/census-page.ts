import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { PageHeader } from '@hilom/ui/page-header';

import { AuthService } from '../../core/auth/auth.service';
import { CensusBoard } from './census-board';

/** The ward board on its own page, for everyone with `facility.census.view`. */
@Component({
  selector: 'app-census-page',
  imports: [PageHeader, CensusBoard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-6xl">
      <ui-page-header title="Ward census" [description]="description()" />
      <app-census-board />
    </div>
  `,
})
export class CensusPage {
  private readonly auth = inject(AuthService);

  protected readonly description = computed(() =>
    this.auth.scopes('facility.census.view').includes('all')
      ? 'Every bed, who is in it, and who needs attention.'
      : 'The beds on your wards today, who is in them, and who needs attention.',
  );
}
