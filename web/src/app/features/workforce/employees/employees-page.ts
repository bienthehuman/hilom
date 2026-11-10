import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  linkedSignal,
  resource,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { HlmButtonImports } from '@hilom/ui/button';
import { DataTable, type DataTableColumn, type DataTableSort } from '@hilom/ui/data-table';
import { HlmInputImports } from '@hilom/ui/input';
import { HlmLabelImports } from '@hilom/ui/label';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSelectImports } from '@hilom/ui/select';
import { STATUS_MAP } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSearch, lucideUserPlus } from '@ng-icons/lucide';
import { flexRenderComponent } from '@tanstack/angular-table';

import { Api } from '../../../api/api';
import { listDepartments } from '../../../api/fn/workforce/list-departments';
import { listEmployees } from '../../../api/fn/workforce/list-employees';
import { type EmployeeStatus } from '../../../api/models/employee-status';
import { type EmployeeSummaryResponse } from '../../../api/models/employee-summary-response';
import { type ProfessionalType } from '../../../api/models/professional-type';
import { AuthService } from '../../../core/auth/auth.service';
import { HasPermission } from '../../../core/auth/has-permission.directive';
import { toProblem } from '../../../core/http/problem';
import { EmployeeNameCell, EmployeeStatusCell, LookupRefCell } from './employee-cells';
import { PROFESSIONAL_TYPES, labelOf } from './employee-labels';

const PAGE_SIZE = 25;
const ANY = 'any';
/** Separated staff are hidden until asked for. */
const DEFAULT_STATUS: EmployeeStatus = 'active';

interface EmployeePageData {
  items: EmployeeSummaryResponse[];
  total: number;
}

/** Everyone who works at the hospital, filtered by department, type and status. */
@Component({
  selector: 'app-employees-page',
  imports: [
    RouterLink,
    NgIcon,
    PageHeader,
    DataTable,
    HasPermission,
    HlmButtonImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSelectImports,
  ],
  providers: [provideIcons({ lucideSearch, lucideUserPlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-6xl">
      <ui-page-header
        title="Employees"
        description="Everyone who works at the hospital, whether or not they sign in."
      >
        <a
          *appHasPermission="'workforce.employees.manage'"
          actions
          hlmBtn
          routerLink="/workforce/employees/new"
        >
          <ng-icon name="lucideUserPlus" aria-hidden="true" data-icon="inline-start" />New employee
        </a>
      </ui-page-header>

      <div class="mb-4 flex flex-wrap items-end gap-3">
        <div class="flex w-full max-w-xs flex-col gap-1.5">
          <label hlmLabel for="employee-search">Search</label>
          <div class="relative">
            <ng-icon
              name="lucideSearch"
              aria-hidden="true"
              class="pointer-events-none absolute start-2.5 top-1/2 -translate-y-1/2 text-muted-foreground"
            />
            <input
              hlmInput
              id="employee-search"
              type="search"
              class="ps-8"
              placeholder="Name, employee no. or license"
              autocomplete="off"
              (input)="onSearch($any($event.target).value)"
            />
          </div>
        </div>
        <div class="flex w-48 flex-col gap-1.5">
          <label hlmLabel for="employee-department">Department</label>
          <hlm-select
            [value]="department()"
            [itemToString]="departmentLabel"
            (valueChange)="setFilter(department, $any($event))"
          >
            <hlm-select-trigger buttonId="employee-department" class="w-full">
              <hlm-select-value />
            </hlm-select-trigger>
            <hlm-select-content *hlmSelectPortal>
              <hlm-select-item value="any">All departments</hlm-select-item>
              @for (option of departmentOptions(); track option.id) {
                <hlm-select-item [value]="option.id">{{ option.name }}</hlm-select-item>
              }
            </hlm-select-content>
          </hlm-select>
        </div>
        <div class="flex w-44 flex-col gap-1.5">
          <label hlmLabel for="employee-type">Type</label>
          <hlm-select
            [value]="type()"
            [itemToString]="typeLabel"
            (valueChange)="setFilter(type, $any($event))"
          >
            <hlm-select-trigger buttonId="employee-type" class="w-full">
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
          <label hlmLabel for="employee-status">Status</label>
          <hlm-select
            [value]="status()"
            [itemToString]="statusLabel"
            (valueChange)="setFilter(status, $any($event))"
          >
            <hlm-select-trigger buttonId="employee-status" class="w-full">
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
        caption="Employees"
        [emptyTitle]="isFiltered() ? 'No employees match' : 'No employees yet'"
        [emptyDescription]="
          isFiltered()
            ? 'Try a different search or filter.'
            : canManage()
              ? 'Add the first employee to start building the roster.'
              : 'Employee records you can see will appear here.'
        "
        [emptyIcon]="isFiltered() ? 'lucideSearchX' : 'lucideInbox'"
        [columns]="columns"
        [rows]="loaded().items"
        [total]="loaded().total"
        [page]="page()"
        [pageSize]="pageSize"
        [sort]="sort()"
        [loading]="employees.isLoading()"
        [error]="loadError()"
        [rowId]="rowId"
        (pageChange)="page.set($event)"
        (sortChange)="onSort($event)"
        (retry)="employees.reload()"
      />
    </div>
  `,
})
export class EmployeesPage {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);

  protected readonly pageSize = PAGE_SIZE;
  protected readonly page = signal(1);
  protected readonly sort = signal<DataTableSort | null>({ id: 'name', desc: false });
  protected readonly search = signal('');
  protected readonly department = signal<string>(ANY);
  protected readonly type = signal<ProfessionalType | typeof ANY>(ANY);
  protected readonly status = signal<EmployeeStatus | typeof ANY>(DEFAULT_STATUS);
  private searchTimer?: ReturnType<typeof setTimeout>;

  protected readonly canManage = computed(() => this.auth.can('workforce.employees.manage'));

  protected readonly employees = resource({
    params: () => ({
      page: this.page(),
      sort: this.sort(),
      q: this.search(),
      department: this.department(),
      type: this.type(),
      status: this.status(),
    }),
    loader: ({ params }): Promise<EmployeePageData> =>
      this.api.invoke(listEmployees, {
        page: params.page,
        pageSize: PAGE_SIZE,
        sort: params.sort ? `${params.sort.desc ? '-' : ''}${params.sort.id}` : undefined,
        q: params.q || undefined,
        departmentId: params.department === ANY ? undefined : params.department,
        professionalType: params.type === ANY ? undefined : params.type,
        status: params.status === ANY ? undefined : params.status,
      }),
  });

  /** Last loaded page, kept while the next loads so the pager doesn't jump. */
  protected readonly loaded = linkedSignal<EmployeePageData | undefined, EmployeePageData>({
    source: () => (this.employees.hasValue() ? this.employees.value() : undefined),
    computation: (next, previous) => next ?? previous?.value ?? { items: [], total: 0 },
  });

  protected readonly loadError = computed(() =>
    this.employees.error()
      ? (toProblem(this.employees.error()).detail ?? "Couldn't load employees.")
      : null,
  );

  protected readonly isFiltered = computed(
    () =>
      this.search() !== '' ||
      this.department() !== ANY ||
      this.type() !== ANY ||
      this.status() !== DEFAULT_STATUS,
  );

  // Departments for the filter, including inactive ones: old records still use them.
  private readonly departments = resource({
    loader: () => this.api.invoke(listDepartments, { pageSize: 100 }),
  });
  protected readonly departmentOptions = computed(() =>
    this.departments.hasValue() ? this.departments.value().items : [],
  );
  protected readonly departmentLabel = (value: string): string =>
    value === ANY
      ? 'All departments'
      : (this.departmentOptions().find((d) => d.id === value)?.name ?? '');

  protected readonly types = PROFESSIONAL_TYPES;
  protected readonly typeLabel = (value: string): string =>
    value === ANY ? 'All types' : labelOf(PROFESSIONAL_TYPES, value);

  protected readonly statusOptions: readonly {
    value: EmployeeStatus | typeof ANY;
    label: string;
  }[] = [
    { value: ANY, label: 'All statuses' },
    ...Object.entries(STATUS_MAP.employee).map(([value, appearance]) => ({
      value: value as EmployeeStatus,
      label: appearance.label,
    })),
  ];
  protected readonly statusLabel = (value: string): string =>
    this.statusOptions.find((option) => option.value === value)?.label ?? value;

  protected readonly columns: DataTableColumn<EmployeeSummaryResponse>[] = [
    {
      id: 'name',
      header: 'Name',
      enableSorting: true,
      cell: ({ row }) =>
        flexRenderComponent(EmployeeNameCell, { inputs: { employee: row.original } }),
    },
    {
      id: 'department',
      header: 'Department',
      cell: ({ row }) =>
        flexRenderComponent(LookupRefCell, { inputs: { ref: row.original.department } }),
    },
    {
      id: 'position',
      header: 'Position',
      cell: ({ row }) =>
        flexRenderComponent(LookupRefCell, { inputs: { ref: row.original.position } }),
    },
    {
      id: 'type',
      header: 'Type',
      cell: ({ row }) => labelOf(PROFESSIONAL_TYPES, row.original.professionalType),
    },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) =>
        flexRenderComponent(EmployeeStatusCell, { inputs: { status: row.original.status } }),
    },
  ];

  protected readonly rowId = (employee: EmployeeSummaryResponse) => employee.id;

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
}
