import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';

import { type PermissionGroupResponse } from '../../../api/models/permission-group-response';
import { type PermissionResponse } from '../../../api/models/permission-response';
import { type PermissionScope } from '../../../api/models/permission-scope';

/** Permission key → granted scope. Missing means not granted. */
export type GrantMap = Partial<Record<string, PermissionScope>>;

export const SCOPE_COLUMNS: readonly { scope: PermissionScope; label: string; hint: string }[] = [
  { scope: 'all', label: 'All', hint: 'Every record' },
  { scope: 'ward', label: 'Ward', hint: "Wards they're assigned to" },
  { scope: 'own', label: 'Own', hint: "Records they're responsible for" },
  { scope: 'linked', label: 'Linked', hint: 'Patients linked to their portal account' },
];

/**
 * The role editor's grid: one table per module, one row per permission, one radio per scope the
 * permission allows. Native radios grouped by name, so arrow keys move within a row.
 */
@Component({
  selector: 'app-permission-matrix',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dl class="mb-4 grid gap-x-6 gap-y-1 rounded-lg border bg-muted/50 p-3 text-sm sm:grid-cols-2">
      @for (column of scopeColumns; track column.scope) {
        <div class="flex gap-2">
          <dt class="font-medium">{{ column.label }}</dt>
          <dd class="text-muted-foreground">{{ column.hint }}</dd>
        </div>
      }
    </dl>

    <div class="flex flex-col gap-6">
      @for (group of groups(); track group.group) {
        <div class="overflow-hidden rounded-lg border bg-card">
          <div class="overflow-x-auto">
            <table class="w-full text-sm">
              <caption class="border-b bg-muted px-4 py-2.5 text-start">
                <span class="font-semibold">{{ group.group }}</span>
                <span class="ms-2 text-xs text-muted-foreground" data-numeric
                  >{{ grantedCount(group) }} of {{ group.permissions.length }} granted</span
                >
              </caption>
              <thead>
                <tr class="border-b text-xs text-muted-foreground">
                  <th scope="col" class="px-4 py-2 text-start font-medium">Permission</th>
                  <th scope="col" class="w-24 px-2 py-2 text-center font-medium">Not granted</th>
                  @for (column of scopeColumns; track column.scope) {
                    <th
                      scope="col"
                      class="w-20 px-2 py-2 text-center font-medium"
                      [title]="column.hint"
                    >
                      {{ column.label }}
                    </th>
                  }
                </tr>
              </thead>
              <tbody>
                @for (permission of group.permissions; track permission.key) {
                  <tr
                    class="border-b last:border-b-0 hover:bg-muted/40"
                    [attr.data-permission]="permission.key"
                  >
                    <th scope="row" class="px-4 py-2 text-start font-normal">
                      <span class="block text-foreground">{{ permission.description }}</span>
                      <span class="font-mono text-xs text-muted-foreground">{{
                        permission.key
                      }}</span>
                    </th>
                    <td class="px-2 py-2 text-center">
                      <input
                        type="radio"
                        class="size-4 accent-primary disabled:cursor-not-allowed"
                        [name]="'grant-' + permission.key"
                        [checked]="!grants()[permission.key]"
                        [disabled]="readOnly()"
                        [attr.aria-label]="permission.description + ': not granted'"
                        (change)="set(permission.key, null)"
                      />
                    </td>
                    @for (column of scopeColumns; track column.scope) {
                      <td class="px-2 py-2 text-center">
                        @if (allows(permission, column.scope)) {
                          <input
                            type="radio"
                            class="size-4 accent-primary disabled:cursor-not-allowed"
                            [name]="'grant-' + permission.key"
                            [checked]="grants()[permission.key] === column.scope"
                            [disabled]="readOnly()"
                            [attr.aria-label]="permission.description + ': ' + column.label"
                            (change)="set(permission.key, column.scope)"
                          />
                        } @else {
                          <span class="text-muted-foreground/60" aria-hidden="true">—</span>
                        }
                      </td>
                    }
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      }
    </div>
  `,
})
export class PermissionMatrix {
  readonly groups = input.required<readonly PermissionGroupResponse[]>();
  readonly grants = model.required<GrantMap>();
  readonly readOnly = input(false);

  protected readonly scopeColumns = SCOPE_COLUMNS;

  protected allows(permission: PermissionResponse, scope: PermissionScope): boolean {
    return permission.allowedScopes.includes(scope);
  }

  protected grantedCount(group: PermissionGroupResponse): number {
    const grants = this.grants();
    return group.permissions.filter((p) => grants[p.key]).length;
  }

  protected set(key: string, scope: PermissionScope | null): void {
    this.grants.update((grants) => {
      const next = { ...grants };
      if (scope) {
        next[key] = scope;
      } else {
        delete next[key];
      }
      return next;
    });
  }
}

/** A role that reaches patients through portal links shouldn't also reach every patient or whole wards. */
export function mixesPortalAndStaffAccess(grants: GrantMap): boolean {
  const scopes = Object.values(grants);
  return scopes.includes('linked') && (scopes.includes('all') || scopes.includes('ward'));
}
