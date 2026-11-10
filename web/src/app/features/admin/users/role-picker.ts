import { ChangeDetectionStrategy, Component, computed, inject, input, model } from '@angular/core';
import { HlmCheckboxImports } from '@hilom/ui/checkbox';
import { HlmLabelImports } from '@hilom/ui/label';

import { type RoleSummaryResponse } from '../../../api/models/role-summary-response';
import { AuthService } from '../../../core/auth/auth.service';

/**
 * Checkbox list of roles. The System Administrator role can only be ticked or unticked by someone
 * who can manage roles (the API enforces the same rule).
 */
@Component({
  selector: 'app-role-picker',
  imports: [HlmCheckboxImports, HlmLabelImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <fieldset class="flex flex-col gap-1">
      <legend class="mb-2 text-sm font-medium">Roles</legend>
      @for (role of roles(); track role.id) {
        <div
          class="flex items-start gap-3 rounded-md px-2 py-2 hover:bg-muted"
          [class.opacity-60]="isLocked(role)"
        >
          <hlm-checkbox
            class="mt-0.5"
            [inputId]="'role-' + role.id"
            [checked]="selected().includes(role.id)"
            [disabled]="isLocked(role)"
            (checkedChange)="toggle(role.id, $event)"
          />
          <label hlmLabel [for]="'role-' + role.id" class="flex flex-col items-start gap-0.5">
            <span>{{ role.name }}</span>
            <span class="text-xs font-normal text-muted-foreground">{{ role.description }}</span>
            @if (isLocked(role)) {
              <span class="text-xs font-normal text-muted-foreground"
                >Only someone who can manage roles can change this.</span
              >
            }
          </label>
        </div>
      } @empty {
        <p class="text-sm text-muted-foreground">No roles to choose from.</p>
      }
    </fieldset>
  `,
})
export class RolePicker {
  private readonly auth = inject(AuthService);

  readonly roles = input.required<readonly RoleSummaryResponse[]>();
  readonly selected = model.required<string[]>();

  private readonly canChangeAdministrators = computed(() => this.auth.can('access.roles.manage'));

  protected isLocked(role: RoleSummaryResponse): boolean {
    return role.isSystem && !this.canChangeAdministrators();
  }

  protected toggle(roleId: string, checked: boolean): void {
    this.selected.update((ids) =>
      checked ? [...new Set([...ids, roleId])] : ids.filter((id) => id !== roleId),
    );
  }
}
