import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  linkedSignal,
  resource,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HlmButtonImports } from '@hilom/ui/button';
import { DataTable, type DataTableColumn, type DataTableSort } from '@hilom/ui/data-table';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { HlmLabelImports } from '@hilom/ui/label';
import { HlmSelectImports } from '@hilom/ui/select';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideSearch } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { flexRenderComponent } from '@tanstack/angular-table';

import { Api } from '../../../api/api';
import { type LookupResponse } from '../../../api/models/lookup-response';
import { type LookupStatusFilter } from '../../../api/models/lookup-status-filter';
import { type Problem, toProblem } from '../../../core/http/problem';
import { LookupActionsCell, LookupStatusCell } from './lookup-cells';
import { type LookupKind } from './lookup-kinds';

const PAGE_SIZE = 25;
const NAME_MAX_LENGTH = 100;

interface LookupPageData {
  items: LookupResponse[];
  total: number;
}

/**
 * One lookup (departments, positions or specializations): search, filter by status, add, rename,
 * deactivate and reactivate. Entries are never deleted, so records that use them stay valid.
 */
@Component({
  selector: 'app-lookup-list',
  imports: [
    ReactiveFormsModule,
    NgIcon,
    DataTable,
    FormField,
    HlmButtonImports,
    HlmDialogImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSelectImports,
  ],
  providers: [provideIcons({ lucidePlus, lucideSearch })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-4 flex flex-wrap items-end gap-3">
      <div class="flex w-full max-w-xs flex-col gap-1.5">
        <label hlmLabel [for]="kind().id + '-search'">Search</label>
        <div class="relative">
          <ng-icon
            name="lucideSearch"
            aria-hidden="true"
            class="pointer-events-none absolute start-2.5 top-1/2 -translate-y-1/2 text-muted-foreground"
          />
          <input
            hlmInput
            [id]="kind().id + '-search'"
            type="search"
            class="ps-8"
            placeholder="Name"
            autocomplete="off"
            (input)="onSearch($any($event.target).value)"
          />
        </div>
      </div>
      <div class="flex w-40 flex-col gap-1.5">
        <label hlmLabel [for]="kind().id + '-status'">Status</label>
        <hlm-select
          [value]="status()"
          [itemToString]="statusLabel"
          (valueChange)="onStatus($any($event))"
        >
          <hlm-select-trigger [buttonId]="kind().id + '-status'" class="w-full">
            <hlm-select-value />
          </hlm-select-trigger>
          <hlm-select-content *hlmSelectPortal>
            @for (option of statusOptions; track option.value) {
              <hlm-select-item [value]="option.value">{{ option.label }}</hlm-select-item>
            }
          </hlm-select-content>
        </hlm-select>
      </div>
      <button hlmBtn type="button" class="ms-auto" (click)="openCreate()">
        <ng-icon name="lucidePlus" aria-hidden="true" data-icon="inline-start" />New
        {{ kind().singular.toLowerCase() }}
      </button>
    </div>

    <ui-data-table
      [caption]="kind().plural"
      [emptyTitle]="isFiltered() ? 'Nothing matches' : 'No ' + kind().plural.toLowerCase() + ' yet'"
      [emptyDescription]="
        isFiltered()
          ? 'Try a different search or status.'
          : 'Add the first ' + kind().singular.toLowerCase() + ' to use it on employee records.'
      "
      [emptyIcon]="isFiltered() ? 'lucideSearchX' : 'lucideInbox'"
      [columns]="columns"
      [rows]="loaded().items"
      [total]="loaded().total"
      [page]="page()"
      [pageSize]="pageSize"
      [sort]="sort()"
      [loading]="items.isLoading()"
      [error]="loadError()"
      [rowId]="rowId"
      (pageChange)="page.set($event)"
      (sortChange)="onSort($event)"
      (retry)="items.reload()"
    />

    <!-- Add or rename -->
    <hlm-dialog [state]="editing() ? 'open' : 'closed'" (closed)="editing.set(null)">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="sm:max-w-md">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>{{ dialogTitle() }}</h2>
          <p hlmDialogDescription>Names must be unique. Capitalization doesn't count.</p>
        </hlm-dialog-header>
        <form
          [id]="kind().id + '-form'"
          class="flex flex-col gap-4"
          [formGroup]="form"
          (ngSubmit)="save()"
          novalidate
        >
          <ui-form-field label="Name" [for]="kind().id + '-name'" required>
            <input
              hlmInput
              [id]="kind().id + '-name'"
              formControlName="name"
              autocomplete="off"
              [placeholder]="kind().example"
            />
          </ui-form-field>
          @if (saveProblem(); as problem) {
            <p class="text-sm text-destructive" role="alert">{{ problem.detail }}</p>
          }
        </form>
        <hlm-dialog-footer>
          <button hlmBtn variant="outline" type="button" (click)="editing.set(null)">Cancel</button>
          <button hlmBtn type="submit" [attr.form]="kind().id + '-form'" [disabled]="saving()">
            {{ saving() ? 'Saving…' : editing() === 'new' ? 'Add' : 'Save' }}
          </button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class LookupList {
  private readonly api = inject(Api);

  readonly kind = input.required<LookupKind>();

  // ---- List ----
  protected readonly pageSize = PAGE_SIZE;
  protected readonly page = signal(1);
  protected readonly sort = signal<DataTableSort | null>({ id: 'name', desc: false });
  protected readonly search = signal('');
  protected readonly status = signal<LookupStatusFilter | 'any'>('any');
  private searchTimer?: ReturnType<typeof setTimeout>;

  protected readonly items = resource({
    params: () => ({
      kind: this.kind(),
      page: this.page(),
      sort: this.sort(),
      q: this.search(),
      status: this.status(),
    }),
    loader: ({ params }): Promise<LookupPageData> =>
      this.api.invoke(params.kind.list, {
        page: params.page,
        pageSize: PAGE_SIZE,
        sort: params.sort?.desc ? '-name' : 'name',
        q: params.q || undefined,
        status: params.status === 'any' ? undefined : params.status,
      }),
  });

  /** Last loaded page, kept while the next loads so the pager doesn't jump. */
  protected readonly loaded = linkedSignal<LookupPageData | undefined, LookupPageData>({
    source: () => (this.items.hasValue() ? this.items.value() : undefined),
    computation: (next, previous) => next ?? previous?.value ?? { items: [], total: 0 },
  });

  protected readonly loadError = computed(() =>
    this.items.error()
      ? (toProblem(this.items.error()).detail ??
        `Couldn't load ${this.kind().plural.toLowerCase()}.`)
      : null,
  );

  protected readonly isFiltered = computed(() => this.search() !== '' || this.status() !== 'any');

  protected readonly columns: DataTableColumn<LookupResponse>[] = [
    { id: 'name', accessorKey: 'name', header: 'Name', enableSorting: true },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) => flexRenderComponent(LookupStatusCell, { inputs: { item: row.original } }),
    },
    {
      id: 'actions',
      header: () => '',
      cell: ({ row }) =>
        flexRenderComponent(LookupActionsCell, {
          inputs: { item: row.original },
          outputs: {
            rename: (item: LookupResponse) => this.openRename(item),
            deactivate: (item: LookupResponse) => void this.setActive(item, false),
            activate: (item: LookupResponse) => void this.setActive(item, true),
          },
        }),
    },
  ];

  protected readonly rowId = (item: LookupResponse) => item.id;

  protected readonly statusOptions: readonly {
    value: LookupStatusFilter | 'any';
    label: string;
  }[] = [
    { value: 'any', label: 'All' },
    { value: 'active', label: 'Active' },
    { value: 'inactive', label: 'Inactive' },
  ];

  protected readonly statusLabel = (value: string): string =>
    this.statusOptions.find((option) => option.value === value)?.label ?? value;

  protected onSearch(value: string): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.search.set(value.trim());
      this.page.set(1);
    }, 300);
  }

  protected onStatus(value: LookupStatusFilter | 'any'): void {
    this.status.set(value);
    this.page.set(1);
  }

  protected onSort(sort: DataTableSort | null): void {
    this.sort.set(sort);
    this.page.set(1);
  }

  // ---- Add and rename ----
  /** `'new'` while adding, the entry while renaming, null when the dialog is closed. */
  protected readonly editing = signal<LookupResponse | 'new' | null>(null);
  protected readonly saving = signal(false);
  protected readonly saveProblem = signal<Problem | null>(null);
  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(NAME_MAX_LENGTH)]],
  });

  protected readonly dialogTitle = computed(() => {
    const editing = this.editing();
    const singular = this.kind().singular.toLowerCase();
    return editing === 'new' || editing === null ? `New ${singular}` : `Rename ${editing.name}`;
  });

  protected openCreate(): void {
    this.form.reset();
    this.saveProblem.set(null);
    this.editing.set('new');
  }

  protected openRename(item: LookupResponse): void {
    this.form.reset({ name: item.name });
    this.saveProblem.set(null);
    this.editing.set(item);
  }

  protected async save(): Promise<void> {
    this.form.markAllAsTouched();
    const editing = this.editing();
    if (this.form.invalid || this.saving() || editing === null) {
      return;
    }

    const kind = this.kind();
    const name = this.form.getRawValue().name.trim();
    this.saving.set(true);
    this.saveProblem.set(null);
    try {
      const saved =
        editing === 'new'
          ? await this.api.invoke(kind.create, { body: { name } })
          : await this.api.invoke(kind.update, {
              id: editing.id,
              body: { name, version: editing.version },
            });
      this.editing.set(null);
      toast.success(editing === 'new' ? `${kind.singular} added` : `${kind.singular} renamed`, {
        description: saved.name,
      });
      this.items.reload();
    } catch (error) {
      this.showSaveError(error);
    } finally {
      this.saving.set(false);
    }
  }

  private showSaveError(error: unknown): void {
    const problem = toProblem(error);
    if (problem.code === 'lookup_name_taken') {
      this.form.controls.name.setErrors({ server: problem.detail });
      return;
    }
    if (problem.errors?.['name']) {
      this.form.controls.name.setErrors({ server: problem.errors['name'][0] });
      return;
    }
    if (problem.code === 'concurrency_conflict') {
      // Someone else saved first. Show the latest list so the next attempt starts from it.
      this.items.reload();
      this.saveProblem.set({
        ...problem,
        detail: 'Someone else changed this entry while you were editing. Close and try again.',
      });
      return;
    }
    this.saveProblem.set(problem);
  }

  // ---- Deactivate and reactivate ----
  protected async setActive(item: LookupResponse, active: boolean): Promise<void> {
    const kind = this.kind();
    try {
      await this.api.invoke(active ? kind.activate : kind.deactivate, { id: item.id });
      toast.success(active ? `${kind.singular} reactivated` : `${kind.singular} deactivated`, {
        description: active
          ? `${item.name} can be picked again.`
          : `${item.name} can't be picked for new records. Records that already use it keep it.`,
      });
      this.items.reload();
    } catch (error) {
      toast.error(active ? "Couldn't reactivate" : "Couldn't deactivate", {
        description: toProblem(error).detail ?? 'Please try again.',
      });
    }
  }
}
