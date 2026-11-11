import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';

export interface BedPickerBed {
  id: string;
  label: string;
}

export interface BedPickerWard {
  id: string;
  code: string;
  name: string;
  beds: readonly BedPickerBed[];
}

let nextId = 0;

/**
 * Choose one bed, grouped by ward: `<ui-bed-picker [wards]="freeBeds" [(value)]="bedId" />`.
 * Native radio buttons styled as tiles, so arrow keys move between beds and screen readers announce
 * "Ward 3, Bed 2". Pass only the beds that can be chosen (e.g. free ones); a ward with none is skipped.
 */
@Component({
  selector: 'ui-bed-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    @for (ward of wards(); track ward.id) {
      @if (ward.beds.length > 0) {
        <fieldset [attr.aria-describedby]="describedBy() || null">
          <legend class="mb-2 text-sm font-medium">
            <span data-numeric>{{ ward.code }}</span>
            <span class="font-normal text-muted-foreground"> · {{ ward.name }}</span>
            <span class="font-normal text-muted-foreground"> · {{ countLabel(ward) }}</span>
          </legend>
          <div class="grid grid-cols-[repeat(auto-fill,minmax(6.5rem,1fr))] gap-2">
            @for (bed of ward.beds; track bed.id) {
              <label
                class="relative flex min-h-11 cursor-pointer items-center justify-center rounded-md border bg-card px-3 py-2 text-sm font-medium transition-colors hover:bg-accent has-checked:border-primary has-checked:bg-primary has-checked:text-primary-foreground has-focus-visible:ring-[3px] has-focus-visible:ring-ring/50"
              >
                <input
                  type="radio"
                  class="sr-only"
                  [name]="name"
                  [value]="bed.id"
                  [checked]="value() === bed.id"
                  [attr.aria-label]="ward.code + ', ' + bed.label"
                  (change)="value.set(bed.id)"
                />
                <span data-numeric>{{ bed.label }}</span>
              </label>
            }
          </div>
        </fieldset>
      }
    }
  `,
})
export class BedPicker {
  /** Wards with the beds that can be chosen, in display order. */
  readonly wards = input.required<readonly BedPickerWard[]>();
  /** The chosen bed's id. */
  readonly value = model<string | null>(null);
  /** Id of a hint or error to announce with each ward's beds. */
  readonly describedBy = input<string>();

  protected readonly name = `ui-bed-picker-${nextId++}`;

  protected countLabel(ward: BedPickerWard): string {
    return ward.beds.length === 1 ? '1 free bed' : `${ward.beds.length} free beds`;
  }
}
