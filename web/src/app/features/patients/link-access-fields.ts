import { ChangeDetectionStrategy, Component, model } from '@angular/core';
import { HlmCheckboxImports } from '@hilom/ui/checkbox';
import { HlmLabelImports } from '@hilom/ui/label';

/** What a linked portal account may do for the patient: book visits, and see records. */
@Component({
  selector: 'app-link-access-fields',
  imports: [HlmCheckboxImports, HlmLabelImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <fieldset class="flex flex-col gap-1">
      <legend class="mb-2 text-sm font-medium">The account may</legend>
      <div class="flex items-start gap-3 rounded-md px-2 py-2 hover:bg-muted">
        <hlm-checkbox
          class="mt-0.5"
          inputId="link-can-book"
          [checked]="canBook()"
          (checkedChange)="canBook.set($event)"
        />
        <label hlmLabel for="link-can-book" class="flex flex-col items-start gap-0.5">
          <span>Book and cancel visits</span>
          <span class="text-xs font-normal text-muted-foreground">For this patient</span>
        </label>
      </div>
      <div class="flex items-start gap-3 rounded-md px-2 py-2 hover:bg-muted">
        <hlm-checkbox
          class="mt-0.5"
          inputId="link-can-view"
          [checked]="canViewRecords()"
          (checkedChange)="canViewRecords.set($event)"
        />
        <label hlmLabel for="link-can-view" class="flex flex-col items-start gap-0.5">
          <span>See records</span>
          <span class="text-xs font-normal text-muted-foreground"
            >Details, allergies, and visit summaries</span
          >
        </label>
      </div>
    </fieldset>
  `,
})
export class LinkAccessFields {
  readonly canBook = model(true);
  readonly canViewRecords = model(false);
}
