import {
  ChangeDetectionStrategy,
  Component,
  Directive,
  ElementRef,
  computed,
  contentChild,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { HlmButtonImports } from '@hilom/ui/button';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEye, lucideEyeOff } from '@ng-icons/lucide';

/** Marks the projected `<input>` of a `ui-password-input`, and leaves room for the show/hide button. */
@Directive({
  selector: 'input[uiPassword]',
  host: { type: 'password', '[style.padding-inline-end]': "'2.75rem'" },
})
export class PasswordInputControl {
  readonly element = inject<ElementRef<HTMLInputElement>>(ElementRef);
}

/**
 * A password field with its own show/hide button, so every password field (including "confirm") can be
 * checked by eye. Wrap the input; the form field and validation work as for any input.
 *
 * ```html
 * <ui-form-field label="Confirm new password" for="confirm-password" required>
 *   <ui-password-input what="new password">
 *     <input hlmInput uiPassword id="confirm-password" formControlName="confirm" autocomplete="new-password" />
 *   </ui-password-input>
 * </ui-form-field>
 * ```
 */
@Component({
  selector: 'ui-password-input',
  imports: [NgIcon, HlmButtonImports],
  providers: [provideIcons({ lucideEye, lucideEyeOff })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'relative block' },
  template: `
    <ng-content />
    <button
      hlmBtn
      variant="ghost"
      size="icon-sm"
      type="button"
      class="absolute end-1 top-1/2 -translate-y-1/2"
      [attr.aria-label]="buttonLabel()"
      [attr.aria-controls]="controlId()"
      [attr.aria-pressed]="visible()"
      (click)="visible.set(!visible())"
    >
      <ng-icon [name]="visible() ? 'lucideEyeOff' : 'lucideEye'" aria-hidden="true" />
    </button>
  `,
})
export class PasswordInput {
  /** What the button names, when a form has several password fields: "Show <what>". */
  readonly what = input('password');

  protected readonly visible = signal(false);
  protected readonly buttonLabel = computed(
    () => `${this.visible() ? 'Hide' : 'Show'} ${this.what()}`,
  );

  private readonly control = contentChild.required(PasswordInputControl);
  protected readonly controlId = computed(() => this.control().element.nativeElement.id || null);

  constructor() {
    effect(() => {
      this.control().element.nativeElement.type = this.visible() ? 'text' : 'password';
    });
  }
}

export const PasswordInputImports = [PasswordInput, PasswordInputControl] as const;
