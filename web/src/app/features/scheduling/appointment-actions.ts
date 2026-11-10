import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { FormField } from '@hilom/ui/form-field';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { checkIn } from '../../api/fn/clinical/check-in';
import { cancelAppointment } from '../../api/fn/scheduling/cancel-appointment';
import { confirmAppointment } from '../../api/fn/scheduling/confirm-appointment';
import { declineAppointment } from '../../api/fn/scheduling/decline-appointment';
import { markNoShow } from '../../api/fn/scheduling/mark-no-show';
import { type AppointmentResponse } from '../../api/models/appointment-response';
import { AuthService } from '../../core/auth/auth.service';
import { hospitalToday } from '../../core/format/date-time';
import { type Problem, toProblem } from '../../core/http/problem';

type ReasonAction = 'decline' | 'cancel';

/**
 * What staff can do with an appointment right now, as buttons: confirm or decline a request
 * (`appointments.decide`), check a confirmed patient in on the day and mark a no-show once a
 * confirmed visit has started (`appointments.check_in`), cancel before it starts (the API's
 * `canCancel`), and open the visit a check-in started (`clinical.encounters.view`). The API checks
 * scope and state again.
 */
@Component({
  selector: 'app-appointment-actions',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    FormField,
    HlmButtonImports,
    HlmDialogImports,
    HlmTextareaImports,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-wrap items-center justify-end gap-2' },
  template: `
    @if (appointment().encounterId && canOpenVisit()) {
      <a
        hlmBtn
        size="sm"
        variant="outline"
        [routerLink]="['/clinical/encounters', appointment().encounterId]"
      >
        Open visit<span class="sr-only"> {{ label() }}</span>
      </a>
    }
    @if (canCheckIn()) {
      <button hlmBtn size="sm" type="button" [disabled]="busy()" (click)="checkIn()">
        Check in<span class="sr-only"> {{ label() }}</span>
      </button>
    }
    @if (canDecide()) {
      <button hlmBtn size="sm" type="button" [disabled]="busy()" (click)="confirm()">
        Confirm<span class="sr-only"> {{ label() }}</span>
      </button>
      <button hlmBtn size="sm" variant="outline" type="button" (click)="open('decline')">
        Decline<span class="sr-only"> {{ label() }}</span>
      </button>
    }
    @if (canNoShow()) {
      <button
        hlmBtn
        size="sm"
        variant="outline"
        type="button"
        [disabled]="busy()"
        (click)="noShow()"
      >
        No-show<span class="sr-only"> {{ label() }}</span>
      </button>
    }
    @if (appointment().canCancel) {
      <button hlmBtn size="sm" variant="ghost" type="button" (click)="open('cancel')">
        Cancel<span class="sr-only"> {{ label() }}</span>
      </button>
    }

    <hlm-dialog [state]="reasonFor() ? 'open' : 'closed'" (closed)="reasonFor.set(null)">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="sm:max-w-md">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>
            {{ reasonFor() === 'decline' ? 'Decline request' : 'Cancel appointment' }}
          </h2>
          <p hlmDialogDescription>
            {{ label() }}.
            {{
              reasonFor() === 'decline'
                ? 'The reason is shown to the patient, so say what to do next.'
                : 'The time becomes free for others to book.'
            }}
          </p>
        </hlm-dialog-header>
        <form
          id="appointment-reason-form"
          class="flex flex-col gap-4"
          [formGroup]="form"
          (ngSubmit)="submitReason()"
          novalidate
        >
          <ui-form-field label="Reason" for="appointment-reason" required>
            <textarea
              hlmTextarea
              id="appointment-reason"
              rows="3"
              formControlName="reason"
            ></textarea>
          </ui-form-field>
          @if (problem(); as p) {
            <p class="text-sm text-destructive" role="alert">{{ p.detail }}</p>
          }
        </form>
        <hlm-dialog-footer>
          <button hlmBtn variant="outline" type="button" (click)="reasonFor.set(null)">Back</button>
          <button
            hlmBtn
            variant="destructive"
            type="submit"
            form="appointment-reason-form"
            [disabled]="busy()"
          >
            {{ reasonFor() === 'decline' ? 'Decline request' : 'Cancel appointment' }}
          </button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class AppointmentActions {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);

  readonly appointment = input.required<AppointmentResponse>();
  /** "Juan Dela Cruz, Mon 16 Nov 09:00", for screen readers and the dialog. */
  readonly label = input.required<string>();
  /** The appointment after a change; the parent refreshes its list. */
  readonly changed = output<AppointmentResponse>();

  private readonly started = computed(
    () => new Date(this.appointment().startsAt).getTime() <= Date.now(),
  );
  protected readonly canDecide = computed(
    () =>
      this.appointment().status === 'requested' &&
      !this.started() &&
      this.auth.can('scheduling.appointments.decide'),
  );
  /** Confirmed, and its day is today in hospital time: early or late that day is fine. */
  protected readonly canCheckIn = computed(
    () =>
      this.appointment().status === 'confirmed' &&
      hospitalToday(new Date(this.appointment().startsAt)) === hospitalToday() &&
      this.auth.can('scheduling.appointments.check_in'),
  );
  protected readonly canOpenVisit = computed(() => this.auth.can('clinical.encounters.view'));
  protected readonly canNoShow = computed(
    () =>
      this.appointment().status === 'confirmed' &&
      this.started() &&
      this.auth.can('scheduling.appointments.check_in'),
  );

  protected readonly busy = signal(false);
  protected readonly problem = signal<Problem | null>(null);
  protected readonly reasonFor = signal<ReasonAction | null>(null);
  protected readonly form = new FormGroup({
    reason: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(500)],
    }),
  });

  protected open(action: ReasonAction): void {
    this.form.reset({ reason: '' });
    this.problem.set(null);
    this.reasonFor.set(action);
  }

  protected async confirm(): Promise<void> {
    const a = this.appointment();
    await this.run(
      () =>
        this.api.invoke(confirmAppointment, { id: a.id, body: { note: null, version: a.version } }),
      'Appointment confirmed',
    );
  }

  protected async checkIn(): Promise<void> {
    const a = this.appointment();
    await this.run(async () => {
      await this.api.invoke(checkIn, { body: { appointmentId: a.id, version: a.version } });
      return a;
    }, 'Checked in');
  }

  protected async noShow(): Promise<void> {
    const a = this.appointment();
    await this.run(
      () => this.api.invoke(markNoShow, { id: a.id, body: { version: a.version } }),
      'Marked as a no-show',
    );
  }

  protected async submitReason(): Promise<void> {
    this.form.markAllAsTouched();
    const action = this.reasonFor();
    const reason = this.form.controls.reason.value.trim();
    if (!action || this.form.invalid || !reason) {
      return;
    }
    const a = this.appointment();
    const done = await this.run(
      () =>
        action === 'decline'
          ? this.api.invoke(declineAppointment, {
              id: a.id,
              body: { reason, version: a.version },
            })
          : this.api.invoke(cancelAppointment, { id: a.id, body: { reason, version: a.version } }),
      action === 'decline' ? 'Request declined' : 'Appointment cancelled',
    );
    if (done) {
      this.reasonFor.set(null);
    }
  }

  private async run(call: () => Promise<AppointmentResponse>, message: string): Promise<boolean> {
    if (this.busy()) {
      return false;
    }
    this.busy.set(true);
    this.problem.set(null);
    try {
      const updated = await call();
      toast.success(message, { description: this.label() });
      this.changed.emit(updated);
      return true;
    } catch (error) {
      const problem = toProblem(error);
      if (this.reasonFor()) {
        this.problem.set(problem);
      } else {
        toast.error("Couldn't update the appointment", { description: problem.detail });
      }
      // Someone else acted first: refresh so the list shows what happened.
      if (problem.status === 409) {
        this.changed.emit(this.appointment());
      }
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}
