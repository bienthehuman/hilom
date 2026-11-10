import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  resource,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmAlertDialogImports } from '@hilom/ui/alert-dialog';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft,
  lucideCircleAlert,
  lucideLock,
  lucideTrash2,
  lucideTriangleAlert,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../../api/api';
import { createRole } from '../../../api/fn/access/create-role';
import { deleteRole } from '../../../api/fn/access/delete-role';
import { getPermissionCatalog } from '../../../api/fn/access/get-permission-catalog';
import { getRole } from '../../../api/fn/access/get-role';
import { updateRole } from '../../../api/fn/access/update-role';
import { type GrantDto } from '../../../api/models/grant-dto';
import { type RoleResponse } from '../../../api/models/role-response';
import { AuthService } from '../../../core/auth/auth.service';
import { type Problem, toProblem } from '../../../core/http/problem';
import { type GrantMap, PermissionMatrix, mixesPortalAndStaffAccess } from './permission-matrix';

/** Create or edit a role: name, description and the permission grid. */
@Component({
  selector: 'app-role-editor-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    NgIcon,
    PageHeader,
    FormField,
    PermissionMatrix,
    HlmAlertImports,
    HlmAlertDialogImports,
    HlmButtonImports,
    HlmCardImports,
    HlmInputImports,
    HlmSkeletonImports,
    HlmTextareaImports,
  ],
  providers: [
    provideIcons({
      lucideArrowLeft,
      lucideCircleAlert,
      lucideLock,
      lucideTrash2,
      lucideTriangleAlert,
    }),
  ],
  templateUrl: './role-editor-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RoleEditorPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);

  /** Route parameter: a role id, or "new". */
  readonly id = input.required<string>();

  protected readonly isNew = computed(() => this.id() === 'new');

  protected readonly catalog = resource({ loader: () => this.api.invoke(getPermissionCatalog) });
  protected readonly role = resource({
    params: () => (this.isNew() ? undefined : this.id()),
    loader: ({ params }) => this.api.invoke(getRole, { id: params }),
  });

  protected readonly groups = computed(() => (this.catalog.hasValue() ? this.catalog.value() : []));
  private readonly loadedRole = computed<RoleResponse | null>(() =>
    this.role.hasValue() ? (this.role.value() ?? null) : null,
  );

  protected readonly isSystem = computed(() => this.loadedRole()?.isSystem ?? false);
  protected readonly isPortal = computed(() => this.loadedRole()?.isPortal ?? false);
  protected readonly readOnly = computed(
    () => this.isSystem() || !this.auth.can('access.roles.manage'),
  );
  protected readonly loading = computed(
    () =>
      this.catalog.isLoading() || (!this.isNew() && this.role.isLoading() && !this.loadedRole()),
  );
  protected readonly loadProblem = computed(() => {
    const error = this.catalog.error() ?? this.role.error();
    return error ? toProblem(error) : null;
  });

  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(80)]],
    description: ['', Validators.maxLength(300)],
  });
  protected readonly grants = signal<GrantMap>({});
  private version = 0;

  protected readonly mixedAccess = computed(() => mixesPortalAndStaffAccess(this.grants()));
  protected readonly title = computed(() =>
    this.isNew() ? 'New role' : (this.loadedRole()?.name ?? 'Role'),
  );

  protected readonly saving = signal(false);
  protected readonly problem = signal<Problem | null>(null);
  protected readonly confirmDelete = signal(false);

  constructor() {
    // Fill the form whenever a role loads (including after a conflict reload).
    effect(() => {
      const role = this.loadedRole();
      if (!role) {
        return;
      }
      this.form.reset({ name: role.name, description: role.description });
      this.grants.set(Object.fromEntries(role.grants.map((g) => [g.permission, g.scope])));
      this.version = role.version;
      this.problem.set(null);
    });

    effect(() => {
      if (this.readOnly()) {
        this.form.disable({ emitEvent: false });
      } else {
        this.form.enable({ emitEvent: false });
      }
    });
  }

  protected async save(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.saving() || this.readOnly()) {
      return;
    }

    this.saving.set(true);
    this.problem.set(null);
    const { name, description } = this.form.getRawValue();
    const grants: GrantDto[] = Object.entries(this.grants())
      .filter((entry): entry is [string, NonNullable<GrantMap[string]>] => !!entry[1])
      .map(([permission, scope]) => ({ permission, scope }));

    try {
      if (this.isNew()) {
        const created = await this.api.invoke(createRole, { body: { name, description, grants } });
        toast.success('Role created', { description: created.name });
        await this.router.navigate(['/admin/roles', created.id], { replaceUrl: true });
      } else {
        const updated = await this.api.invoke(updateRole, {
          id: this.id(),
          body: { name, description, grants, version: this.version },
        });
        this.version = updated.version;
        this.form.markAsPristine();
        toast.success('Role saved', {
          description: 'People with this role get the new permissions on their next action.',
        });
      }
    } catch (error) {
      const problem = toProblem(error);
      if (problem.code === 'role_name_taken') {
        this.form.controls.name.setErrors({ server: problem.detail });
      }
      this.problem.set(problem);
    } finally {
      this.saving.set(false);
    }
  }

  protected reload(): void {
    this.role.reload();
  }

  protected async delete(): Promise<void> {
    this.confirmDelete.set(false);
    try {
      await this.api.invoke(deleteRole, { id: this.id() });
      toast.success('Role deleted');
      await this.router.navigateByUrl('/admin/roles');
    } catch (error) {
      toast.error("Couldn't delete role", {
        description: toProblem(error).detail ?? 'Please try again.',
      });
    }
  }
}
