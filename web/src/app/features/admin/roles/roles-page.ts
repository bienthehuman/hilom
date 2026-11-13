import { ChangeDetectionStrategy, Component, computed, inject, resource } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmBadgeImports } from '@hilom/ui/badge';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { EmptyState } from '@hilom/ui/empty-state';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideChevronRight,
  lucideCircleAlert,
  lucidePlus,
  lucideShieldCheck,
  lucideUsers,
} from '@ng-icons/lucide';

import { Api } from '../../../api/api';
import { listRoles } from '../../../api/fn/access/list-roles';
import { HasPermission } from '../../../core/auth/has-permission.directive';
import { toProblem } from '../../../core/http/problem';

/** Every role, how many people hold it, and a way into the editor. */
@Component({
  selector: 'app-roles-page',
  imports: [
    RouterLink,
    NgIcon,
    PageHeader,
    EmptyState,
    HasPermission,
    HlmAlertImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmSkeletonImports,
  ],
  providers: [
    provideIcons({
      lucidePlus,
      lucideUsers,
      lucideChevronRight,
      lucideCircleAlert,
      lucideShieldCheck,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-6xl">
      <ui-page-header
        title="Roles"
        description="A role is a set of permissions, each limited to all records, a ward, the user's own, or linked patients."
      >
        <a *appHasPermission="'access.roles.manage'" actions hlmBtn routerLink="/admin/roles/new">
          <ng-icon name="lucidePlus" aria-hidden="true" data-icon="inline-start" />New role
        </a>
      </ui-page-header>

      @if (roles.error()) {
        <div hlmAlert variant="destructive">
          <ng-icon name="lucideCircleAlert" aria-hidden="true" />
          <h2 hlmAlertTitle>Couldn't load roles</h2>
          <p hlmAlertDescription>{{ errorMessage() }}</p>
          <div hlmAlertAction>
            <button hlmBtn variant="outline" size="sm" type="button" (click)="roles.reload()">
              Try again
            </button>
          </div>
        </div>
      } @else if (roles.isLoading() && !roles.hasValue()) {
        <div class="grid gap-3 md:grid-cols-2" aria-busy="true">
          @for (i of [1, 2, 3, 4]; track i) {
            <div hlmSkeleton class="h-28 w-full rounded-lg"></div>
          }
        </div>
      } @else if (roleList().length === 0) {
        <ui-empty-state
          icon="lucideShieldCheck"
          title="No roles yet"
          description="Roles decide what each person can do. Create one to start granting access."
        >
          <a *appHasPermission="'access.roles.manage'" hlmBtn routerLink="/admin/roles/new">
            <ng-icon name="lucidePlus" aria-hidden="true" data-icon="inline-start" />New role
          </a>
        </ui-empty-state>
      } @else {
        <ul class="grid gap-3 md:grid-cols-2">
          @for (role of roleList(); track role.id) {
            <li>
              <a
                hlmCard
                [routerLink]="['/admin/roles', role.id]"
                class="h-full gap-2 transition-colors hover:border-primary/50 hover:bg-accent/40"
              >
                <div hlmCardHeader class="flex items-start justify-between gap-3">
                  <div class="flex flex-col gap-1">
                    <h2 class="flex items-center gap-2 text-lg font-medium">
                      {{ role.name }}
                      @if (role.isSystem) {
                        <span hlmBadge>Protected</span>
                      }
                      @if (role.isPortal) {
                        <span hlmBadge variant="secondary">Patient portal</span>
                      }
                    </h2>
                    <p class="text-sm text-muted-foreground">{{ role.description }}</p>
                  </div>
                  <ng-icon
                    name="lucideChevronRight"
                    aria-hidden="true"
                    class="mt-1 text-muted-foreground"
                  />
                </div>
                <div
                  hlmCardContent
                  class="flex items-center gap-1.5 text-sm text-muted-foreground"
                  data-numeric
                >
                  <ng-icon name="lucideUsers" aria-hidden="true" />
                  {{ role.userCount }} {{ role.userCount === 1 ? 'person' : 'people' }}
                </div>
              </a>
            </li>
          }
        </ul>
      }
    </div>
  `,
})
export class RolesPage {
  private readonly api = inject(Api);

  protected readonly roles = resource({ loader: () => this.api.invoke(listRoles) });
  protected readonly roleList = computed(() => (this.roles.hasValue() ? this.roles.value() : []));
  protected readonly errorMessage = computed(
    () => toProblem(this.roles.error()).detail ?? 'Please try again.',
  );
}
