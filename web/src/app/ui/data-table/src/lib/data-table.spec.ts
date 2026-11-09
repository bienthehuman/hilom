import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { DataTable, type DataTableColumn, type DataTableSort } from './data-table';

interface Row {
  id: string;
  name: string;
  ward: string;
}

@Component({
  imports: [DataTable],
  template: `
    <ui-data-table
      caption="Patients"
      emptyTitle="No patients"
      [columns]="columns"
      [rows]="rows()"
      [total]="total()"
      [page]="page()"
      [pageSize]="2"
      [sort]="sort()"
      [loading]="loading()"
      [error]="error()"
      [rowId]="rowId"
      (pageChange)="pageRequests.push($event)"
      (sortChange)="sortRequests.push($event)"
      (retry)="retries = retries + 1"
    />
  `,
})
class Host {
  readonly columns: DataTableColumn<Row>[] = [
    { accessorKey: 'name', header: 'Name', enableSorting: true },
    { accessorKey: 'ward', header: 'Ward' },
  ];
  readonly rows = signal<Row[]>([
    { id: '1', name: 'Cruz, Ana', ward: 'Ward 3' },
    { id: '2', name: 'Reyes, Jose', ward: 'ICU' },
  ]);
  readonly total = signal(5);
  readonly page = signal(1);
  readonly sort = signal<DataTableSort | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly rowId = (row: Row) => row.id;
  readonly pageRequests: number[] = [];
  readonly sortRequests: (DataTableSort | null)[] = [];
  retries = 0;
}

describe('DataTable', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const refresh = async () => {
      fixture.detectChanges();
      await fixture.whenStable();
    };
    return { host: fixture.componentInstance, el, refresh };
  }

  const bodyRows = (el: HTMLElement) => el.querySelectorAll('tbody tr');
  const button = (el: HTMLElement, label: string) =>
    Array.from(el.querySelectorAll('button')).find(
      (b) => b.getAttribute('aria-label') === label || b.textContent?.trim() === label,
    ) as HTMLButtonElement;

  it('renders the current page and the range', async () => {
    const { el } = await render();

    expect(bodyRows(el)).toHaveLength(2);
    expect(el.textContent).toContain('Cruz, Ana');
    expect(el.textContent).toContain('Showing 1–2 of 5');
    expect(el.textContent).toContain('Page 1 of 3');
    expect(el.querySelector('caption')?.textContent?.trim()).toBe('Patients');
  });

  it('marks only sortable columns with aria-sort', async () => {
    const { el, host, refresh } = await render();
    const headers = el.querySelectorAll('th');

    expect(headers[0]?.getAttribute('aria-sort')).toBe('none');
    expect(headers[1]?.hasAttribute('aria-sort')).toBe(false);

    host.sort.set({ id: 'name', desc: true });
    await refresh();
    expect(el.querySelector('th')?.getAttribute('aria-sort')).toBe('descending');
  });

  it('asks the server for a new sort when a header is clicked', async () => {
    const { el, host } = await render();

    button(el, 'Name').click();

    expect(host.sortRequests).toEqual([{ id: 'name', desc: false }]);
  });

  it('asks for the next page and disables previous on the first page', async () => {
    const { el, host } = await render();

    expect(button(el, 'Previous page').disabled).toBe(true);
    button(el, 'Next page').click();

    expect(host.pageRequests).toEqual([2]);
  });

  it('shows skeleton rows while loading', async () => {
    const { el, host, refresh } = await render();

    host.loading.set(true);
    await refresh();

    expect(el.querySelector('table')?.getAttribute('aria-busy')).toBe('true');
    expect(el.querySelectorAll('[data-slot="skeleton"], [hlmSkeleton]').length).toBeGreaterThan(0);
  });

  it('shows the empty state when there are no rows', async () => {
    const { el, host, refresh } = await render();

    host.rows.set([]);
    host.total.set(0);
    await refresh();

    expect(el.textContent).toContain('No patients');
    expect(el.textContent).not.toContain('Showing');
  });

  it('shows the error with a retry button', async () => {
    const { el, host, refresh } = await render();

    host.error.set('The server took too long to respond.');
    await refresh();
    button(el, 'Try again').click();

    expect(el.textContent).toContain("Couldn't load patients");
    expect(host.retries).toBe(1);
  });
});
