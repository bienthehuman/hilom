import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  resource,
  signal,
  untracked,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { HlmSelectImports } from '@hilom/ui/select';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../../api/api';
import { createWard } from '../../../api/fn/facility/create-ward';
import { updateWard } from '../../../api/fn/facility/update-ward';
import { listDepartments } from '../../../api/fn/workforce/list-departments';
import { type WardResponse } from '../../../api/models/ward-response';
import { type WardType } from '../../../api/models/ward-type';
import { labelOf } from '../../../core/format/options';
import { type Problem, toProblem } from '../../../core/http/problem';
import { WARD_TYPES } from './ward-labels';

const NO_DEPARTMENT = 'none';

/** Add a ward, or edit one when `ward` is set. */
@Component({
  selector: 'app-ward-dialog',
  imports: [
    ReactiveFormsModule,
    FormField,
    HlmButtonImports,
    HlmDialogImports,
    HlmInputImports,
    HlmSelectImports,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-dialog [state]="open() ? 'open' : 'closed'" (closed)="open.set(false)">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="sm:max-w-md">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>{{ ward() ? 'Edit ' + ward()!.code : 'New ward' }}</h2>
          <p hlmDialogDescription>Beds are added on the ward's page.</p>
        </hlm-dialog-header>
        <form
          id="ward-form"
          class="grid gap-4 sm:grid-cols-3"
          [formGroup]="form"
          (ngSubmit)="save()"
          novalidate
        >
          <ui-form-field label="Code" for="ward-code" hint="e.g. W3" required>
            <input
              hlmInput
              id="ward-code"
              class="uppercase"
              formControlName="code"
              autocomplete="off"
              spellcheck="false"
            />
          </ui-form-field>
          <ui-form-field class="sm:col-span-2" label="Name" for="ward-name" required>
            <input hlmInput id="ward-name" formControlName="name" autocomplete="off" />
          </ui-form-field>
          <ui-form-field class="sm:col-span-3" label="Type" for="ward-type" required>
            <hlm-select formControlName="wardType" [itemToString]="typeLabel">
              <hlm-select-trigger buttonId="ward-type" class="w-full">
                <hlm-select-value placeholder="Choose a type" />
              </hlm-select-trigger>
              <hlm-select-content *hlmSelectPortal>
                @for (option of types; track option.value) {
                  <hlm-select-item [value]="option.value">{{ option.label }}</hlm-select-item>
                }
              </hlm-select-content>
            </hlm-select>
          </ui-form-field>
          <ui-form-field class="sm:col-span-3" label="Department" for="ward-department">
            <hlm-select formControlName="departmentId" [itemToString]="departmentLabel">
              <hlm-select-trigger buttonId="ward-department" class="w-full">
                <hlm-select-value />
              </hlm-select-trigger>
              <hlm-select-content *hlmSelectPortal>
                <hlm-select-item value="none">No department</hlm-select-item>
                @for (option of departmentOptions(); track option.id) {
                  <hlm-select-item [value]="option.id">{{ option.name }}</hlm-select-item>
                }
              </hlm-select-content>
            </hlm-select>
          </ui-form-field>
          @if (problem(); as p) {
            <p class="text-sm text-destructive sm:col-span-3" role="alert">{{ p.detail }}</p>
          }
        </form>
        <hlm-dialog-footer>
          <button hlmBtn variant="outline" type="button" (click)="open.set(false)">Cancel</button>
          <button hlmBtn type="submit" form="ward-form" [disabled]="saving()">
            {{ saving() ? 'Saving…' : ward() ? 'Save changes' : 'Add ward' }}
          </button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class WardDialog {
  private readonly api = inject(Api);

  readonly open = model(false);
  /** The ward being edited; empty when adding one. */
  readonly ward = input<WardResponse | null>(null);
  readonly saved = output<WardResponse>();

  protected readonly types = WARD_TYPES;
  protected readonly typeLabel = (value: string) => labelOf(WARD_TYPES, value);

  private readonly departments = resource({
    params: () => (this.open() ? true : undefined),
    loader: () => this.api.invoke(listDepartments, { pageSize: 100, status: 'active' }),
  });
  /** Active departments, plus the ward's current one if it has since been deactivated. */
  protected readonly departmentOptions = computed(() => {
    const options = (this.departments.hasValue() ? this.departments.value().items : []).map(
      ({ id, name }) => ({ id, name }),
    );
    const current = this.ward()?.department;
    if (current && !options.some((o) => o.id === current.id)) {
      options.push({ id: current.id, name: `${current.name} (inactive)` });
    }
    return options;
  });
  protected readonly departmentLabel = (id: string) =>
    id === NO_DEPARTMENT
      ? 'No department'
      : (this.departmentOptions().find((o) => o.id === id)?.name ?? '');

  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    code: [
      '',
      [Validators.required, Validators.maxLength(20), Validators.pattern(/^[A-Za-z0-9-]+$/)],
    ],
    name: ['', [Validators.required, Validators.maxLength(100)]],
    wardType: this.fb.control<WardType | null>(null, Validators.required),
    departmentId: [NO_DEPARTMENT],
  });
  protected readonly saving = signal(false);
  protected readonly problem = signal<Problem | null>(null);

  constructor() {
    // Start from the ward (or blank) each time the dialog opens.
    effect(() => {
      if (this.open()) {
        const ward = untracked(() => this.ward());
        this.form.reset({
          code: ward?.code ?? '',
          name: ward?.name ?? '',
          wardType: ward?.wardType ?? null,
          departmentId: ward?.department?.id ?? NO_DEPARTMENT,
        });
        this.problem.set(null);
      }
    });
  }

  protected async save(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.saving()) {
      return;
    }

    const value = this.form.getRawValue();
    const body = {
      code: value.code.trim(),
      name: value.name.trim(),
      wardType: value.wardType,
      departmentId: value.departmentId === NO_DEPARTMENT ? null : value.departmentId,
    };
    const ward = this.ward();
    this.saving.set(true);
    this.problem.set(null);
    try {
      const saved = ward
        ? await this.api.invoke(updateWard, {
            id: ward.id,
            body: { ...body, version: ward.version },
          })
        : await this.api.invoke(createWard, { body });
      this.open.set(false);
      toast.success(ward ? 'Ward updated' : 'Ward added', {
        description: `${saved.code} · ${saved.name}`,
      });
      this.saved.emit(saved);
    } catch (error) {
      const problem = toProblem(error);
      let placed = false;
      if (problem.code === 'ward_code_taken') {
        this.form.controls.code.setErrors({ server: problem.detail });
        placed = true;
      }
      for (const [field, messages] of Object.entries(problem.errors ?? {})) {
        const control = this.form.get(field);
        if (control) {
          control.setErrors({ server: messages[0] });
          placed = true;
        }
      }
      if (!placed) {
        this.problem.set(
          problem.code === 'concurrency_conflict'
            ? {
                ...problem,
                detail: 'Someone else changed this ward just now. Close and try again.',
              }
            : problem,
        );
      }
    } finally {
      this.saving.set(false);
    }
  }
}
