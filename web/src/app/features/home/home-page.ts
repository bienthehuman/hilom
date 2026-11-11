import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { HlmCardImports } from '@hilom/ui/card';
import { EmptyState } from '@hilom/ui/empty-state';
import { PageHeader } from '@hilom/ui/page-header';
import { provideIcons } from '@ng-icons/core';
import { lucideLayoutDashboard } from '@ng-icons/lucide';

import { AuthService } from '../../core/auth/auth.service';
import { AppointmentsTile } from './tiles/appointments-tile';
import { AuditTile } from './tiles/audit-tile';
import { InpatientsTile } from './tiles/inpatients-tile';
import { OccupancyTile } from './tiles/occupancy-tile';
import { PendingLinksTile } from './tiles/pending-links-tile';
import { ShiftsTile } from './tiles/shifts-tile';
import { StaffingTile } from './tiles/staffing-tile';
import { SwapsTile } from './tiles/swaps-tile';
import { UsersTile } from './tiles/users-tile';

/**
 * Landing page after sign-in: a dashboard built from tiles. A tile shows when the user holds the permission
 * behind it (never by role name), and each tile's data is already narrowed to the user's scope by the API
 * endpoint it reads, so a nurse sees their wards and a chief nurse every ward in the same tile.
 */
@Component({
  selector: 'app-home-page',
  imports: [
    PageHeader,
    EmptyState,
    HlmCardImports,
    AppointmentsTile,
    AuditTile,
    InpatientsTile,
    OccupancyTile,
    PendingLinksTile,
    ShiftsTile,
    StaffingTile,
    SwapsTile,
    UsersTile,
  ],
  providers: [provideIcons({ lucideLayoutDashboard })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-6xl">
      <ui-page-header [title]="greeting()" description="Here's what needs your attention today." />
      @if (anyTile()) {
        <div class="grid gap-4 md:grid-cols-2">
          @if (auth.can('access.audit.view')) {
            <app-audit-tile />
          }
          @if (auth.can('access.users.view')) {
            <app-users-tile />
          }
          @if (auth.can('facility.census.view')) {
            <app-occupancy-tile />
          }
          @if (auth.scopes('clinical.encounters.view').includes('own')) {
            <app-inpatients-tile />
          }
          @if (appointments()) {
            <app-appointments-tile />
          }
          @if (auth.can('patients.links.manage')) {
            <app-pending-links-tile />
          }
          @if (auth.can('workforce.roster.manage')) {
            <app-staffing-tile />
          }
          @if (auth.can('workforce.roster.view')) {
            <app-shifts-tile />
          }
          @if (auth.can('workforce.swaps.request')) {
            <app-swaps-tile />
          }
        </div>
      } @else {
        <div hlmCard>
          <div hlmCardContent>
            <ui-empty-state
              icon="lucideLayoutDashboard"
              title="Nothing needs your attention"
              description="Your role has no dashboard tiles. Use the menu to open the parts of Hilom you can reach."
            />
          </div>
        </div>
      }
    </div>
  `,
})
export class HomePage {
  protected readonly auth = inject(AuthService);

  protected readonly appointments = computed(() => {
    const scopes = this.auth.scopes('scheduling.appointments.view');
    return scopes.includes('all') || scopes.includes('own');
  });

  /** True when at least one tile applies to the signed-in user. */
  protected readonly anyTile = computed(
    () =>
      this.appointments() ||
      this.auth.scopes('clinical.encounters.view').includes('own') ||
      [
        'access.audit.view',
        'access.users.view',
        'facility.census.view',
        'patients.links.manage',
        'workforce.roster.manage',
        'workforce.roster.view',
        'workforce.swaps.request',
      ].some((permission) => this.auth.can(permission)),
  );

  protected readonly greeting = computed(() => {
    const name = this.auth.user()?.displayName ?? '';
    const hour = new Date().getHours();
    const part = hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening';
    return name ? `${part}, ${name}` : part;
  });
}
