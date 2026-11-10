import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  resource,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { PageHeader } from '@hilom/ui/page-header';
import { PatientHeader, formatAge } from '@hilom/ui/patient-header';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { StatusPill } from '@hilom/ui/status';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideCircleCheck, lucideSearch } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { getPatient } from '../../api/fn/patients/get-patient';
import { listPatients } from '../../api/fn/patients/list-patients';
import { bookAppointment } from '../../api/fn/scheduling/book-appointment';
import { type AppointmentResponse } from '../../api/models/appointment-response';
import { type OpenSlotResponse } from '../../api/models/open-slot-response';
import { type PatientSummaryResponse } from '../../api/models/patient-summary-response';
import {
  formatDate,
  formatLongDay,
  formatTimeRange,
  hospitalDateOf,
  hospitalToday,
} from '../../core/format/date-time';
import { type Problem, toProblem } from '../../core/http/problem';
import { formatPatientName } from '../patients/patient-labels';
import { SlotPicker } from './slot-picker';

/** Staff book a free slot for any patient. Opened from the sidebar, or from a patient's record (?patientId=). */
@Component({
  selector: 'app-book-appointment-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    NgIcon,
    PageHeader,
    PatientHeader,
    FormField,
    StatusPill,
    SlotPicker,
    HlmAlertImports,
    HlmButtonImports,
    HlmCardImports,
    HlmInputImports,
    HlmSkeletonImports,
    HlmTextareaImports,
  ],
  providers: [provideIcons({ lucideCircleAlert, lucideCircleCheck, lucideSearch })],
  templateUrl: './book-appointment-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BookAppointmentPage {
  private readonly api = inject(Api);
  private readonly picker = viewChild(SlotPicker);

  /** From the query string, when booking from a patient's record. */
  readonly patientId = input<string>();

  protected readonly today = hospitalToday();

  // ---- Patient ----
  protected readonly chosenId = signal<string | null>(null);
  protected readonly query = signal('');
  private searchTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    effect(() => {
      const id = this.patientId();
      untracked(() => this.chosenId.set(id ?? null));
    });
  }

  protected readonly patient = resource({
    params: () => this.chosenId() ?? undefined,
    loader: ({ params }) => this.api.invoke(getPatient, { id: params }),
  });

  protected readonly results = resource({
    params: () => (this.query().length >= 2 ? this.query() : undefined),
    loader: ({ params }) => this.api.invoke(listPatients, { q: params, pageSize: 8 }),
  });
  protected readonly resultList = computed(() =>
    this.results.hasValue() ? this.results.value().items : [],
  );

  protected onSearch(value: string): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.query.set(value.trim()), 250);
  }

  protected choose(patient: PatientSummaryResponse): void {
    this.chosenId.set(patient.id);
  }

  protected changePatient(): void {
    this.chosenId.set(null);
    this.booked.set(null);
  }

  protected summaryOf(p: PatientSummaryResponse): string {
    return `${p.mrn} · ${formatAge(p.birthDate, this.today)} · born ${formatDate(p.birthDate)}`;
  }

  protected readonly patientName = formatPatientName;

  // ---- Slot and reason ----
  protected readonly slot = signal<OpenSlotResponse | null>(null);
  protected readonly slotError = signal<string | null>(null);
  protected readonly reason = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(500)],
  });

  // ---- Book ----
  protected readonly booking = signal(false);
  protected readonly problem = signal<Problem | null>(null);
  protected readonly booked = signal<AppointmentResponse | null>(null);

  protected async book(): Promise<void> {
    const patientId = this.chosenId();
    const slot = this.slot();
    this.reason.markAsTouched();
    this.slotError.set(slot ? null : 'Choose a free time.');
    if (!patientId || !slot || this.reason.invalid || this.booking()) {
      return;
    }

    this.booking.set(true);
    this.problem.set(null);
    try {
      const appointment = await this.api.invoke(bookAppointment, {
        body: { patientId, slotId: slot.id, reason: this.reason.value.trim() },
      });
      this.booked.set(appointment);
      toast.success('Appointment requested', { description: this.when(appointment) });
    } catch (error) {
      const problem = toProblem(error);
      if (problem.code === 'slot_taken' || problem.code === 'slot_not_bookable') {
        this.slot.set(null);
        this.picker()?.refresh();
      }
      const message = problem.errors?.['reason']?.[0];
      if (message) {
        this.reason.setErrors({ server: message });
      } else {
        this.problem.set(problem);
      }
    } finally {
      this.booking.set(false);
    }
  }

  protected bookAnother(): void {
    this.booked.set(null);
    this.slot.set(null);
    this.reason.reset('');
  }

  protected when(appointment: {
    startsAt: string;
    endsAt: string;
    physician: { firstName: string; lastName: string };
  }): string {
    return `Dr. ${appointment.physician.firstName} ${appointment.physician.lastName} · ${formatLongDay(hospitalDateOf(appointment.startsAt))}, ${formatTimeRange(appointment.startsAt, appointment.endsAt)}`;
  }
}
