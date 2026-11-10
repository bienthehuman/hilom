import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  linkedSignal,
  resource,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HlmAlertDialogImports } from '@hilom/ui/alert-dialog';
import { HlmButtonImports } from '@hilom/ui/button';
import { DataTable, type DataTableColumn, type DataTableSort } from '@hilom/ui/data-table';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { HlmLabelImports } from '@hilom/ui/label';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmSheetImports } from '@hilom/ui/sheet';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSearch, lucideUserPlus } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { flexRenderComponent } from '@tanstack/angular-table';

import { Api } from '../../../api/api';
import { createUser } from '../../../api/fn/access/create-user';
import { disableUser } from '../../../api/fn/access/disable-user';
import { enableUser } from '../../../api/fn/access/enable-user';
import { listRoles } from '../../../api/fn/access/list-roles';
import { listUsers } from '../../../api/fn/access/list-users';
import { setUserRoles } from '../../../api/fn/access/set-user-roles';
import { unlockUser } from '../../../api/fn/access/unlock-user';
import { type UserResponse } from '../../../api/models/user-response';
import { type UserStatusFilter } from '../../../api/models/user-status-filter';
import { AuthService } from '../../../core/auth/auth.service';
import { HasPermission } from '../../../core/auth/has-permission.directive';
import { formatDateTime } from '../../../core/format/date-time';
import { type Problem, toProblem } from '../../../core/http/problem';
import { RolePicker } from './role-picker';
import { UserActionsCell, UserIdentityCell, UserRolesCell, UserStatusCell } from './user-cells';

const PAGE_SIZE = 25;

interface UserPageData {
  items: UserResponse[];
  total: number;
}

/** Users who can sign in, with their roles. Create, edit roles, disable, enable, unlock. */
@Component({
  selector: 'app-users-page',
  imports: [
    ReactiveFormsModule,
    NgIcon,
    PageHeader,
    DataTable,
    FormField,
    HasPermission,
    RolePicker,
    HlmAlertDialogImports,
    HlmButtonImports,
    HlmDialogImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSelectImports,
    HlmSheetImports,
  ],
  providers: [provideIcons({ lucideUserPlus, lucideSearch })],
  templateUrl: './users-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsersPage {
  private readonly api = inject(Api);
  protected readonly auth = inject(AuthService);

  // ---- List ----
  protected readonly pageSize = PAGE_SIZE;
  protected readonly page = signal(1);
  protected readonly sort = signal<DataTableSort | null>({ id: 'name', desc: false });
  protected readonly search = signal('');
  protected readonly status = signal<UserStatusFilter | 'any'>('any');
  private searchTimer?: ReturnType<typeof setTimeout>;

  protected readonly users = resource({
    params: () => ({
      page: this.page(),
      sort: this.sort(),
      q: this.search(),
      status: this.status(),
    }),
    loader: ({ params }): Promise<UserPageData> =>
      this.api.invoke(listUsers, {
        page: params.page,
        pageSize: PAGE_SIZE,
        sort: params.sort ? `${params.sort.desc ? '-' : ''}${params.sort.id}` : undefined,
        q: params.q || undefined,
        status: params.status === 'any' ? undefined : params.status,
      }),
  });

  /** Last loaded page, kept while the next loads so the pager doesn't jump. */
  protected readonly loaded = linkedSignal<UserPageData | undefined, UserPageData>({
    source: () => (this.users.hasValue() ? this.users.value() : undefined),
    computation: (next, previous) => next ?? previous?.value ?? { items: [], total: 0 },
  });

  protected readonly loadError = computed(() =>
    this.users.error() ? (toProblem(this.users.error()).detail ?? "Couldn't load users.") : null,
  );

  private readonly canManage = computed(() => this.auth.can('access.users.manage'));
  private readonly selfId = computed(() => this.auth.user()?.id);

  protected readonly columns = computed<DataTableColumn<UserResponse>[]>(() => [
    {
      id: 'name',
      accessorKey: 'displayName',
      header: 'Name',
      enableSorting: true,
      cell: ({ row }) =>
        flexRenderComponent(UserIdentityCell, {
          inputs: { user: row.original, isSelf: row.original.id === this.selfId() },
        }),
    },
    {
      id: 'roles',
      header: 'Roles',
      cell: ({ row }) => flexRenderComponent(UserRolesCell, { inputs: { user: row.original } }),
    },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) => flexRenderComponent(UserStatusCell, { inputs: { user: row.original } }),
    },
    {
      id: 'lastLoginAt',
      accessorKey: 'lastLoginAt',
      header: 'Last sign-in',
      enableSorting: true,
      cell: ({ row }) => formatDateTime(row.original.lastLoginAt, 'Never'),
    },
    ...(this.canManage()
      ? [
          {
            id: 'actions',
            header: () => '',
            cell: ({ row }) =>
              flexRenderComponent(UserActionsCell, {
                inputs: { user: row.original, isSelf: row.original.id === this.selfId() },
                outputs: {
                  editRoles: (user: UserResponse) => this.openRoles(user),
                  disable: (user: UserResponse) => this.confirmDisable.set(user),
                  enable: (user: UserResponse) => void this.enable(user),
                  unlock: (user: UserResponse) => void this.unlock(user),
                },
              }),
          } satisfies DataTableColumn<UserResponse>,
        ]
      : []),
  ]);

  protected readonly rowId = (user: UserResponse) => user.id;

  protected onSearch(value: string): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.search.set(value.trim());
      this.page.set(1);
    }, 300);
  }

  protected readonly statusOptions: readonly { value: UserStatusFilter | 'any'; label: string }[] =
    [
      { value: 'any', label: 'All users' },
      { value: 'active', label: 'Active' },
      { value: 'locked', label: 'Locked' },
      { value: 'disabled', label: 'Disabled' },
    ];

  /** Lets the closed select show "All users" rather than the raw value "any". */
  protected readonly statusLabel = (value: string): string =>
    this.statusOptions.find((option) => option.value === value)?.label ?? value;

  protected onStatus(value: UserStatusFilter | 'any'): void {
    this.status.set(value);
    this.page.set(1);
  }

  protected onSort(sort: DataTableSort | null): void {
    this.sort.set(sort);
    this.page.set(1);
  }

  // ---- Roles to choose from (create and edit) ----
  protected readonly roles = resource({
    params: () => (this.canManage() ? true : undefined),
    loader: () => this.api.invoke(listRoles),
  });
  protected readonly roleOptions = computed(() =>
    this.roles.hasValue() ? this.roles.value() : [],
  );

  // ---- Create ----
  protected readonly createOpen = signal(false);
  protected readonly creating = signal(false);
  protected readonly createProblem = signal<Problem | null>(null);
  protected readonly createRoleIds = signal<string[]>([]);
  protected readonly createForm = inject(FormBuilder).nonNullable.group({
    displayName: ['', [Validators.required, Validators.maxLength(200)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(12)]],
  });

  protected openCreate(): void {
    this.createForm.reset();
    this.createRoleIds.set([]);
    this.createProblem.set(null);
    this.createOpen.set(true);
  }

  protected async submitCreate(): Promise<void> {
    this.createForm.markAllAsTouched();
    if (this.createForm.invalid || this.creating()) {
      return;
    }

    this.creating.set(true);
    this.createProblem.set(null);
    try {
      const created = await this.api.invoke(createUser, {
        body: { ...this.createForm.getRawValue(), roleIds: this.createRoleIds() },
      });
      this.createOpen.set(false);
      toast.success('User created', {
        description: `${created.displayName} can now sign in with the temporary password.`,
      });
      this.users.reload();
    } catch (error) {
      const problem = toProblem(error);
      if (problem.code === 'email_taken') {
        this.createForm.controls.email.setErrors({ server: problem.detail });
      }
      for (const [field, messages] of Object.entries(problem.errors ?? {})) {
        this.createForm.get(field)?.setErrors({ server: messages[0] });
      }
      this.createProblem.set(problem);
    } finally {
      this.creating.set(false);
    }
  }

  // ---- Edit roles ----
  protected readonly rolesFor = signal<UserResponse | null>(null);
  protected readonly editRoleIds = signal<string[]>([]);
  protected readonly savingRoles = signal(false);

  protected openRoles(user: UserResponse): void {
    this.editRoleIds.set(user.roles.map((r) => r.id));
    this.rolesFor.set(user);
  }

  protected async saveRoles(): Promise<void> {
    const user = this.rolesFor();
    if (!user || this.savingRoles()) {
      return;
    }

    this.savingRoles.set(true);
    try {
      await this.api.invoke(setUserRoles, {
        userId: user.id,
        body: { roleIds: this.editRoleIds() },
      });
      this.rolesFor.set(null);
      toast.success('Roles updated', {
        description: `${user.displayName}'s access changes on their next action.`,
      });
      this.users.reload();
    } catch (error) {
      this.showError("Couldn't update roles", error);
    } finally {
      this.savingRoles.set(false);
    }
  }

  // ---- Disable, enable, unlock ----
  protected readonly confirmDisable = signal<UserResponse | null>(null);

  protected async disable(): Promise<void> {
    const user = this.confirmDisable();
    if (!user) {
      return;
    }
    this.confirmDisable.set(null);
    await this.runAction(
      () => this.api.invoke(disableUser, { id: user.id }),
      'User disabled',
      `${user.displayName} can't sign in, and any open session has ended.`,
      "Couldn't disable user",
    );
  }

  protected enable(user: UserResponse): Promise<void> {
    return this.runAction(
      () => this.api.invoke(enableUser, { id: user.id }),
      'User enabled',
      `${user.displayName} can sign in again.`,
      "Couldn't enable user",
    );
  }

  protected unlock(user: UserResponse): Promise<void> {
    return this.runAction(
      () => this.api.invoke(unlockUser, { id: user.id }),
      'User unlocked',
      `${user.displayName} can try signing in again.`,
      "Couldn't unlock user",
    );
  }

  private async runAction(
    action: () => Promise<unknown>,
    success: string,
    description: string,
    failure: string,
  ): Promise<void> {
    try {
      await action();
      toast.success(success, { description });
      this.users.reload();
    } catch (error) {
      this.showError(failure, error);
    }
  }

  private showError(title: string, error: unknown): void {
    toast.error(title, { description: toProblem(error).detail ?? 'Please try again.' });
  }
}
