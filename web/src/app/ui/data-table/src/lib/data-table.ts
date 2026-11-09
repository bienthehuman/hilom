import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  input,
  output,
} from '@angular/core';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { EmptyState } from '@hilom/ui/empty-state';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { HlmTableImports } from '@hilom/ui/table';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowDown,
  lucideArrowUp,
  lucideArrowUpDown,
  lucideChevronLeft,
  lucideChevronRight,
  lucideCircleAlert,
} from '@ng-icons/lucide';
import {
  type ColumnDef,
  FlexRender,
  type Header,
  type RowData,
  type SortingState,
  type Updater,
  injectTable,
  rowSortingFeature,
  tableFeatures,
} from '@tanstack/angular-table';

export const dataTableFeatures = tableFeatures({ rowSortingFeature });

export type DataTableColumn<T extends RowData> = ColumnDef<typeof dataTableFeatures, T>;

/** Sort as the API expects it: one column id, ascending or descending. */
export interface DataTableSort {
  id: string;
  desc: boolean;
}

const SKELETON_ROWS = 5;

/**
 * Server-driven table: the API sorts and pages, this component shows one page and asks for others.
 * Columns are TanStack column definitions; a column is sortable only when it sets `enableSorting: true`.
 */
@Component({
  selector: 'ui-data-table',
  imports: [
    FlexRender,
    HlmTableImports,
    HlmButtonImports,
    HlmSkeletonImports,
    HlmAlertImports,
    EmptyState,
    NgIcon,
  ],
  providers: [
    provideIcons({
      lucideArrowUp,
      lucideArrowDown,
      lucideArrowUpDown,
      lucideChevronLeft,
      lucideChevronRight,
      lucideCircleAlert,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <div class="overflow-hidden rounded-lg border bg-card">
      <div hlmTableContainer class="max-h-(--data-table-max-height,none) overflow-auto">
        <table hlmTable [attr.aria-busy]="loading() || null">
          <caption class="sr-only">
            {{
              caption()
            }}
          </caption>
          <thead hlmTHead class="sticky top-0 z-10 bg-muted">
            @for (headerGroup of table.getHeaderGroups(); track headerGroup.id) {
              <tr hlmTr class="hover:bg-transparent">
                @for (header of headerGroup.headers; track header.id) {
                  <th
                    hlmTh
                    scope="col"
                    class="text-xs font-semibold text-muted-foreground"
                    [attr.aria-sort]="ariaSort(header)"
                  >
                    @if (!header.isPlaceholder) {
                      @if (header.column.getCanSort()) {
                        <button
                          type="button"
                          class="-mx-1.5 inline-flex items-center gap-1 rounded-sm px-1.5 py-1 hover:bg-background hover:text-foreground"
                          (click)="header.column.getToggleSortingHandler()?.($event)"
                        >
                          <ng-container *flexRenderHeader="header; let value">{{
                            value
                          }}</ng-container>
                          <ng-icon [name]="sortIcon(header)" aria-hidden="true" class="size-3.5" />
                        </button>
                      } @else {
                        <ng-container *flexRenderHeader="header; let value">{{
                          value
                        }}</ng-container>
                      }
                    }
                  </th>
                }
              </tr>
            }
          </thead>

          <tbody hlmTBody>
            @if (error()) {
              <tr hlmTr class="hover:bg-transparent">
                <td hlmTd [attr.colspan]="columnCount()" class="p-4">
                  <div hlmAlert variant="destructive">
                    <ng-icon name="lucideCircleAlert" aria-hidden="true" />
                    <h3 hlmAlertTitle>Couldn't load {{ caption().toLowerCase() }}</h3>
                    <p hlmAlertDescription>{{ error() }}</p>
                    <div hlmAlertAction>
                      <button
                        hlmBtn
                        variant="outline"
                        size="sm"
                        type="button"
                        (click)="retry.emit()"
                      >
                        Try again
                      </button>
                    </div>
                  </div>
                </td>
              </tr>
            } @else if (loading()) {
              @for (row of skeletonRows; track row) {
                <tr hlmTr class="hover:bg-transparent">
                  @for (column of table.getAllLeafColumns(); track column.id) {
                    <td hlmTd><div hlmSkeleton class="h-4 w-3/4"></div></td>
                  }
                </tr>
              }
            } @else if (rows().length === 0) {
              <tr hlmTr class="hover:bg-transparent">
                <td hlmTd [attr.colspan]="columnCount()">
                  <ui-empty-state
                    [title]="emptyTitle()"
                    [description]="emptyDescription()"
                    [icon]="emptyIcon()"
                  >
                    <ng-content select="[empty-action]" />
                  </ui-empty-state>
                </td>
              </tr>
            } @else {
              @for (row of table.getRowModel().rows; track row.id) {
                <tr hlmTr>
                  @for (cell of row.getAllCells(); track cell.id) {
                    <td hlmTd>
                      <ng-container *flexRenderCell="cell; let value">{{ value }}</ng-container>
                    </td>
                  }
                </tr>
              }
            }
          </tbody>
        </table>
      </div>

      @if (!error() && total() > 0) {
        <div class="flex flex-wrap items-center justify-between gap-3 border-t px-4 py-2.5">
          <p class="text-sm text-muted-foreground" aria-live="polite" data-numeric>
            Showing {{ rangeStart() }}–{{ rangeEnd() }} of {{ total() }}
          </p>
          <div class="flex items-center gap-2">
            <span class="text-sm text-muted-foreground" data-numeric
              >Page {{ page() }} of {{ pageCount() }}</span
            >
            <button
              hlmBtn
              variant="outline"
              size="icon-sm"
              type="button"
              aria-label="Previous page"
              [disabled]="page() <= 1 || loading()"
              (click)="pageChange.emit(page() - 1)"
            >
              <ng-icon name="lucideChevronLeft" aria-hidden="true" />
            </button>
            <button
              hlmBtn
              variant="outline"
              size="icon-sm"
              type="button"
              aria-label="Next page"
              [disabled]="page() >= pageCount() || loading()"
              (click)="pageChange.emit(page() + 1)"
            >
              <ng-icon name="lucideChevronRight" aria-hidden="true" />
            </button>
          </div>
        </div>
      }
    </div>
  `,
})
export class DataTable<T extends RowData> {
  readonly columns = input.required<DataTableColumn<T>[]>();
  readonly rows = input.required<T[]>();
  /** Total matching rows on the server, across all pages. */
  readonly total = input.required<number>();
  /** 1-based page number, as the API uses. */
  readonly page = input(1);
  readonly pageSize = input(25);
  readonly sort = input<DataTableSort | null>(null);
  readonly loading = input(false, { transform: booleanAttribute });
  readonly error = input<string | null>(null);
  /** Accessible name for the table, e.g. "Appointments". */
  readonly caption = input.required<string>();
  readonly emptyTitle = input('Nothing here yet');
  readonly emptyDescription = input<string>();
  readonly emptyIcon = input('lucideInbox');
  readonly rowId = input<(row: T) => string>();

  readonly pageChange = output<number>();
  readonly sortChange = output<DataTableSort | null>();
  readonly retry = output();

  protected readonly skeletonRows = Array.from({ length: SKELETON_ROWS }, (_, i) => i);

  private readonly sorting = computed<SortingState>(() => {
    const sort = this.sort();
    return sort ? [sort] : [];
  });

  protected readonly table = injectTable(() => {
    const rowId = this.rowId();
    return {
      features: dataTableFeatures,
      columns: this.columns(),
      data: this.rows(),
      getRowId: rowId ? (row: T) => rowId(row) : undefined,
      manualSorting: true,
      // Columns opt in to sorting. (Table-level enableSorting: false would disable it for every column.)
      defaultColumn: { enableSorting: false },
      state: { sorting: this.sorting() },
      onSortingChange: (updater: Updater<SortingState>) => {
        const next = typeof updater === 'function' ? updater(this.sorting()) : updater;
        this.sortChange.emit(next[0] ?? null);
      },
    };
  });

  protected readonly columnCount = computed(() => this.columns().length);
  protected readonly pageCount = computed(() =>
    Math.max(1, Math.ceil(this.total() / this.pageSize())),
  );
  protected readonly rangeStart = computed(() =>
    this.total() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1,
  );
  protected readonly rangeEnd = computed(() =>
    Math.min(this.page() * this.pageSize(), this.total()),
  );

  protected ariaSort(header: Header<typeof dataTableFeatures, T, unknown>): string | null {
    if (!header.column.getCanSort()) {
      return null;
    }
    const sorted = header.column.getIsSorted();
    return sorted === 'asc' ? 'ascending' : sorted === 'desc' ? 'descending' : 'none';
  }

  protected sortIcon(header: Header<typeof dataTableFeatures, T, unknown>): string {
    const sorted = header.column.getIsSorted();
    return sorted === 'asc'
      ? 'lucideArrowUp'
      : sorted === 'desc'
        ? 'lucideArrowDown'
        : 'lucideArrowUpDown';
  }
}
