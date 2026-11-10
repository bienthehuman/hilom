import { ChangeDetectionStrategy, Component } from '@angular/core';
import { EmptyState } from '@hilom/ui/empty-state';
import { provideIcons } from '@ng-icons/core';
import { lucideUsersRound } from '@ng-icons/lucide';

/** Patients linked to this account. Linking itself (request, staff verification) comes with slice 2.4. */
@Component({
  selector: 'app-portal-patients-page',
  imports: [EmptyState],
  providers: [provideIcons({ lucideUsersRound })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="mb-1 text-2xl font-semibold tracking-tight">Patients</h1>
    <p class="mb-6 text-muted-foreground">
      You, or the family members you help, once the hospital links them to your account.
    </p>
    <div class="rounded-lg border bg-card p-6">
      <ui-empty-state
        icon="lucideUsersRound"
        title="No linked patients yet"
        description="For your privacy, the hospital checks who you are before linking a patient's record to your account. Linked patients appear here."
      />
    </div>
  `,
})
export class PortalPatientsPage {}
