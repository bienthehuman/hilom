import { ChangeDetectionStrategy, Component, computed, inject, resource } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { EmptyState } from '@hilom/ui/empty-state';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { StatusPill } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCircleAlert,
  lucideClipboardList,
  lucideUserPlus,
  lucideUsersRound,
} from '@ng-icons/lucide';

import { Api } from '../../api/api';
import { listMyLinks } from '../../api/fn/patients/list-my-links';
import { type MyLinkResponse } from '../../api/models/my-link-response';
import { formatDate } from '../../core/format/date-time';
import { toProblem } from '../../core/http/problem';
import { RELATIONSHIP_FOR_YOU, describeAccess } from '../patients/link-labels';
import { formatPatientName } from '../patients/patient-labels';

/** Patients linked to this account, and requests waiting for the hospital. */
@Component({
  selector: 'app-portal-patients-page',
  imports: [
    RouterLink,
    NgIcon,
    EmptyState,
    StatusPill,
    HlmAlertImports,
    HlmButtonImports,
    HlmSkeletonImports,
  ],
  providers: [
    provideIcons({ lucideCircleAlert, lucideClipboardList, lucideUserPlus, lucideUsersRound }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-6 flex items-start justify-between gap-4">
      <div>
        <h1 class="mb-1 text-2xl font-semibold tracking-tight">Patients</h1>
        <p class="text-muted-foreground">You, or the family members you help.</p>
      </div>
    </div>

    @if (loadError(); as message) {
      <div hlmAlert variant="destructive">
        <ng-icon name="lucideCircleAlert" aria-hidden="true" />
        <h2 hlmAlertTitle>Couldn't load your patients</h2>
        <p hlmAlertDescription>{{ message }}</p>
        <div hlmAlertAction>
          <button hlmBtn variant="outline" type="button" (click)="links.reload()">Try again</button>
        </div>
      </div>
    } @else if (!links.hasValue()) {
      <div class="flex flex-col gap-3" aria-busy="true">
        <div hlmSkeleton class="h-24 w-full rounded-lg"></div>
        <div hlmSkeleton class="h-24 w-full rounded-lg"></div>
      </div>
    } @else if (links.value().length === 0) {
      <div class="rounded-lg border bg-card p-6">
        <ui-empty-state
          icon="lucideUsersRound"
          title="No linked patients yet"
          description="Ask the hospital to link a patient to your account. For their privacy, the front desk checks your ID first."
        >
          <a hlmBtn routerLink="/portal/patients/link">
            <ng-icon name="lucideUserPlus" aria-hidden="true" data-icon="inline-start" />Link a
            patient
          </a>
        </ui-empty-state>
      </div>
    } @else {
      @if (active().length > 0) {
        <ul class="grid gap-3 md:grid-cols-2" aria-label="Linked patients">
          @for (link of active(); track link.id) {
            <li class="rounded-lg border bg-card p-4">
              <p class="text-lg font-medium">{{ patientName(link) }}</p>
              <p class="text-sm text-muted-foreground">
                <span data-numeric>{{ link.patient?.mrn }}</span> · {{ relationship(link) }}
              </p>
              <p class="mt-2 text-sm">{{ access(link) }}</p>
              @if (link.canViewRecords && link.patient) {
                <a
                  hlmBtn
                  variant="outline"
                  class="mt-3 w-full"
                  [routerLink]="['/portal/patients', link.patient.id, 'records']"
                >
                  <ng-icon
                    name="lucideClipboardList"
                    aria-hidden="true"
                    data-icon="inline-start"
                  />View records
                </a>
              }
            </li>
          }
        </ul>
      }

      @if (others().length > 0) {
        <h2 class="mt-8 mb-3 text-lg font-medium">Requests</h2>
        <ul class="grid gap-3 md:grid-cols-2" aria-label="Link requests">
          @for (link of others(); track link.id) {
            <li class="rounded-lg border bg-card p-4">
              <div class="flex flex-wrap items-center justify-between gap-2">
                <p class="font-medium">{{ link.requestedPatientName }}</p>
                <ui-status-pill kind="patientLink" [status]="link.status" />
              </div>
              <p class="text-sm text-muted-foreground">
                Born <span data-numeric>{{ formatDate(link.requestedBirthDate) }}</span> ·
                {{ relationship(link) }}
              </p>
              <p class="mt-2 text-sm">{{ statusText(link) }}</p>
            </li>
          }
        </ul>
      }

      <a hlmBtn variant="outline" class="mt-6 w-full md:w-auto" routerLink="/portal/patients/link">
        <ng-icon name="lucideUserPlus" aria-hidden="true" data-icon="inline-start" />Link another
        patient
      </a>
    }
  `,
})
export class PortalPatientsPage {
  private readonly api = inject(Api);

  protected readonly links = resource({ loader: () => this.api.invoke(listMyLinks) });

  protected readonly loadError = computed(() =>
    this.links.error()
      ? (toProblem(this.links.error()).detail ?? 'Please try again in a moment.')
      : null,
  );

  protected readonly active = computed(() =>
    (this.links.hasValue() ? this.links.value() : []).filter((l) => l.status === 'active'),
  );
  protected readonly others = computed(() =>
    (this.links.hasValue() ? this.links.value() : []).filter((l) => l.status !== 'active'),
  );

  protected readonly formatDate = formatDate;
  protected readonly access = describeAccess;

  protected patientName(link: MyLinkResponse): string {
    return link.patient ? formatPatientName(link.patient) : link.requestedPatientName;
  }

  protected relationship(link: MyLinkResponse): string {
    return RELATIONSHIP_FOR_YOU[link.relationship];
  }

  protected statusText(link: MyLinkResponse): string {
    switch (link.status) {
      case 'pending':
        return 'Waiting for the hospital. Bring a valid ID to the front desk so they can confirm who you are.';
      case 'rejected':
        return link.reviewNote
          ? `The hospital couldn't link this patient: ${link.reviewNote}`
          : "The hospital couldn't link this patient.";
      case 'revoked':
        return 'The hospital ended this link. Contact the front desk if you think this is a mistake.';
      default:
        return '';
    }
  }
}
