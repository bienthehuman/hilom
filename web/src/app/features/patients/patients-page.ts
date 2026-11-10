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
import { formatAge } from '@hilom/ui/patient-header';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSearch, lucideUserPlus } from '@ng-icons/lucide';
import { flexRenderComponent } from '@tanstack/angular-table';

import { Api } from '../../api/api';
import { listPatients } from '../../api/fn/patients/list-patients';
import { type PatientSummaryResponse } from '../../api/models/patient-summary-response';
import { AuthService } from '../../core/auth/auth.service';
import { HasPermission } from '../../core/auth/has-permission.directive';
import { formatDate, hospitalToday } from '../../core/format/date-time';
import { toProblem } from '../../core/http/problem';
import { PatientNameCell } from './patient-cells';
import { SEXES, labelOf } from './patient-labels';

const PAGE_SIZE = 25;

interface PatientPageData {
  items: PatientSummaryResponse[];
  total: number;
}

/** The patient registry: find a patient by name, MRN or date of birth. */
@Component({
  selector: 'app-patients-page',
  imports: [
    RouterLink,
    NgIcon,
    PageHeader,
    DataTable,
    HasPermission,
    HlmButtonImports,
    HlmInputImports,
    HlmLabelImports,
  ],
  providers: [provideIcons({ lucideSearch, lucideUserPlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-6xl">
      <ui-page-header
        title="Patients"
        description="Search the registry before registering, so nobody gets two records."
      >
        <a *appHasPermission="'patients.register'" actions hlmBtn routerLink="/patients/new">
          <ng-icon name="lucideUserPlus" aria-hidden="true" data-icon="inline-start" />Register
          patient
        </a>
      </ui-page-header>

      <div class="mb-4 flex flex-wrap items-end gap-3">
        <div class="flex w-full max-w-xs flex-col gap-1.5">
          <label hlmLabel for="patient-search">Search</label>
          <div class="relative">
            <ng-icon
              name="lucideSearch"
              aria-hidden="true"
              class="pointer-events-none absolute start-2.5 top-1/2 -translate-y-1/2 text-muted-foreground"
            />
            <input
              hlmInput
              id="patient-search"
              type="search"
              class="ps-8"
              placeholder="Name or MRN"
              autocomplete="off"
              (input)="onSearch($any($event.target).value)"
            />
          </div>
        </div>
        <div class="flex w-44 flex-col gap-1.5">
          <label hlmLabel for="patient-birth-date">Date of birth</label>
          <input
            hlmInput
            id="patient-birth-date"
            type="date"
            [max]="today"
            (change)="setBirthDate($any($event.target).value)"
          />
        </div>
      </div>

      <ui-data-table
        caption="Patients"
        [emptyTitle]="isFiltered() ? 'No patients match' : 'No patients yet'"
        [emptyDescription]="
          isFiltered()
            ? 'Check the spelling or try the MRN. If they are new, register them.'
            : canRegister()
              ? 'Register the first patient to start the registry.'
              : 'Patients you can see will appear here.'
        "
        [emptyIcon]="isFiltered() ? 'lucideSearchX' : 'lucideInbox'"
        [columns]="columns"
        [rows]="loaded().items"
        [total]="loaded().total"
        [page]="page()"
        [pageSize]="pageSize"
        [sort]="sort()"
        [loading]="patients.isLoading()"
        [error]="loadError()"
        [rowId]="rowId"
        (pageChange)="page.set($event)"
        (sortChange)="onSort($event)"
        (retry)="patients.reload()"
      />
    </div>
  `,
})
export class PatientsPage {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);

  protected readonly today = hospitalToday();
  protected readonly pageSize = PAGE_SIZE;
  protected readonly page = signal(1);
  protected readonly sort = signal<DataTableSort | null>({ id: 'name', desc: false });
  protected readonly search = signal('');
  protected readonly birthDate = signal('');
  private searchTimer?: ReturnType<typeof setTimeout>;

  protected readonly canRegister = computed(() => this.auth.can('patients.register'));

  protected readonly patients = resource({
    params: () => ({
      page: this.page(),
      sort: this.sort(),
      q: this.search(),
      birthDate: this.birthDate(),
    }),
    loader: ({ params }): Promise<PatientPageData> =>
      this.api.invoke(listPatients, {
        page: params.page,
        pageSize: PAGE_SIZE,
        sort: params.sort ? `${params.sort.desc ? '-' : ''}${params.sort.id}` : undefined,
        q: params.q || undefined,
        birthDate: params.birthDate || undefined,
      }),
  });

  /** Last loaded page, kept while the next loads so the pager doesn't jump. */
  protected readonly loaded = linkedSignal<PatientPageData | undefined, PatientPageData>({
    source: () => (this.patients.hasValue() ? this.patients.value() : undefined),
    computation: (next, previous) => next ?? previous?.value ?? { items: [], total: 0 },
  });

  protected readonly loadError = computed(() =>
    this.patients.error()
      ? (toProblem(this.patients.error()).detail ?? "Couldn't load patients.")
      : null,
  );

  protected readonly isFiltered = computed(() => this.search() !== '' || this.birthDate() !== '');

  protected readonly columns: DataTableColumn<PatientSummaryResponse>[] = [
    {
      id: 'name',
      header: 'Name',
      enableSorting: true,
      cell: ({ row }) =>
        flexRenderComponent(PatientNameCell, { inputs: { patient: row.original } }),
    },
    {
      id: 'ageSex',
      header: 'Age · Sex',
      cell: ({ row }) =>
        `${formatAge(row.original.birthDate, this.today)} · ${labelOf(SEXES, row.original.sex)}`,
    },
    {
      id: 'birthDate',
      header: 'Date of birth',
      enableSorting: true,
      cell: ({ row }) => formatDate(row.original.birthDate),
    },
    {
      id: 'contact',
      header: 'Contact number',
      cell: ({ row }) => row.original.contactNumber,
    },
    {
      id: 'registeredAt',
      header: 'Registered',
      enableSorting: true,
      cell: ({ row }) => formatDate(hospitalToday(new Date(row.original.registeredAt))),
    },
  ];

  protected readonly rowId = (patient: PatientSummaryResponse) => patient.id;

  protected onSearch(value: string): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.search.set(value.trim());
      this.page.set(1);
    }, 300);
  }

  protected setBirthDate(value: string): void {
    this.birthDate.set(value);
    this.page.set(1);
  }

  protected onSort(sort: DataTableSort | null): void {
    this.sort.set(sort);
    this.page.set(1);
  }
}
