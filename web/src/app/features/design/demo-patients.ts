import { type DataTableSort } from '@hilom/ui/data-table';

/** Fake rows for the style guide's data table. Not real people. */
export interface DemoPatient {
  id: string;
  mrn: string;
  name: string;
  ward: string;
  admittedOn: string;
  status: 'open' | 'closed' | 'voided';
}

const FIRST = [
  'Ana',
  'Jose',
  'Maria',
  'Carlo',
  'Liza',
  'Ramon',
  'Grace',
  'Paolo',
  'Rosa',
  'Miguel',
];
const LAST = ['Santos', 'Reyes', 'Cruz', 'Bautista', 'Garcia', 'Mendoza', 'Torres', 'Flores'];
const WARDS = ['Ward 1 · General', 'Ward 3 · Medical', 'Ward 5 · Surgical', 'Pedia', 'ICU'];
const STATUSES: DemoPatient['status'][] = ['open', 'open', 'open', 'closed', 'voided'];

export const DEMO_PATIENTS: readonly DemoPatient[] = Array.from({ length: 57 }, (_, i) => ({
  id: `p-${i + 1}`,
  mrn: `MRN-${String(1201 + i).padStart(6, '0')}`,
  name: `${LAST[i % LAST.length]}, ${FIRST[(i * 7) % FIRST.length]}`,
  ward: WARDS[(i * 3) % WARDS.length]!,
  admittedOn: new Date(Date.UTC(2026, 9, 1 + (i % 30), 8 + (i % 10))).toISOString(),
  status: STATUSES[i % STATUSES.length]!,
}));

/** Stands in for an API call: sorts and pages on the "server", with a little latency. */
export function fetchDemoPage(
  page: number,
  pageSize: number,
  sort: DataTableSort | null,
): Promise<{ items: DemoPatient[]; total: number }> {
  const rows = [...DEMO_PATIENTS];
  if (sort) {
    const key = sort.id as keyof DemoPatient;
    rows.sort((a, b) => a[key].localeCompare(b[key]) * (sort.desc ? -1 : 1));
  }
  const items = rows.slice((page - 1) * pageSize, page * pageSize);
  return new Promise((resolve) => setTimeout(() => resolve({ items, total: rows.length }), 400));
}
