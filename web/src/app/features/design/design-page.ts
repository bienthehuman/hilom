import {
  ChangeDetectionStrategy,
  Component,
  inject,
  linkedSignal,
  resource,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmAlertDialogImports } from '@hilom/ui/alert-dialog';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { HlmCheckboxImports } from '@hilom/ui/checkbox';
import { DataTable, type DataTableColumn, type DataTableSort } from '@hilom/ui/data-table';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { HlmDropdownMenuImports } from '@hilom/ui/dropdown-menu';
import { EmptyState } from '@hilom/ui/empty-state';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { HlmLabelImports } from '@hilom/ui/label';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmSheetImports } from '@hilom/ui/sheet';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { STATUS_MAP, type StatusKind, StatusPill } from '@hilom/ui/status';
import { HlmSwitchImports } from '@hilom/ui/switch';
import { HlmTabsImports } from '@hilom/ui/tabs';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { HlmTooltipImports } from '@hilom/ui/tooltip';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCalendarPlus,
  lucideCircleAlert,
  lucideCircleCheck,
  lucideEllipsis,
  lucideInfo,
  lucidePencil,
  lucideTrash2,
  lucideTriangleAlert,
  lucideUserPlus,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { flexRenderComponent } from '@tanstack/angular-table';

import { type DemoPatient, fetchDemoPage } from './demo-patients';

interface DemoPage {
  items: DemoPatient[];
  total: number;
}

/**
 * Living style guide (docs/DESIGN_SYSTEM.md §9): every token, component and state in both themes.
 * New design-system components are added here in the same PR that creates them.
 */
@Component({
  selector: 'app-design-page',
  imports: [
    ReactiveFormsModule,
    NgIcon,
    PageHeader,
    StatusPill,
    EmptyState,
    FormField,
    DataTable,
    HlmAlertImports,
    HlmAlertDialogImports,
    HlmButtonImports,
    HlmCardImports,
    HlmCheckboxImports,
    HlmDialogImports,
    HlmDropdownMenuImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSelectImports,
    HlmSheetImports,
    HlmSkeletonImports,
    HlmSwitchImports,
    HlmTabsImports,
    HlmTextareaImports,
    HlmTooltipImports,
  ],
  providers: [
    provideIcons({
      lucideCalendarPlus,
      lucideCircleAlert,
      lucideCircleCheck,
      lucideEllipsis,
      lucideInfo,
      lucidePencil,
      lucideTrash2,
      lucideTriangleAlert,
      lucideUserPlus,
    }),
  ],
  templateUrl: './design-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DesignPage {
  protected readonly sections = [
    ['colors', 'Colors'],
    ['typography', 'Typography'],
    ['buttons', 'Buttons'],
    ['status', 'Status'],
    ['alerts', 'Alerts'],
    ['forms', 'Forms'],
    ['overlays', 'Overlays'],
    ['layout', 'Layout'],
    ['data-table', 'Data table'],
  ] as const;

  /** `text` is the token meant for text on that fill. Shift colors are for dots and bars only. */
  protected readonly colorTokens: readonly { name: string; fill: string; text?: string }[] = [
    { name: 'background', fill: 'bg-background', text: 'text-foreground' },
    { name: 'card', fill: 'bg-card', text: 'text-card-foreground' },
    { name: 'muted', fill: 'bg-muted', text: 'text-muted-foreground' },
    { name: 'primary', fill: 'bg-primary', text: 'text-primary-foreground' },
    { name: 'accent', fill: 'bg-accent', text: 'text-accent-foreground' },
    { name: 'destructive', fill: 'bg-destructive', text: 'text-destructive-foreground' },
    { name: 'success', fill: 'bg-success', text: 'text-success-foreground' },
    { name: 'warning', fill: 'bg-warning', text: 'text-warning-foreground' },
    { name: 'info', fill: 'bg-info', text: 'text-info-foreground' },
    { name: 'shift-am', fill: 'bg-shift-am' },
    { name: 'shift-pm', fill: 'bg-shift-pm' },
    { name: 'shift-noc', fill: 'bg-shift-noc' },
  ];

  protected readonly typeScale = [
    ['text-3xl font-bold', '28 / 36 · Dashboard numbers', '1,284'],
    ['text-2xl font-semibold', '22 / 30 · Page titles', 'Ward census'],
    ['text-xl font-semibold', '18 / 28 · Section headings', 'Current admissions'],
    ['text-lg font-medium', '16 / 24 · Card titles, portal body', 'Dela Cruz, Juan'],
    ['text-base', '14 / 20 · Staff body text', 'Admitted for observation after a fall at home.'],
    ['text-sm', '13 / 20 · Table cells, hints', 'MRN-001234 · Bed 3-02'],
    ['text-xs font-medium', '12 / 16 · Pills, metadata', 'Updated 08:30'],
  ] as const;

  protected readonly statusKinds = Object.entries(STATUS_MAP).map(([kind, statuses]) => ({
    kind: kind as StatusKind,
    statuses: Object.keys(statuses),
  }));

  // ---- Forms ----
  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly wards = ['Ward 1 · General', 'Ward 3 · Medical', 'Ward 5 · Surgical', 'ICU'];
  protected readonly form = this.fb.group({
    fullName: ['', [Validators.required, Validators.minLength(2)]],
    email: ['', [Validators.required, Validators.email]],
    ward: ['', Validators.required],
    notes: ['', Validators.maxLength(200)],
    notifyFamily: [false],
    active: [true],
  });

  protected submitDemoForm(): void {
    this.form.markAllAsTouched();
    if (this.form.valid) {
      toast.success('Patient saved', { description: this.form.controls.fullName.value });
    }
  }

  // ---- Toasts ----
  protected showToast(kind: 'success' | 'info' | 'warning' | 'error'): void {
    const messages = {
      success: ['Appointment confirmed', 'Mon 12 Nov, 08:00–08:30 with Dr. Santos'],
      info: ['Shift swap requested', 'Waiting for Reyes, A. to respond'],
      warning: ['Medication held', 'Reason recorded on the medication record'],
      error: ["Couldn't save vitals", 'Check your connection and try again'],
    } as const;
    const [title, description] = messages[kind];
    toast[kind](title, { description });
  }

  // ---- Data table (simulated server) ----
  protected readonly page = signal(1);
  protected readonly sort = signal<DataTableSort | null>({ id: 'name', desc: false });
  protected readonly simulateError = signal(false);
  protected readonly simulateEmpty = signal(false);
  protected readonly pageSize = 10;

  protected readonly patients = resource({
    params: () => ({
      page: this.page(),
      sort: this.sort(),
      fail: this.simulateError(),
      empty: this.simulateEmpty(),
    }),
    loader: async ({ params }): Promise<DemoPage> => {
      const result = await fetchDemoPage(params.page, this.pageSize, params.sort);
      if (params.fail) {
        throw new Error('The server took too long to respond.');
      }
      return params.empty ? { items: [], total: 0 } : result;
    },
  });

  /** Last loaded page. Kept while the next one loads so the pager doesn't jump. */
  protected readonly loaded = linkedSignal<DemoPage | undefined, DemoPage>({
    source: () => (this.patients.hasValue() ? this.patients.value() : undefined),
    computation: (next, previous) => next ?? previous?.value ?? { items: [], total: 0 },
  });

  protected readonly columns: DataTableColumn<DemoPatient>[] = [
    { accessorKey: 'mrn', header: 'MRN', enableSorting: true },
    { accessorKey: 'name', header: 'Patient', enableSorting: true },
    { accessorKey: 'ward', header: 'Ward', enableSorting: true },
    {
      accessorKey: 'admittedOn',
      header: 'Admitted',
      enableSorting: true,
      cell: ({ getValue }) =>
        new Intl.DateTimeFormat('en-PH', {
          dateStyle: 'medium',
          timeStyle: 'short',
          hourCycle: 'h23',
          timeZone: 'Asia/Manila',
        }).format(new Date(getValue<string>())),
    },
    {
      accessorKey: 'status',
      header: 'Encounter',
      cell: ({ row }) =>
        flexRenderComponent(StatusPill, {
          inputs: { kind: 'encounter', status: row.original.status },
        }),
    },
  ];

  protected readonly patientRowId = (row: DemoPatient) => row.id;

  protected changeSort(sort: DataTableSort | null): void {
    this.sort.set(sort);
    this.page.set(1);
  }

  protected errorMessage(): string | null {
    const error = this.patients.error();
    return error instanceof Error ? error.message : error ? 'Something went wrong.' : null;
  }
}
