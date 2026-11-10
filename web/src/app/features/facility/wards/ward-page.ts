import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  resource,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmAlertDialogImports } from '@hilom/ui/alert-dialog';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { HlmDropdownMenuImports } from '@hilom/ui/dropdown-menu';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { StatusPill } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArchive,
  lucideArchiveRestore,
  lucideArrowLeft,
  lucideBed,
  lucideCircleAlert,
  lucideEllipsis,
  lucidePencil,
  lucidePlus,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../../api/api';
import { activateBed } from '../../../api/fn/facility/activate-bed';
import { activateWard } from '../../../api/fn/facility/activate-ward';
import { addBed } from '../../../api/fn/facility/add-bed';
import { deactivateBed } from '../../../api/fn/facility/deactivate-bed';
import { deactivateWard } from '../../../api/fn/facility/deactivate-ward';
import { getWard } from '../../../api/fn/facility/get-ward';
import { updateBed } from '../../../api/fn/facility/update-bed';
import { type BedResponse } from '../../../api/models/bed-response';
import { type WardResponse } from '../../../api/models/ward-response';
import { AuthService } from '../../../core/auth/auth.service';
import { HasPermission } from '../../../core/auth/has-permission.directive';
import { labelOf } from '../../../core/format/options';
import { toProblem } from '../../../core/http/problem';
import { WardDialog } from './ward-dialog';
import { WardStaffCard } from './ward-staff-card';
import { WARD_TYPES, bedCount } from './ward-labels';

/** One ward: its details and its beds. Managers add, rename and take beds out of service here. */
@Component({
  selector: 'app-ward-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    NgIcon,
    PageHeader,
    FormField,
    HasPermission,
    StatusPill,
    WardDialog,
    WardStaffCard,
    HlmAlertImports,
    HlmAlertDialogImports,
    HlmButtonImports,
    HlmCardImports,
    HlmDialogImports,
    HlmDropdownMenuImports,
    HlmInputImports,
    HlmSkeletonImports,
  ],
  providers: [
    provideIcons({
      lucideArchive,
      lucideArchiveRestore,
      lucideArrowLeft,
      lucideBed,
      lucideCircleAlert,
      lucideEllipsis,
      lucidePencil,
      lucidePlus,
    }),
  ],
  templateUrl: './ward-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WardPage {
  private readonly api = inject(Api);
  private readonly fb = inject(FormBuilder).nonNullable;
  private readonly auth = inject(AuthService);

  /** From the route. */
  readonly id = input.required<string>();

  protected readonly canManage = computed(() => this.auth.can('facility.wards.manage'));

  protected readonly ward = resource({
    params: () => this.id(),
    loader: ({ params }) => this.api.invoke(getWard, { id: params }),
  });
  protected readonly current = computed(() =>
    this.ward.hasValue() ? this.ward.value() : undefined,
  );
  protected readonly loadProblem = computed(() =>
    this.ward.error() ? toProblem(this.ward.error()) : null,
  );

  protected readonly typeLabel = (ward: WardResponse) => labelOf(WARD_TYPES, ward.wardType);
  protected readonly bedCount = bedCount;
  protected readonly outOfService = computed(
    () => this.current()?.beds.filter((bed) => !bed.isActive).length ?? 0,
  );

  // ---- Edit, deactivate ----
  protected readonly editOpen = signal(false);
  protected readonly confirmDeactivate = signal(false);

  protected async setWardActive(active: boolean): Promise<void> {
    const ward = this.current();
    if (!ward) {
      return;
    }
    this.confirmDeactivate.set(false);
    try {
      await this.api.invoke(active ? activateWard : deactivateWard, { id: ward.id });
      toast.success(active ? 'Ward reactivated' : 'Ward deactivated', {
        description: active
          ? `${ward.code} is back in use.`
          : `${ward.code} no longer counts toward capacity or takes new patients.`,
      });
      this.ward.reload();
    } catch (error) {
      toast.error("Couldn't update the ward", {
        description: toProblem(error).detail ?? 'Please try again.',
      });
    }
  }

  // ---- Add bed ----
  protected readonly addForm = this.fb.group({
    label: ['', [Validators.required, Validators.maxLength(20)]],
  });
  protected readonly adding = signal(false);

  protected async addBed(): Promise<void> {
    const ward = this.current();
    this.addForm.markAllAsTouched();
    if (!ward || this.addForm.invalid || this.adding()) {
      return;
    }

    const label = this.addForm.getRawValue().label.trim();
    this.adding.set(true);
    try {
      await this.api.invoke(addBed, { wardId: ward.id, body: { label } });
      toast.success('Bed added', { description: `${label} in ${ward.code}` });
      // Ready for the next one: wards are usually set up a bed at a time.
      this.addForm.reset();
      this.ward.reload();
    } catch (error) {
      const problem = toProblem(error);
      this.addForm.controls.label.setErrors({
        server: problem.errors?.['label']?.[0] ?? problem.detail ?? "Couldn't add the bed.",
      });
    } finally {
      this.adding.set(false);
    }
  }

  // ---- Rename bed ----
  protected readonly renaming = signal<BedResponse | null>(null);
  protected readonly renameForm = this.fb.group({
    label: ['', [Validators.required, Validators.maxLength(20)]],
  });
  protected readonly savingRename = signal(false);

  protected openRename(bed: BedResponse): void {
    this.renameForm.reset({ label: bed.label });
    this.renaming.set(bed);
  }

  protected async saveRename(): Promise<void> {
    const bed = this.renaming();
    this.renameForm.markAllAsTouched();
    if (!bed || this.renameForm.invalid || this.savingRename()) {
      return;
    }

    this.savingRename.set(true);
    try {
      const saved = await this.api.invoke(updateBed, {
        id: bed.id,
        body: { label: this.renameForm.getRawValue().label.trim(), version: bed.version },
      });
      this.renaming.set(null);
      toast.success('Bed renamed', { description: `${bed.label} is now ${saved.label}.` });
      this.ward.reload();
    } catch (error) {
      const problem = toProblem(error);
      this.renameForm.controls.label.setErrors({
        server:
          problem.code === 'concurrency_conflict'
            ? 'Someone else changed this bed just now. Close and try again.'
            : (problem.errors?.['label']?.[0] ?? problem.detail ?? "Couldn't rename the bed."),
      });
    } finally {
      this.savingRename.set(false);
    }
  }

  // ---- Bed in or out of service ----
  protected async setBedActive(bed: BedResponse, active: boolean): Promise<void> {
    try {
      await this.api.invoke(active ? activateBed : deactivateBed, { id: bed.id });
      toast.success(active ? 'Bed back in service' : 'Bed out of service', {
        description: active
          ? `${bed.label} counts toward capacity again.`
          : `${bed.label} no longer counts toward capacity.`,
      });
      this.ward.reload();
    } catch (error) {
      toast.error("Couldn't update the bed", {
        description: toProblem(error).detail ?? 'Please try again.',
      });
    }
  }
}
