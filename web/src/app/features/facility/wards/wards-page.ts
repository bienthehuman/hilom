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
import { Router, RouterLink } from '@angular/router';
import { HlmButtonImports } from '@hilom/ui/button';
import { DataTable, type DataTableColumn, type DataTableSort } from '@hilom/ui/data-table';
import { HlmInputImports } from '@hilom/ui/input';
import { HlmLabelImports } from '@hilom/ui/label';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSelectImports } from '@hilom/ui/select';
import { StatusPill } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideSearch } from '@ng-icons/lucide';
import { flexRenderComponent } from '@tanstack/angular-table';

import { Api } from '../../../api/api';
import { listWards } from '../../../api/fn/facility/list-wards';
import { type WardResponse } from '../../../api/models/ward-response';
import { type WardStatusFilter } from '../../../api/models/ward-status-filter';
import { type WardSummaryResponse } from '../../../api/models/ward-summary-response';
import { type WardType } from '../../../api/models/ward-type';
import { HasPermission } from '../../../core/auth/has-permission.directive';
import { labelOf } from '../../../core/format/options';
import { toProblem } from '../../../core/http/problem';
import { WardDialog } from './ward-dialog';
import { WARD_TYPES, bedCount } from './ward-labels';

const PAGE_SIZE = 25;
const ANY = 'any';
const DEFAULT_STATUS: WardStatusFilter = 'active';

interface WardPageData {
  items: WardSummaryResponse[];
  total: number;
}

/** Code and name, linking to the ward's page. */
@Component({
  selector: 'app-ward-name-cell',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a
      class="font-medium text-foreground underline-offset-4 hover:underline focus-visible:underline"
      [routerLink]="['/facility/wards', ward().id]"
    >
      <span data-numeric>{{ ward().code }}</span> · {{ ward().name }}
    </a>
  `,
})
export class WardNameCell {
  readonly ward = input.required<WardSummaryResponse>();
}

@Component({
  selector: 'app-ward-status-cell',
  imports: [StatusPill],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<ui-status-pill kind="lookup" [status]="active() ? 'active' : 'inactive'" />`,
})
export class WardStatusCell {
  readonly active = input.required<boolean>();
}

/** Every ward with its capacity (active beds). */
@Component({
  selector: 'app-wards-page',
  imports: [
    NgIcon,
    PageHeader,
    DataTable,
    HasPermission,
    WardDialog,
    HlmButtonImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSelectImports,
  ],
  providers: [provideIcons({ lucidePlus, lucideSearch })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-6xl">
      <ui-page-header
        title="Wards"
        description="Nursing units and their beds. Capacity is the number of beds in service."
      >
        <button
          *appHasPermission="'facility.wards.manage'"
          actions
          hlmBtn
          type="button"
          (click)="dialogOpen.set(true)"
        >
          <ng-icon name="lucidePlus" aria-hidden="true" data-icon="inline-start" />New ward
        </button>
      </ui-page-header>

      <div class="mb-4 flex flex-wrap items-end gap-3">
        <div class="flex w-full max-w-xs flex-col gap-1.5">
          <label hlmLabel for="ward-search">Search</label>
          <div class="relative">
            <ng-icon
              name="lucideSearch"
              aria-hidden="true"
              class="pointer-events-none absolute start-2.5 top-1/2 -translate-y-1/2 text-muted-foreground"
            />
            <input
              hlmInput
              id="ward-search"
              type="search"
              class="ps-8"
              placeholder="Code or name"
              autocomplete="off"
              (input)="onSearch($any($event.target).value)"
            />
          </div>
        </div>
        <div class="flex w-44 flex-col gap-1.5">
          <label hlmLabel for="ward-type-filter">Type</label>
          <hlm-select
            [value]="type()"
            [itemToString]="typeLabel"
            (valueChange)="setFilter(type, $any($event))"
          >
            <hlm-select-trigger buttonId="ward-type-filter" class="w-full">
              <hlm-select-value />
            </hlm-select-trigger>
            <hlm-select-content *hlmSelectPortal>
              <hlm-select-item value="any">All types</hlm-select-item>
              @for (option of types; track option.value) {
                <hlm-select-item [value]="option.value">{{ option.label }}</hlm-select-item>
              }
            </hlm-select-content>
          </hlm-select>
        </div>
        <div class="flex w-40 flex-col gap-1.5">
          <label hlmLabel for="ward-status-filter">Status</label>
          <hlm-select
            [value]="status()"
            [itemToString]="statusLabel"
            (valueChange)="setFilter(status, $any($event))"
          >
            <hlm-select-trigger buttonId="ward-status-filter" class="w-full">
              <hlm-select-value />
            </hlm-select-trigger>
            <hlm-select-content *hlmSelectPortal>
              @for (option of statusOptions; track option.value) {
                <hlm-select-item [value]="option.value">{{ option.label }}</hlm-select-item>
              }
            </hlm-select-content>
          </hlm-select>
        </div>
      </div>

      <ui-data-table
        caption="Wards"
        [emptyTitle]="isFiltered() ? 'No wards match' : 'No wards yet'"
        [emptyDescription]="
          isFiltered() ? 'Try a different search or filter.' : 'Add the first ward, then its beds.'
        "
        [emptyIcon]="isFiltered() ? 'lucideSearchX' : 'lucideInbox'"
        [columns]="columns"
        [rows]="loaded().items"
        [total]="loaded().total"
        [page]="page()"
        [pageSize]="pageSize"
        [sort]="sort()"
        [loading]="wards.isLoading()"
        [error]="loadError()"
        [rowId]="rowId"
        (pageChange)="page.set($event)"
        (sortChange)="onSort($event)"
        (retry)="wards.reload()"
      />
    </div>

    <app-ward-dialog [(open)]="dialogOpen" (saved)="opened($event)" />
  `,
})
export class WardsPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  protected readonly pageSize = PAGE_SIZE;
  protected readonly page = signal(1);
  protected readonly sort = signal<DataTableSort | null>({ id: 'code', desc: false });
  protected readonly search = signal('');
  protected readonly type = signal<WardType | typeof ANY>(ANY);
  protected readonly status = signal<WardStatusFilter | typeof ANY>(DEFAULT_STATUS);
  protected readonly dialogOpen = signal(false);
  private searchTimer?: ReturnType<typeof setTimeout>;

  protected readonly wards = resource({
    params: () => ({
      page: this.page(),
      sort: this.sort(),
      q: this.search(),
      type: this.type(),
      status: this.status(),
    }),
    loader: ({ params }): Promise<WardPageData> =>
      this.api.invoke(listWards, {
        page: params.page,
        pageSize: PAGE_SIZE,
        sort: params.sort?.desc ? '-code' : 'code',
        q: params.q || undefined,
        wardType: params.type === ANY ? undefined : params.type,
        status: params.status === ANY ? undefined : params.status,
      }),
  });

  protected readonly loaded = linkedSignal<WardPageData | undefined, WardPageData>({
    source: () => (this.wards.hasValue() ? this.wards.value() : undefined),
    computation: (next, previous) => next ?? previous?.value ?? { items: [], total: 0 },
  });

  protected readonly loadError = computed(() =>
    this.wards.error() ? (toProblem(this.wards.error()).detail ?? "Couldn't load wards.") : null,
  );

  protected readonly isFiltered = computed(
    () => this.search() !== '' || this.type() !== ANY || this.status() !== DEFAULT_STATUS,
  );

  protected readonly types = WARD_TYPES;
  protected readonly typeLabel = (value: string) =>
    value === ANY ? 'All types' : labelOf(WARD_TYPES, value);

  protected readonly statusOptions: readonly {
    value: WardStatusFilter | typeof ANY;
    label: string;
  }[] = [
    { value: ANY, label: 'All wards' },
    { value: 'active', label: 'Active' },
    { value: 'inactive', label: 'Inactive' },
  ];
  protected readonly statusLabel = (value: string) =>
    this.statusOptions.find((option) => option.value === value)?.label ?? value;

  protected readonly columns: DataTableColumn<WardSummaryResponse>[] = [
    {
      id: 'code',
      header: 'Ward',
      enableSorting: true,
      cell: ({ row }) => flexRenderComponent(WardNameCell, { inputs: { ward: row.original } }),
    },
    { id: 'type', header: 'Type', cell: ({ row }) => labelOf(WARD_TYPES, row.original.wardType) },
    {
      id: 'department',
      header: 'Department',
      cell: ({ row }) => row.original.department?.name ?? '—',
    },
    { id: 'capacity', header: 'Capacity', cell: ({ row }) => bedCount(row.original.capacity) },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) =>
        flexRenderComponent(WardStatusCell, { inputs: { active: row.original.isActive } }),
    },
  ];

  protected readonly rowId = (ward: WardSummaryResponse) => ward.id;

  protected onSearch(value: string): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.search.set(value.trim());
      this.page.set(1);
    }, 300);
  }

  protected setFilter<T>(filter: { set(value: T): void }, value: T): void {
    filter.set(value);
    this.page.set(1);
  }

  protected onSort(sort: DataTableSort | null): void {
    this.sort.set(sort);
    this.page.set(1);
  }

  /** A new ward has no beds yet, so go straight to its page to add them. */
  protected opened(ward: WardResponse): void {
    void this.router.navigate(['/facility/wards', ward.id]);
  }
}
