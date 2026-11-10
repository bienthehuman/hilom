import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  resource,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  type AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  type ValidationErrors,
  Validators,
} from '@angular/forms';
import { HlmButtonImports } from '@hilom/ui/button';
import { DataTable, type DataTableColumn } from '@hilom/ui/data-table';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { HlmDropdownMenuImports } from '@hilom/ui/dropdown-menu';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSelectImports } from '@hilom/ui/select';
import {
  ShiftChip,
  type ShiftChipColor,
  formatClock,
  formatShiftHours,
  formatShiftLength,
} from '@hilom/ui/shift-chip';
import { StatusPill } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArchive,
  lucideArchiveRestore,
  lucideEllipsis,
  lucidePencil,
  lucidePlus,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { flexRenderComponent } from '@tanstack/angular-table';

import { Api } from '../../../api/api';
import { activateShift } from '../../../api/fn/workforce/activate-shift';
import { createShift } from '../../../api/fn/workforce/create-shift';
import { deactivateShift } from '../../../api/fn/workforce/deactivate-shift';
import { listShifts } from '../../../api/fn/workforce/list-shifts';
import { updateShift } from '../../../api/fn/workforce/update-shift';
import { type ShiftColor } from '../../../api/models/shift-color';
import { type ShiftResponse } from '../../../api/models/shift-response';
import { type Option, labelOf } from '../../../core/format/options';
import { type Problem, toProblem } from '../../../core/http/problem';

/**
 * 24-hour "HH:MM". Plain text, because native time inputs show AM/PM in some browser locales and the
 * hospital uses the 24-hour clock everywhere.
 */
const CLOCK_24 = /^([01]\d|2[0-3]):[0-5]\d$/;

function clock24(control: AbstractControl<string>): ValidationErrors | null {
  return !control.value || CLOCK_24.test(control.value.trim())
    ? null
    : { clock: { message: 'Use 24-hour time, like 06:00 or 22:00.' } };
}

const COLORS: readonly Option<ShiftColor>[] = [
  { value: 'am', label: 'Morning (sky)' },
  { value: 'pm', label: 'Afternoon (amber)' },
  { value: 'noc', label: 'Night (violet)' },
];

@Component({
  selector: 'app-shift-cell',
  imports: [ShiftChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex items-center gap-2">
      <ui-shift-chip [code]="shift().code" [color]="shift().color" />
      <span class="font-medium">{{ shift().name }}</span>
    </div>
  `,
})
export class ShiftCell {
  readonly shift = input.required<ShiftResponse>();
}

@Component({
  selector: 'app-shift-status-cell',
  imports: [StatusPill],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<ui-status-pill kind="lookup" [status]="active() ? 'active' : 'inactive'" />`,
})
export class ShiftStatusCell {
  readonly active = input.required<boolean>();
}

@Component({
  selector: 'app-shift-actions-cell',
  imports: [HlmButtonImports, HlmDropdownMenuImports, NgIcon],
  providers: [provideIcons({ lucideEllipsis, lucidePencil, lucideArchive, lucideArchiveRestore })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      hlmBtn
      variant="ghost"
      size="icon-sm"
      type="button"
      [attr.aria-label]="'Actions for ' + shift().code"
      [hlmDropdownMenuTrigger]="menu"
      align="end"
    >
      <ng-icon name="lucideEllipsis" aria-hidden="true" />
    </button>
    <ng-template #menu>
      <hlm-dropdown-menu class="w-44">
        <button hlmDropdownMenuItem type="button" (triggered)="edit.emit(shift())">
          <ng-icon name="lucidePencil" aria-hidden="true" />Edit
        </button>
        <hlm-dropdown-menu-separator />
        @if (shift().isActive) {
          <button hlmDropdownMenuItem type="button" (triggered)="deactivate.emit(shift())">
            <ng-icon name="lucideArchive" aria-hidden="true" />Deactivate
          </button>
        } @else {
          <button hlmDropdownMenuItem type="button" (triggered)="activate.emit(shift())">
            <ng-icon name="lucideArchiveRestore" aria-hidden="true" />Reactivate
          </button>
        }
      </hlm-dropdown-menu>
    </ng-template>
  `,
})
export class ShiftActionsCell {
  readonly shift = input.required<ShiftResponse>();
  readonly edit = output<ShiftResponse>();
  readonly deactivate = output<ShiftResponse>();
  readonly activate = output<ShiftResponse>();
}

/** Shift definitions used by the duty roster: code, hours, color and order. */
@Component({
  selector: 'app-shifts-page',
  imports: [
    ReactiveFormsModule,
    NgIcon,
    PageHeader,
    DataTable,
    FormField,
    ShiftChip,
    HlmButtonImports,
    HlmDialogImports,
    HlmInputImports,
    HlmSelectImports,
  ],
  providers: [provideIcons({ lucidePlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-5xl">
      <ui-page-header
        title="Shifts"
        description="The shifts staff are rostered on. A shift that ends earlier than it starts runs into the next day."
      >
        <button actions hlmBtn type="button" (click)="openEdit(null)">
          <ng-icon name="lucidePlus" aria-hidden="true" data-icon="inline-start" />New shift
        </button>
      </ui-page-header>

      <ui-data-table
        caption="Shifts"
        emptyTitle="No shifts yet"
        emptyDescription="Add the shifts your wards run, such as AM, PM and NOC."
        [columns]="columns"
        [rows]="rows()"
        [total]="rows().length"
        [pageSize]="100"
        [loading]="shifts.isLoading()"
        [error]="loadError()"
        [rowId]="rowId"
        (retry)="shifts.reload()"
      />
    </div>

    <hlm-dialog [state]="editing() ? 'open' : 'closed'" (closed)="editing.set(null)">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="sm:max-w-md">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>
            {{ editing() === 'new' ? 'New shift' : 'Edit ' + editingShift()?.code }}
          </h2>
          <p hlmDialogDescription>Times use the 24-hour clock, in hospital time.</p>
        </hlm-dialog-header>
        <form
          id="shift-form"
          class="grid grid-cols-2 gap-4"
          [formGroup]="form"
          (ngSubmit)="save()"
          novalidate
        >
          <ui-form-field label="Code" for="shift-code" hint="e.g. AM" required>
            <input
              hlmInput
              id="shift-code"
              class="uppercase"
              formControlName="code"
              autocomplete="off"
              spellcheck="false"
            />
          </ui-form-field>
          <ui-form-field label="Name" for="shift-name" required>
            <input hlmInput id="shift-name" formControlName="name" autocomplete="off" />
          </ui-form-field>
          <ui-form-field label="Starts" for="shift-starts" required>
            <input
              hlmInput
              id="shift-starts"
              formControlName="startsAt"
              placeholder="HH:MM"
              autocomplete="off"
              data-numeric
            />
          </ui-form-field>
          <ui-form-field label="Ends" for="shift-ends" required>
            <input
              hlmInput
              id="shift-ends"
              formControlName="endsAt"
              placeholder="HH:MM"
              autocomplete="off"
              data-numeric
            />
          </ui-form-field>
          <ui-form-field label="Color" for="shift-color" required>
            <hlm-select formControlName="color" [itemToString]="colorLabel">
              <hlm-select-trigger buttonId="shift-color" class="w-full">
                <hlm-select-value placeholder="Choose" />
              </hlm-select-trigger>
              <hlm-select-content *hlmSelectPortal>
                @for (option of colors; track option.value) {
                  <hlm-select-item [value]="option.value">{{ option.label }}</hlm-select-item>
                }
              </hlm-select-content>
            </hlm-select>
          </ui-form-field>
          <ui-form-field label="Order" for="shift-order" hint="Lower comes first">
            <input
              hlmInput
              id="shift-order"
              type="number"
              inputmode="numeric"
              min="0"
              max="999"
              formControlName="sortOrder"
            />
          </ui-form-field>
          <div class="col-span-2 flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <span>Preview:</span>
            <ui-shift-chip
              [code]="preview().code || '—'"
              [color]="preview().color"
              [detail]="preview().detail"
            />
            @if (preview().crossesMidnight) {
              <span>Ends the next day.</span>
            }
          </div>
          @if (problem(); as p) {
            <p class="col-span-2 text-sm text-destructive" role="alert">{{ p.detail }}</p>
          }
        </form>
        <hlm-dialog-footer>
          <button hlmBtn variant="outline" type="button" (click)="editing.set(null)">Cancel</button>
          <button hlmBtn type="submit" form="shift-form" [disabled]="saving()">
            {{ saving() ? 'Saving…' : editing() === 'new' ? 'Add shift' : 'Save changes' }}
          </button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class ShiftsPage {
  private readonly api = inject(Api);
  private readonly fb = inject(FormBuilder).nonNullable;

  protected readonly shifts = resource({
    loader: () => this.api.invoke(listShifts, { pageSize: 100 }),
  });
  protected readonly rows = computed(() =>
    this.shifts.hasValue() ? this.shifts.value().items : [],
  );
  protected readonly loadError = computed(() =>
    this.shifts.error() ? (toProblem(this.shifts.error()).detail ?? "Couldn't load shifts.") : null,
  );
  protected readonly rowId = (shift: ShiftResponse) => shift.id;

  protected readonly columns: DataTableColumn<ShiftResponse>[] = [
    {
      id: 'shift',
      header: 'Shift',
      cell: ({ row }) => flexRenderComponent(ShiftCell, { inputs: { shift: row.original } }),
    },
    {
      id: 'hours',
      header: 'Hours',
      cell: ({ row }) =>
        formatShiftHours(row.original.startsAt, row.original.endsAt, row.original.crossesMidnight),
    },
    {
      id: 'length',
      header: 'Length',
      cell: ({ row }) => formatShiftLength(row.original.durationMinutes),
    },
    { id: 'order', header: 'Order', cell: ({ row }) => String(row.original.sortOrder) },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) =>
        flexRenderComponent(ShiftStatusCell, { inputs: { active: row.original.isActive } }),
    },
    {
      id: 'actions',
      header: () => '',
      cell: ({ row }) =>
        flexRenderComponent(ShiftActionsCell, {
          inputs: { shift: row.original },
          outputs: {
            edit: (shift: ShiftResponse) => this.openEdit(shift),
            deactivate: (shift: ShiftResponse) => void this.setActive(shift, false),
            activate: (shift: ShiftResponse) => void this.setActive(shift, true),
          },
        }),
    },
  ];

  // ---- Add and edit ----
  protected readonly colors = COLORS;
  protected readonly colorLabel = (value: string) => labelOf(COLORS, value);

  /** `'new'` while adding, the shift while editing, null when closed. */
  protected readonly editing = signal<ShiftResponse | 'new' | null>(null);
  protected readonly editingShift = computed(() => {
    const editing = this.editing();
    return editing === 'new' ? null : editing;
  });
  protected readonly saving = signal(false);
  protected readonly problem = signal<Problem | null>(null);

  protected readonly form = this.fb.group({
    code: [
      '',
      [Validators.required, Validators.maxLength(10), Validators.pattern(/^[A-Za-z0-9]+$/)],
    ],
    name: ['', [Validators.required, Validators.maxLength(50)]],
    startsAt: ['', [Validators.required, clock24]],
    endsAt: ['', [Validators.required, clock24]],
    color: this.fb.control<ShiftColor | null>(null, Validators.required),
    sortOrder: this.fb.control<number | null>(null, [Validators.min(0), Validators.max(999)]),
  });

  private readonly formValue = toSignal(this.form.valueChanges, { initialValue: this.form.value });
  protected readonly preview = computed(() => {
    const { code, startsAt, endsAt, color } = this.formValue();
    const starts = startsAt?.trim() ?? '';
    const ends = endsAt?.trim() ?? '';
    const valid = CLOCK_24.test(starts) && CLOCK_24.test(ends) && starts !== ends;
    return {
      code: (code ?? '').toUpperCase(),
      color: (color ?? 'am') as ShiftChipColor,
      detail: valid ? `${starts}–${ends}` : undefined,
      crossesMidnight: valid && ends < starts,
    };
  });

  protected openEdit(shift: ShiftResponse | null): void {
    this.form.reset({
      code: shift?.code ?? '',
      name: shift?.name ?? '',
      startsAt: shift ? formatClock(shift.startsAt) : '',
      endsAt: shift ? formatClock(shift.endsAt) : '',
      color: shift?.color ?? null,
      sortOrder: shift?.sortOrder ?? null,
    });
    this.problem.set(null);
    this.editing.set(shift ?? 'new');
  }

  protected async save(): Promise<void> {
    this.form.markAllAsTouched();
    const editing = this.editing();
    if (this.form.invalid || this.saving() || editing === null) {
      return;
    }

    const value = this.form.getRawValue();
    const body = {
      code: value.code.trim(),
      name: value.name.trim(),
      startsAt: `${value.startsAt.trim()}:00`,
      endsAt: `${value.endsAt.trim()}:00`,
      color: value.color,
      sortOrder: value.sortOrder,
    };
    this.saving.set(true);
    this.problem.set(null);
    try {
      const saved =
        editing === 'new'
          ? await this.api.invoke(createShift, { body })
          : await this.api.invoke(updateShift, {
              id: editing.id,
              body: { ...body, version: editing.version },
            });
      this.editing.set(null);
      toast.success(editing === 'new' ? 'Shift added' : 'Shift updated', {
        description: `${saved.code} · ${formatShiftHours(saved.startsAt, saved.endsAt, saved.crossesMidnight)}`,
      });
      this.shifts.reload();
    } catch (error) {
      const problem = toProblem(error);
      let placed = false;
      if (problem.code === 'shift_code_taken') {
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
                detail: 'Someone else changed this shift just now. Close and try again.',
              }
            : problem,
        );
      }
    } finally {
      this.saving.set(false);
    }
  }

  // ---- Deactivate and reactivate ----
  protected async setActive(shift: ShiftResponse, active: boolean): Promise<void> {
    try {
      await this.api.invoke(active ? activateShift : deactivateShift, { id: shift.id });
      toast.success(active ? 'Shift reactivated' : 'Shift deactivated', {
        description: active
          ? `${shift.code} can be rostered again.`
          : `${shift.code} can't be used for new roster entries. Existing entries keep it.`,
      });
      this.shifts.reload();
    } catch (error) {
      toast.error("Couldn't update the shift", {
        description: toProblem(error).detail ?? 'Please try again.',
      });
    }
  }
}
