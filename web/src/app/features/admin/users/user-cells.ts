import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { HlmBadgeImports } from '@hilom/ui/badge';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmDropdownMenuImports } from '@hilom/ui/dropdown-menu';
import { StatusPill } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBan,
  lucideCircleCheck,
  lucideEllipsis,
  lucideLockOpen,
  lucideShieldCheck,
} from '@ng-icons/lucide';

import { type UserResponse } from '../../../api/models/user-response';

/** Name, email and a "You" marker for the signed-in user. */
@Component({
  selector: 'app-user-identity-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col">
      <span class="font-medium text-foreground">
        {{ user().displayName }}
        @if (isSelf()) {
          <span class="ms-1 text-xs font-normal text-muted-foreground">(you)</span>
        }
      </span>
      <span class="text-xs text-muted-foreground">{{ user().email }}</span>
    </div>
  `,
})
export class UserIdentityCell {
  readonly user = input.required<UserResponse>();
  readonly isSelf = input(false);
}

/** The user's roles as badges; the System Administrator role stands out. */
@Component({
  selector: 'app-user-roles-cell',
  imports: [HlmBadgeImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-wrap gap-1">
      @for (role of user().roles; track role.id) {
        <span hlmBadge [variant]="role.isSystem ? 'default' : 'secondary'">{{ role.name }}</span>
      } @empty {
        <span class="text-xs text-muted-foreground">No roles</span>
      }
    </div>
  `,
})
export class UserRolesCell {
  readonly user = input.required<UserResponse>();
}

/** Active, locked (too many wrong passwords) or disabled. */
@Component({
  selector: 'app-user-status-cell',
  imports: [StatusPill],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<ui-status-pill kind="user" [status]="status()" />`,
})
export class UserStatusCell {
  readonly user = input.required<UserResponse>();
  protected readonly status = computed(() => {
    const user = this.user();
    return user.status === 'disabled' ? 'disabled' : user.isLockedOut ? 'locked' : 'active';
  });
}

/** Row menu. Shows only actions that make sense for this user and this viewer. */
@Component({
  selector: 'app-user-actions-cell',
  imports: [HlmButtonImports, HlmDropdownMenuImports, NgIcon],
  providers: [
    provideIcons({
      lucideEllipsis,
      lucideShieldCheck,
      lucideBan,
      lucideCircleCheck,
      lucideLockOpen,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (!isSelf()) {
      <button
        hlmBtn
        variant="ghost"
        size="icon-sm"
        type="button"
        [attr.aria-label]="'Actions for ' + user().displayName"
        [hlmDropdownMenuTrigger]="menu"
        align="end"
      >
        <ng-icon name="lucideEllipsis" aria-hidden="true" />
      </button>
      <ng-template #menu>
        <hlm-dropdown-menu class="w-44">
          <button hlmDropdownMenuItem type="button" (triggered)="editRoles.emit(user())">
            <ng-icon name="lucideShieldCheck" aria-hidden="true" />Edit roles
          </button>
          @if (user().isLockedOut) {
            <button hlmDropdownMenuItem type="button" (triggered)="unlock.emit(user())">
              <ng-icon name="lucideLockOpen" aria-hidden="true" />Unlock
            </button>
          }
          <hlm-dropdown-menu-separator />
          @if (user().status === 'active') {
            <button
              hlmDropdownMenuItem
              variant="destructive"
              type="button"
              (triggered)="disable.emit(user())"
            >
              <ng-icon name="lucideBan" aria-hidden="true" />Disable
            </button>
          } @else {
            <button hlmDropdownMenuItem type="button" (triggered)="enable.emit(user())">
              <ng-icon name="lucideCircleCheck" aria-hidden="true" />Enable
            </button>
          }
        </hlm-dropdown-menu>
      </ng-template>
    }
  `,
})
export class UserActionsCell {
  readonly user = input.required<UserResponse>();
  readonly isSelf = input(false);

  readonly editRoles = output<UserResponse>();
  readonly disable = output<UserResponse>();
  readonly enable = output<UserResponse>();
  readonly unlock = output<UserResponse>();
}
