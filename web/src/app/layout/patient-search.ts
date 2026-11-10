import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  resource,
  signal,
  viewChild,
} from '@angular/core';
import { Router } from '@angular/router';
import { HlmInputImports } from '@hilom/ui/input';
import { formatAge } from '@hilom/ui/patient-header';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSearch } from '@ng-icons/lucide';

import { Api } from '../api/api';
import { listPatients } from '../api/fn/patients/list-patients';
import { type PatientSummaryResponse } from '../api/models/patient-summary-response';
import { hospitalToday } from '../core/format/date-time';

const MAX_RESULTS = 6;
const MIN_QUERY = 2;

/**
 * Global patient search in the staff top bar (docs/DESIGN_SYSTEM.md §6). Type a name or MRN, pick with the
 * arrow keys and Enter, or press Enter on "See all results" for the registry. Ctrl/⌘+K focuses it.
 * Follows the ARIA combobox pattern. Results come from the same scoped list as the registry, and searching
 * isn't a record view: opening a patient is.
 */
@Component({
  selector: 'app-patient-search',
  imports: [NgIcon, HlmInputImports],
  providers: [provideIcons({ lucideSearch })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'relative block w-full max-w-sm',
    '(document:keydown)': 'onGlobalKey($event)',
    '(focusout)': 'onFocusOut($event)',
  },
  template: `
    <label for="patient-search-input" class="sr-only">Search patients</label>
    <ng-icon
      name="lucideSearch"
      aria-hidden="true"
      class="pointer-events-none absolute start-2.5 top-1/2 -translate-y-1/2 text-muted-foreground"
    />
    <input
      #input
      hlmInput
      id="patient-search-input"
      type="text"
      role="combobox"
      autocomplete="off"
      spellcheck="false"
      class="w-full ps-8 pe-14"
      placeholder="Search patients"
      aria-autocomplete="list"
      [attr.aria-controls]="expanded() ? 'patient-search-results' : null"
      [attr.aria-expanded]="expanded()"
      [attr.aria-activedescendant]="open() && active() >= 0 ? optionId(active()) : null"
      [value]="query()"
      (input)="onInput($any($event.target).value)"
      (keydown)="onKey($event)"
      (focus)="opened.set(true)"
    />
    <kbd
      aria-hidden="true"
      class="pointer-events-none absolute end-2 top-1/2 hidden -translate-y-1/2 rounded border bg-muted px-1.5 text-xs text-muted-foreground sm:block"
      >{{ shortcut }}</kbd
    >

    @if (open()) {
      <div
        class="absolute inset-x-0 top-full z-30 mt-1 overflow-hidden rounded-md border bg-popover text-popover-foreground shadow-md"
      >
        @if (status(); as message) {
          <p
            class="px-3 py-2 text-sm"
            [class]="results.error() ? 'text-destructive' : 'text-muted-foreground'"
          >
            {{ message }}
          </p>
        }
        @if (optionCount() > 0) {
          <ul id="patient-search-results" role="listbox" aria-label="Patients" class="py-1">
            @for (patient of items(); track patient.id; let i = $index) {
              <!-- Combobox pattern: focus stays in the input, which handles the keys; options are clicked, never focused. -->
              <!-- eslint-disable-next-line @angular-eslint/template/click-events-have-key-events, @angular-eslint/template/interactive-supports-focus -->
              <li
                role="option"
                [id]="optionId(i)"
                [attr.aria-selected]="active() === i"
                class="flex cursor-pointer items-baseline justify-between gap-3 px-3 py-2"
                [class.bg-accent]="active() === i"
                (mousedown)="$event.preventDefault()"
                (click)="openPatient(patient)"
                (mousemove)="active.set(i)"
              >
                <span class="truncate font-medium">{{ nameOf(patient) }}</span>
                <span class="shrink-0 text-xs text-muted-foreground" data-numeric
                  >{{ patient.mrn }} · {{ ageOf(patient) }}</span
                >
              </li>
            }
            @if (total() > 0) {
              <!-- Combobox pattern: focus stays in the input, which handles the keys; options are clicked, never focused. -->
              <!-- eslint-disable-next-line @angular-eslint/template/click-events-have-key-events, @angular-eslint/template/interactive-supports-focus -->
              <li
                role="option"
                [id]="optionId(items().length)"
                [attr.aria-selected]="active() === items().length"
                class="cursor-pointer border-t px-3 py-2 text-sm text-primary"
                [class.bg-accent]="active() === items().length"
                (mousedown)="$event.preventDefault()"
                (click)="seeAll()"
                (mousemove)="active.set(items().length)"
              >
                See all {{ total() }} {{ total() === 1 ? 'result' : 'results' }} in Patients
              </li>
            }
          </ul>
        }
      </div>
    }
    <p class="sr-only" aria-live="polite">{{ open() ? announcement() : '' }}</p>
  `,
})
export class PatientSearch {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly input = viewChild.required<ElementRef<HTMLInputElement>>('input');

  protected readonly shortcut = /Mac|iPhone|iPad/.test(globalThis.navigator?.platform ?? '')
    ? '⌘K'
    : 'Ctrl K';
  private readonly today = hospitalToday();
  private timer?: ReturnType<typeof setTimeout>;

  protected readonly query = signal('');
  /** The query actually searched, after typing pauses. */
  protected readonly debounced = signal('');
  protected readonly opened = signal(false);
  /** Highlighted option; the last index is "See all results". -1 = none. */
  protected readonly active = signal(-1);

  protected readonly results = resource({
    params: () => (this.debounced().length >= MIN_QUERY ? this.debounced() : undefined),
    loader: ({ params }) =>
      this.api.invoke(listPatients, { q: params, pageSize: MAX_RESULTS, sort: 'name' }),
  });

  protected readonly items = computed(() =>
    this.results.hasValue() ? this.results.value().items : [],
  );
  protected readonly total = computed(() =>
    this.results.hasValue() ? this.results.value().total : 0,
  );
  protected readonly open = computed(() => this.opened() && this.debounced().length >= MIN_QUERY);
  protected readonly optionCount = computed(() => this.items().length + (this.total() > 0 ? 1 : 0));
  protected readonly expanded = computed(() => this.open() && this.optionCount() > 0);

  /** Shown instead of results while searching, on error, or when nothing matches. */
  protected readonly status = computed(() => {
    if (this.results.error()) {
      return "Couldn't search. Try again.";
    }
    if (this.results.isLoading() && this.items().length === 0) {
      return 'Searching…';
    }
    return this.results.hasValue() && this.items().length === 0
      ? `No patients match "${this.debounced()}".`
      : null;
  });

  /** For screen readers: how many results there are once a search finishes. */
  protected readonly announcement = computed(() => {
    if (this.results.isLoading()) {
      return '';
    }
    return this.status() ?? `${this.total()} ${this.total() === 1 ? 'patient' : 'patients'} found.`;
  });

  protected optionId(index: number): string {
    return `patient-search-option-${index}`;
  }

  protected nameOf(patient: PatientSummaryResponse): string {
    return `${patient.lastName}, ${[patient.firstName, patient.middleName].filter(Boolean).join(' ')}`;
  }

  protected ageOf(patient: PatientSummaryResponse): string {
    return [formatAge(patient.birthDate, this.today), patient.sex === 'female' ? 'F' : 'M']
      .filter(Boolean)
      .join(' · ');
  }

  protected onInput(value: string): void {
    this.query.set(value);
    this.opened.set(true);
    this.active.set(-1);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.debounced.set(value.trim()), 250);
  }

  protected onKey(event: KeyboardEvent): void {
    const count = this.optionCount();
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.opened.set(true);
        if (count > 0) {
          this.active.set((this.active() + 1) % count);
        }
        break;
      case 'ArrowUp':
        event.preventDefault();
        if (count > 0) {
          this.active.set(this.active() <= 0 ? count - 1 : this.active() - 1);
        }
        break;
      case 'Enter': {
        event.preventDefault();
        const index = this.active();
        const patient = this.items()[index];
        if (patient) {
          this.openPatient(patient);
        } else if (this.query().trim() !== '') {
          // "See all", or Enter without choosing: the registry, filtered by what was typed.
          this.seeAll();
        }
        break;
      }
      case 'Escape':
        if (this.open()) {
          event.preventDefault();
          this.opened.set(false);
          this.active.set(-1);
        }
        break;
    }
  }

  /** Ctrl/⌘+K from anywhere in the staff app. */
  protected onGlobalKey(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      this.input().nativeElement.focus();
      this.input().nativeElement.select();
    }
  }

  protected onFocusOut(event: FocusEvent): void {
    if (!this.host.nativeElement.contains(event.relatedTarget as Node | null)) {
      this.opened.set(false);
      this.active.set(-1);
    }
  }

  protected openPatient(patient: PatientSummaryResponse): void {
    this.reset();
    void this.router.navigate(['/patients', patient.id]);
  }

  protected seeAll(): void {
    const q = this.query().trim();
    this.reset();
    void this.router.navigate(['/patients'], { queryParams: { q } });
  }

  private reset(): void {
    clearTimeout(this.timer);
    this.query.set('');
    this.debounced.set('');
    this.opened.set(false);
    this.active.set(-1);
    this.input().nativeElement.blur();
  }
}
