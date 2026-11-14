import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  resource,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { EmptyState } from '@hilom/ui/empty-state';
import { FormField } from '@hilom/ui/form-field';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { StatusPill } from '@hilom/ui/status';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCalendarDays,
  lucideCalendarPlus,
  lucideCircleAlert,
  lucideHourglass,
  lucideUserRoundPlus,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { cancelAppointment } from '../../api/fn/scheduling/cancel-appointment';
import { listMyLinks } from '../../api/fn/patients/list-my-links';
import { listMyAppointments } from '../../api/fn/scheduling/list-my-appointments';
import { type AppointmentResponse } from '../../api/models/appointment-response';
import { formatLongDay, formatTimeRange, hospitalDateOf } from '../../core/format/date-time';
import { type Problem, toProblem } from '../../core/http/problem';
import { formatPatientName } from '../patients/patient-labels';

interface PatientAppointments {
  patientId: string;
  name: string;
  upcoming: AppointmentResponse[];
  past: AppointmentResponse[];
}

/**
 * Each linked patient's visits: upcoming (cancel when allowed) and the last six months. It's the portal's
 * home tab, so with nothing to show it says what to do first: link a patient, wait for verification, or book.
 */
@Component({
  selector: 'app-portal-appointments-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    NgIcon,
    EmptyState,
    FormField,
    StatusPill,
    HlmAlertImports,
    HlmButtonImports,
    HlmDialogImports,
    HlmSkeletonImports,
    HlmTextareaImports,
  ],
  providers: [
    provideIcons({
      lucideCalendarDays,
      lucideCalendarPlus,
      lucideCircleAlert,
      lucideHourglass,
      lucideUserRoundPlus,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="mb-6 text-2xl font-semibold tracking-tight">Appointments</h1>

    @if (loadError(); as message) {
      <div hlmAlert variant="destructive">
        <ng-icon name="lucideCircleAlert" aria-hidden="true" />
        <h2 hlmAlertTitle>Couldn't load appointments</h2>
        <p hlmAlertDescription>{{ message }}</p>
        <div hlmAlertAction>
          <button hlmBtn variant="outline" type="button" (click)="appointments.reload()">
            Try again
          </button>
        </div>
      </div>
    } @else if (!appointments.hasValue() || (byPatient().length === 0 && links.isLoading())) {
      <div hlmSkeleton class="h-24 w-full rounded-lg" aria-busy="true"></div>
    } @else if (byPatient().length === 0) {
      <div class="rounded-lg border bg-card p-6">
        @switch (start()) {
          @case ('link') {
            <ui-empty-state
              icon="lucideUserRoundPlus"
              title="Link a patient to start booking"
              description="Link yourself, or someone you care for, to their hospital record. The hospital checks it first, then you can book visits here."
            >
              <a hlmBtn routerLink="/portal/patients/link">
                <ng-icon
                  name="lucideUserRoundPlus"
                  aria-hidden="true"
                  data-icon="inline-start"
                />Link a patient
              </a>
            </ui-empty-state>
          }
          @case ('waiting') {
            <ui-empty-state
              icon="lucideHourglass"
              title="Waiting for the hospital"
              description="The hospital is checking your link request. You can book visits once it's verified."
            >
              <a hlmBtn variant="outline" routerLink="/portal/patients">See your patients</a>
            </ui-empty-state>
          }
          @case ('cannot_book') {
            <ui-empty-state
              icon="lucideCalendarDays"
              title="No appointments"
              description="Your links don't allow booking. Ask the front desk if that should change."
            >
              <a hlmBtn variant="outline" routerLink="/portal/patients">See your patients</a>
            </ui-empty-state>
          }
          @default {
            <ui-empty-state
              icon="lucideCalendarDays"
              title="No upcoming appointments"
              description="Book a visit for a patient linked to your account."
            >
              <a hlmBtn routerLink="/portal/appointments/book">
                <ng-icon
                  name="lucideCalendarPlus"
                  aria-hidden="true"
                  data-icon="inline-start"
                />Book a visit
              </a>
            </ui-empty-state>
          }
        }
      </div>
    } @else {
      @for (group of byPatient(); track group.patientId) {
        <section class="mb-8" [attr.aria-label]="group.name">
          <h2 class="mb-3 text-lg font-medium">{{ group.name }}</h2>

          @if (group.upcoming.length === 0) {
            <p class="mb-3 text-muted-foreground">No upcoming appointments.</p>
          } @else {
            <ul class="grid gap-3 md:grid-cols-2" [attr.aria-label]="'Upcoming for ' + group.name">
              @for (a of group.upcoming; track a.id) {
                <li class="rounded-lg border bg-card p-4">
                  <div class="flex flex-wrap items-center justify-between gap-2">
                    <p class="font-medium" data-numeric>{{ when(a) }}</p>
                    <ui-status-pill kind="appointment" [status]="a.status" />
                  </div>
                  <p>
                    Dr. {{ a.physician.firstName }} {{ a.physician.lastName }} ·
                    {{ a.physician.specialization }}
                  </p>
                  <p class="mt-2 text-sm">
                    @if (a.status === 'requested') {
                      Waiting for the doctor to confirm.
                    } @else {
                      Confirmed.{{ a.decisionNote ? ' ' + a.decisionNote : '' }}
                    }
                  </p>
                  @if (a.canCancel) {
                    <button
                      hlmBtn
                      variant="outline"
                      type="button"
                      class="mt-3 w-full"
                      (click)="openCancel(a)"
                    >
                      Cancel this appointment
                    </button>
                  }
                </li>
              }
            </ul>
          }

          @if (group.past.length > 0) {
            <h3 class="mt-5 mb-2 text-sm font-medium text-muted-foreground">Past and cancelled</h3>
            <ul class="grid gap-2 md:grid-cols-2" [attr.aria-label]="'Past for ' + group.name">
              @for (a of group.past; track a.id) {
                <li class="rounded-lg border bg-card px-4 py-3 text-sm">
                  <div class="flex flex-wrap items-center justify-between gap-2">
                    <span data-numeric>{{ when(a) }}</span>
                    <ui-status-pill kind="appointment" [status]="a.status" />
                  </div>
                  <p class="text-muted-foreground">
                    Dr. {{ a.physician.firstName }} {{ a.physician.lastName }}
                  </p>
                  @if (a.status === 'declined' && a.decisionNote) {
                    <p class="mt-1">The doctor said: "{{ a.decisionNote }}"</p>
                  }
                  @if (a.status === 'cancelled' && a.cancelReason) {
                    <p class="mt-1">Cancelled: "{{ a.cancelReason }}"</p>
                  }
                </li>
              }
            </ul>
          }
        </section>
      }
      <a hlmBtn variant="outline" class="w-full md:w-auto" routerLink="/portal/appointments/book">
        <ng-icon name="lucideCalendarPlus" aria-hidden="true" data-icon="inline-start" />Book
        another visit
      </a>
    }

    <hlm-dialog [state]="cancelling() ? 'open' : 'closed'" (closed)="cancelling.set(null)">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="sm:max-w-md">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>Cancel this appointment?</h2>
          <p hlmDialogDescription>
            @if (cancelling(); as a) {
              {{ when(a) }}, Dr. {{ a.physician.lastName }}. The time is offered to others.
            }
          </p>
        </hlm-dialog-header>
        <form
          id="portal-cancel-form"
          class="flex flex-col gap-4"
          [formGroup]="cancelForm"
          (ngSubmit)="cancel()"
          novalidate
        >
          <ui-form-field label="Why are you cancelling?" for="portal-cancel-reason" required>
            <textarea
              hlmTextarea
              id="portal-cancel-reason"
              rows="3"
              formControlName="reason"
            ></textarea>
          </ui-form-field>
          @if (problem(); as p) {
            <p class="text-sm text-destructive" role="alert">{{ p.detail }}</p>
          }
        </form>
        <hlm-dialog-footer>
          <button hlmBtn variant="outline" type="button" (click)="cancelling.set(null)">
            Keep it
          </button>
          <button
            hlmBtn
            variant="destructive"
            type="submit"
            form="portal-cancel-form"
            [disabled]="saving()"
          >
            {{ saving() ? 'Cancelling…' : 'Cancel appointment' }}
          </button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class PortalAppointmentsPage {
  private readonly api = inject(Api);

  protected readonly appointments = resource({
    loader: () => this.api.invoke(listMyAppointments),
  });
  protected readonly loadError = computed(() =>
    this.appointments.error()
      ? (toProblem(this.appointments.error()).detail ?? 'Please try again in a moment.')
      : null,
  );

  /** Only needed for the empty state; if it fails, the page falls back to suggesting a booking. */
  protected readonly links = resource({ loader: () => this.api.invoke(listMyLinks) });

  /** What a user with no appointments should do next. */
  protected readonly start = computed<'link' | 'waiting' | 'cannot_book' | 'book'>(() => {
    if (!this.links.hasValue()) {
      return 'book';
    }
    const links = this.links.value();
    if (links.some((l) => l.status === 'active' && l.canBook)) {
      return 'book';
    }
    if (links.some((l) => l.status === 'active')) {
      return 'cannot_book';
    }
    return links.some((l) => l.status === 'pending') ? 'waiting' : 'link';
  });

  /** One group per patient, upcoming soonest first, past most recent first. */
  protected readonly byPatient = computed<PatientAppointments[]>(() => {
    const now = Date.now();
    const groups = new Map<string, PatientAppointments>();
    for (const a of this.appointments.hasValue() ? this.appointments.value() : []) {
      const group = groups.get(a.patient.id) ?? {
        patientId: a.patient.id,
        name: formatPatientName(a.patient),
        upcoming: [],
        past: [],
      };
      const live = a.status === 'requested' || a.status === 'confirmed';
      if (live && new Date(a.endsAt).getTime() > now) {
        group.upcoming.push(a);
      } else {
        group.past.unshift(a);
      }
      groups.set(a.patient.id, group);
    }
    return [...groups.values()];
  });

  protected when(a: AppointmentResponse): string {
    return `${formatLongDay(hospitalDateOf(a.startsAt))}, ${formatTimeRange(a.startsAt, a.endsAt)}`;
  }

  // ---- Cancel ----
  protected readonly cancelling = signal<AppointmentResponse | null>(null);
  protected readonly saving = signal(false);
  protected readonly problem = signal<Problem | null>(null);
  protected readonly cancelForm = new FormGroup({
    reason: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(500)],
    }),
  });

  protected openCancel(a: AppointmentResponse): void {
    this.cancelForm.reset({ reason: '' });
    this.problem.set(null);
    this.cancelling.set(a);
  }

  protected async cancel(): Promise<void> {
    const a = this.cancelling();
    this.cancelForm.markAllAsTouched();
    const reason = this.cancelForm.controls.reason.value.trim();
    if (!a || this.cancelForm.invalid || !reason || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.problem.set(null);
    try {
      await this.api.invoke(cancelAppointment, { id: a.id, body: { reason, version: a.version } });
      this.cancelling.set(null);
      toast.success('Appointment cancelled', { description: this.when(a) });
      this.appointments.reload();
    } catch (error) {
      this.problem.set(toProblem(error));
      this.appointments.reload();
    } finally {
      this.saving.set(false);
    }
  }
}
