import {
  ChangeDetectionStrategy,
  Component,
  type ComponentRef,
  Directive,
  effect,
  inject,
  input,
  type OnDestroy,
  output,
  signal,
  untracked,
  ViewContainerRef,
} from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { HlmAlertDialogImports } from '@hilom/ui/alert-dialog';
import { HlmButtonImports } from '@hilom/ui/button';
import { FormField } from '@hilom/ui/form-field';
import { HlmTextareaImports } from '@hilom/ui/textarea';

/**
 * What a confirmed action does. Resolve when it worked (the dialog closes); throw to keep the dialog
 * open and show the error. A thrown value with `errors.reason` or `detail` (a ProblemDetails) is shown
 * as is.
 */
export type ConfirmActionHandler = (reason: string) => Promise<unknown>;

/** The ProblemDetails fields the dialog reads from a failed action. */
interface ProblemLike {
  detail?: string | null;
  errors?: Record<string, string[]> | null;
}

function problemOf(error: unknown): ProblemLike {
  const candidate = (error as { error?: unknown })?.error ?? error;
  return candidate && typeof candidate === 'object' ? (candidate as ProblemLike) : {};
}

let nextId = 0;

/** The alert dialog behind `uiConfirmAction`. Use the directive, not this directly. */
@Component({
  selector: 'ui-confirm-action-dialog',
  imports: [
    ReactiveFormsModule,
    FormField,
    HlmAlertDialogImports,
    HlmButtonImports,
    HlmTextareaImports,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-alert-dialog [state]="open() ? 'open' : 'closed'" (closed)="closed.emit()">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <hlm-alert-dialog-header>
          <h2 hlmAlertDialogTitle>{{ title() }}</h2>
          @if (description()) {
            <p hlmAlertDialogDescription>{{ description() }}</p>
          }
        </hlm-alert-dialog-header>
        <form
          [id]="formId"
          class="flex flex-col gap-3"
          (submit)="$event.preventDefault(); confirm()"
          novalidate
        >
          @if (requireReason()) {
            <ui-form-field
              [label]="reasonLabel()"
              [for]="formId + '-reason'"
              [hint]="reasonHint()"
              required
            >
              <textarea
                hlmTextarea
                [id]="formId + '-reason'"
                rows="3"
                [formControl]="reason"
              ></textarea>
            </ui-form-field>
          }
          @if (error(); as message) {
            <p class="text-sm text-destructive" role="alert">{{ message }}</p>
          }
        </form>
        <hlm-alert-dialog-footer>
          <button hlmAlertDialogCancel type="button" [disabled]="busy()">Cancel</button>
          <button
            hlmAlertDialogAction
            type="submit"
            [attr.form]="formId"
            [variant]="destructive() ? 'destructive' : 'default'"
            [disabled]="busy()"
          >
            {{ busy() ? 'Saving…' : confirmLabel() }}
          </button>
        </hlm-alert-dialog-footer>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class ConfirmActionDialog {
  readonly open = input(false);
  readonly title = input.required<string>();
  readonly description = input('');
  readonly confirmLabel = input('Confirm');
  readonly destructive = input(false);
  readonly requireReason = input(false);
  readonly reasonLabel = input('Reason');
  readonly reasonHint = input('');
  readonly maxReasonLength = input(500);
  readonly action = input.required<ConfirmActionHandler>();
  readonly closed = output();

  protected readonly formId = `ui-confirm-action-${nextId++}`;
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly reason = new FormControl('', { nonNullable: true });

  constructor() {
    // A fresh form every time it opens.
    effect(() => {
      if (this.open()) {
        untracked(() => {
          this.reason.reset('');
          this.reason.setValidators(
            this.requireReason()
              ? [Validators.required, Validators.maxLength(this.maxReasonLength())]
              : [],
          );
          this.reason.updateValueAndValidity();
          this.error.set(null);
        });
      }
    });
  }

  protected async confirm(): Promise<void> {
    if (this.busy()) {
      return;
    }
    const reason = this.reason.value.trim();
    if (this.requireReason() && !reason) {
      this.reason.setValue('');
      this.reason.markAsTouched();
      return;
    }
    if (this.reason.invalid) {
      this.reason.markAsTouched();
      return;
    }
    this.error.set(null);
    this.busy.set(true);
    try {
      await this.action()(reason);
      this.closed.emit();
    } catch (error) {
      const problem = problemOf(error);
      const reasonError = problem.errors?.['reason']?.[0];
      if (reasonError && this.requireReason()) {
        this.reason.setErrors({ server: reasonError });
        this.reason.markAsTouched();
      } else {
        this.error.set(problem.detail ?? 'Something went wrong. Please try again.');
      }
    } finally {
      this.busy.set(false);
    }
  }
}

/**
 * Asks before doing something hard to undo (docs/DESIGN_SYSTEM.md §7). Put it on a button; clicking
 * opens an alert dialog, and confirming runs the action. With `requireReason` the person must type
 * why, e.g. for voids and discharges, and the action receives it.
 *
 * ```html
 * <button hlmBtn [uiConfirmAction]="voidReading(v)" confirmTitle="Void this reading?"
 *   confirmLabel="Void" requireReason destructive>Void</button>
 * ```
 */
@Directive({
  selector: '[uiConfirmAction]',
  host: { 'aria-haspopup': 'dialog', '(click)': 'show()' },
})
export class ConfirmAction implements OnDestroy {
  private readonly container = inject(ViewContainerRef);
  private dialog: ComponentRef<ConfirmActionDialog> | null = null;

  readonly action = input.required<ConfirmActionHandler>({ alias: 'uiConfirmAction' });
  readonly confirmTitle = input.required<string>();
  readonly confirmDescription = input('');
  readonly confirmLabel = input('Confirm');
  readonly destructive = input(false, { transform: (v: boolean | '') => v !== false });
  readonly requireReason = input(false, { transform: (v: boolean | '') => v !== false });
  readonly reasonLabel = input('Reason');
  readonly reasonHint = input('');

  /** Opens the dialog. Called on click; also usable from a template reference. */
  show(): void {
    if (!this.dialog) {
      this.dialog = this.container.createComponent(ConfirmActionDialog);
      this.dialog.instance.closed.subscribe(() => this.dialog?.setInput('open', false));
    }
    const d = this.dialog;
    d.setInput('title', this.confirmTitle());
    d.setInput('description', this.confirmDescription());
    d.setInput('confirmLabel', this.confirmLabel());
    d.setInput('destructive', this.destructive());
    d.setInput('requireReason', this.requireReason());
    d.setInput('reasonLabel', this.reasonLabel());
    d.setInput('reasonHint', this.reasonHint());
    d.setInput('action', this.action());
    d.setInput('open', true);
  }

  ngOnDestroy(): void {
    this.dialog?.destroy();
  }
}
