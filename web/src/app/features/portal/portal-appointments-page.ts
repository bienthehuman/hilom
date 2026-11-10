import { ChangeDetectionStrategy, Component } from '@angular/core';
import { EmptyState } from '@hilom/ui/empty-state';
import { provideIcons } from '@ng-icons/core';
import { lucideCalendarDays } from '@ng-icons/lucide';

/** Appointments for linked patients. Booking comes with slices 2.6 and 2.7. */
@Component({
  selector: 'app-portal-appointments-page',
  imports: [EmptyState],
  providers: [provideIcons({ lucideCalendarDays })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="mb-6 text-2xl font-semibold tracking-tight">Appointments</h1>
    <div class="rounded-lg border bg-card p-6">
      <ui-empty-state
        icon="lucideCalendarDays"
        title="No appointments yet"
        description="Once a patient is linked to your account, their appointments appear here."
      />
    </div>
  `,
})
export class PortalAppointmentsPage {}
