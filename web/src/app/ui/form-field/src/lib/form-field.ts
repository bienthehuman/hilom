import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  inject,
  input,
} from '@angular/core';
import { HlmField, HlmFieldImports } from '@hilom/ui/field';
import { BrnField } from '@spartan-ng/brain/field';

import { firstErrorMessage } from './validation-messages';

/**
 * Label + control + hint + error, wired for screen readers by Spartan's field primitives.
 * The error appears once the field is touched or the form is submitted, not while typing the first time.
 *
 * ```html
 * <ui-form-field label="Email" for="email" hint="Used to sign in" required>
 *   <input hlmInput id="email" type="email" formControlName="email" />
 * </ui-form-field>
 * ```
 *
 * The field directive sits on this element (not inside the template) so the projected control,
 * which is declared in the parent template, can find and register with it.
 */
@Component({
  selector: 'ui-form-field',
  imports: [HlmFieldImports],
  hostDirectives: [HlmField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <label hlmFieldLabel [attr.for]="for()">
      {{ label() }}
      @if (required()) {
        <span class="text-destructive" aria-hidden="true">*</span>
        <span class="sr-only">(required)</span>
      }
    </label>
    <ng-content />
    @if (hint()) {
      <p hlmFieldDescription>{{ hint() }}</p>
    }
    <hlm-field-error>{{ errorMessage() }}</hlm-field-error>
  `,
})
export class FormField {
  private readonly field = inject(BrnField);

  readonly label = input.required<string>();
  /** The id of the projected control, so clicking the label focuses it. */
  readonly for = input.required<string>();
  readonly hint = input<string>();
  readonly required = input(false, { transform: booleanAttribute });

  protected readonly errorMessage = computed(() =>
    firstErrorMessage(this.field.errors(), this.label()),
  );
}
