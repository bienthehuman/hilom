import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  linkedSignal,
  resource,
  signal,
  viewChild,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { FormField } from '@hilom/ui/form-field';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft, lucideCircleAlert } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { listMyLinks } from '../../api/fn/patients/list-my-links';
import { bookAppointment } from '../../api/fn/scheduling/book-appointment';
import { type OpenSlotResponse } from '../../api/models/open-slot-response';
import { formatLongDay, formatTimeRange, hospitalDateOf } from '../../core/format/date-time';
import { type Problem, toProblem } from '../../core/http/problem';
import { formatPatientName } from '../patients/patient-labels';
import { SlotPicker } from '../scheduling/slot-picker';

/** Book a visit for a linked patient the account may book for. The doctor confirms the request. */
@Component({
  selector: 'app-portal-book-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    NgIcon,
    FormField,
    SlotPicker,
    HlmAlertImports,
    HlmButtonImports,
    HlmSkeletonImports,
    HlmTextareaImports,
  ],
  providers: [provideIcons({ lucideArrowLeft, lucideCircleAlert })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a hlmBtn variant="ghost" class="-ms-3 mb-2" routerLink="/portal/appointments">
      <ng-icon name="lucideArrowLeft" aria-hidden="true" data-icon="inline-start" />Appointments
    </a>
    <h1 class="mb-6 text-2xl font-semibold tracking-tight">Book a visit</h1>

    @if (!links.hasValue() && !links.error()) {
      <div hlmSkeleton class="h-24 w-full rounded-lg" aria-busy="true"></div>
    } @else if (bookable().length === 0) {
      <div class="rounded-lg border bg-card p-6">
        <p class="font-medium">No patients you can book for</p>
        <p class="mt-1 text-muted-foreground">
          Booking needs a patient linked to your account with permission to book. Ask the front desk
          if that should change.
        </p>
        <a hlmBtn variant="outline" class="mt-4 w-full" routerLink="/portal/patients"
          >See your patients</a
        >
      </div>
    } @else {
      <form class="flex flex-col gap-6" [formGroup]="form" (ngSubmit)="book()" novalidate>
        <fieldset>
          <legend class="mb-2 font-medium">Who is the visit for?</legend>
          <div class="flex flex-col gap-2">
            @for (link of bookable(); track link.id) {
              <label
                class="flex min-h-11 cursor-pointer items-center gap-3 rounded-lg border bg-card px-4 py-2"
                [class.border-primary]="patientId() === link.patient?.id"
              >
                <input
                  type="radio"
                  name="book-patient"
                  class="size-4 accent-primary"
                  [value]="link.patient?.id"
                  [checked]="patientId() === link.patient?.id"
                  (change)="patientId.set(link.patient?.id ?? null); patientError.set(null)"
                />
                <span>
                  <span class="font-medium">{{ name(link.patient) }}</span>
                  <span class="block text-sm text-muted-foreground" data-numeric>{{
                    link.patient?.mrn
                  }}</span>
                </span>
              </label>
            }
          </div>
          @if (patientError(); as message) {
            <p class="mt-2 text-sm text-destructive" role="alert">{{ message }}</p>
          }
        </fieldset>

        <section aria-labelledby="book-time-heading">
          <h2 id="book-time-heading" class="mb-3 font-medium">When?</h2>
          <app-slot-picker [(selected)]="slot" />
          @if (slotError(); as message) {
            <p class="mt-2 text-sm text-destructive" role="alert">{{ message }}</p>
          }
        </section>

        <ui-form-field label="Why do they want to see the doctor?" for="portal-reason" required>
          <textarea hlmTextarea id="portal-reason" rows="3" formControlName="reason"></textarea>
        </ui-form-field>

        @if (problem(); as p) {
          <div hlmAlert variant="destructive" role="alert">
            <ng-icon name="lucideCircleAlert" aria-hidden="true" />
            <h2 hlmAlertTitle>Couldn't book</h2>
            <p hlmAlertDescription>{{ p.detail ?? 'Please try again.' }}</p>
          </div>
        }

        @if (slot(); as s) {
          <p class="text-center text-sm text-muted-foreground" data-numeric>{{ when(s) }}</p>
        }
        <button hlmBtn type="submit" class="w-full" [disabled]="booking()">
          {{ booking() ? 'Sending…' : 'Request appointment' }}
        </button>
      </form>
    }
  `,
})
export class PortalBookPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly picker = viewChild(SlotPicker);

  protected readonly links = resource({ loader: () => this.api.invoke(listMyLinks) });
  protected readonly bookable = computed(() =>
    (this.links.hasValue() ? this.links.value() : []).filter(
      (l) => l.status === 'active' && l.canBook && l.patient,
    ),
  );
  /** The only bookable patient is chosen for you. */
  protected readonly patientId = linkedSignal<string | null>(() =>
    this.bookable().length === 1 ? (this.bookable()[0]?.patient?.id ?? null) : null,
  );

  protected readonly slot = signal<OpenSlotResponse | null>(null);
  protected readonly slotError = signal<string | null>(null);
  protected readonly patientError = signal<string | null>(null);
  protected readonly reason = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(500)],
  });
  /** Gives the form a group, so submitting runs book() instead of reloading the page. */
  protected readonly form = new FormGroup({ reason: this.reason });
  protected readonly booking = signal(false);
  protected readonly problem = signal<Problem | null>(null);

  protected name(
    patient: { firstName: string; middleName?: string | null; lastName: string } | null | undefined,
  ): string {
    return patient ? formatPatientName(patient) : '';
  }

  protected when(slot: OpenSlotResponse): string {
    return `Dr. ${slot.physician.firstName} ${slot.physician.lastName} · ${formatLongDay(hospitalDateOf(slot.startsAt))}, ${formatTimeRange(slot.startsAt, slot.endsAt)}`;
  }

  protected async book(): Promise<void> {
    const patientId = this.patientId();
    const slot = this.slot();
    this.reason.markAsTouched();
    this.slotError.set(slot ? null : 'Choose a free time.');
    this.patientError.set(patientId ? null : 'Choose who the visit is for.');
    if (!patientId || !slot || this.reason.invalid || this.booking()) {
      return;
    }

    this.booking.set(true);
    this.problem.set(null);
    try {
      await this.api.invoke(bookAppointment, {
        body: { patientId, slotId: slot.id, reason: this.reason.value.trim() },
      });
      toast.success('Request sent', {
        description: `${this.when(slot)}. The doctor will confirm it.`,
      });
      await this.router.navigateByUrl('/portal/appointments');
    } catch (error) {
      const problem = toProblem(error);
      if (problem.code === 'slot_taken' || problem.code === 'slot_not_bookable') {
        this.slot.set(null);
        this.picker()?.refresh();
      }
      this.problem.set(problem);
    } finally {
      this.booking.set(false);
    }
  }
}
