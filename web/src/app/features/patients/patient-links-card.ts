import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  resource,
  signal,
} from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { HlmDropdownMenuImports } from '@hilom/ui/dropdown-menu';
import { FormField } from '@hilom/ui/form-field';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { StatusPill } from '@hilom/ui/status';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCircleAlert,
  lucideEllipsis,
  lucideKeyRound,
  lucideLink2Off,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { listPatientLinks } from '../../api/fn/patients/list-patient-links';
import { revokeLink } from '../../api/fn/patients/revoke-link';
import { setLinkPermissions } from '../../api/fn/patients/set-link-permissions';
import { type LinkResponse } from '../../api/models/link-response';
import { labelOf } from '../../core/format/options';
import { type Problem, toProblem } from '../../core/http/problem';
import { LinkAccessFields } from './link-access-fields';
import { RELATIONSHIPS, describeAccess } from './link-labels';

/** Portal accounts linked to this patient: what each may do, and ending a link. Staff with links.manage. */
@Component({
  selector: 'app-patient-links-card',
  imports: [
    ReactiveFormsModule,
    NgIcon,
    FormField,
    StatusPill,
    LinkAccessFields,
    HlmAlertImports,
    HlmButtonImports,
    HlmCardImports,
    HlmDialogImports,
    HlmDropdownMenuImports,
    HlmSkeletonImports,
    HlmTextareaImports,
  ],
  providers: [provideIcons({ lucideCircleAlert, lucideEllipsis, lucideKeyRound, lucideLink2Off })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section hlmCard aria-labelledby="portal-access-heading">
      <div hlmCardHeader>
        <h2 id="portal-access-heading" hlmCardTitle>Portal access</h2>
        <p hlmCardDescription>Patient portal accounts linked to this patient.</p>
      </div>
      <div hlmCardContent>
        @if (loadError(); as message) {
          <div hlmAlert variant="destructive">
            <ng-icon name="lucideCircleAlert" aria-hidden="true" />
            <h3 hlmAlertTitle>Couldn't load portal access</h3>
            <p hlmAlertDescription>{{ message }}</p>
            <div hlmAlertAction>
              <button hlmBtn variant="outline" size="sm" type="button" (click)="links.reload()">
                Try again
              </button>
            </div>
          </div>
        } @else if (!links.hasValue()) {
          <div hlmSkeleton class="h-12 w-full" aria-busy="true"></div>
        } @else if (active().length === 0) {
          <p class="text-muted-foreground">
            No portal accounts are linked. Requests appear under Portal link requests.
          </p>
        } @else {
          <ul class="flex flex-col divide-y" aria-label="Linked portal accounts">
            @for (link of active(); track link.id) {
              <li class="flex items-start justify-between gap-4 py-3 first:pt-0 last:pb-0">
                <div class="min-w-0">
                  <p class="font-medium">
                    {{ link.requester.displayName }}
                    <span class="text-sm font-normal text-muted-foreground"
                      >· {{ relationshipLabel(link.relationship) }}</span
                    >
                  </p>
                  <p class="text-sm break-all text-muted-foreground">{{ link.requester.email }}</p>
                  <p class="mt-0.5 text-sm">{{ access(link) }}</p>
                </div>
                <button
                  hlmBtn
                  variant="ghost"
                  size="icon-sm"
                  type="button"
                  [attr.aria-label]="'Actions for ' + link.requester.displayName"
                  [hlmDropdownMenuTrigger]="menu"
                  align="end"
                >
                  <ng-icon name="lucideEllipsis" aria-hidden="true" />
                </button>
                <ng-template #menu>
                  <hlm-dropdown-menu class="w-52">
                    <button hlmDropdownMenuItem type="button" (triggered)="openAccess(link)">
                      <ng-icon name="lucideKeyRound" aria-hidden="true" />Change access
                    </button>
                    <button hlmDropdownMenuItem type="button" (triggered)="openRevoke(link)">
                      <ng-icon name="lucideLink2Off" aria-hidden="true" />Revoke link
                    </button>
                  </hlm-dropdown-menu>
                </ng-template>
              </li>
            }
          </ul>
        }

        @if (ended().length > 0) {
          <h3 class="mt-6 mb-2 text-sm font-medium text-muted-foreground">Ended</h3>
          <ul class="flex flex-col gap-2" aria-label="Ended links">
            @for (link of ended(); track link.id) {
              <li class="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
                {{ link.requester.displayName }} · {{ relationshipLabel(link.relationship) }}
                <ui-status-pill kind="patientLink" [status]="link.status" />
                @if (link.revokeReason) {
                  <span class="w-full text-xs">"{{ link.revokeReason }}"</span>
                }
              </li>
            }
          </ul>
        }
      </div>
    </section>

    <!-- Change access -->
    <hlm-dialog [state]="accessFor() ? 'open' : 'closed'" (closed)="accessFor.set(null)">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="sm:max-w-md">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>Change access</h2>
          <p hlmDialogDescription>
            For {{ accessFor()?.requester?.displayName }}. Takes effect on their next request.
          </p>
        </hlm-dialog-header>
        <app-link-access-fields [(canBook)]="canBook" [(canViewRecords)]="canViewRecords" />
        @if (problem(); as p) {
          <p class="text-sm text-destructive" role="alert">{{ p.detail }}</p>
        }
        <hlm-dialog-footer>
          <button hlmBtn variant="outline" type="button" (click)="accessFor.set(null)">
            Cancel
          </button>
          <button hlmBtn type="button" [disabled]="saving()" (click)="saveAccess()">
            {{ saving() ? 'Saving…' : 'Save access' }}
          </button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>

    <!-- Revoke -->
    <hlm-dialog [state]="revokeFor() ? 'open' : 'closed'" (closed)="revokeFor.set(null)">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="sm:max-w-md">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>Revoke link</h2>
          <p hlmDialogDescription>
            {{ revokeFor()?.requester?.displayName }} loses access to this patient right away. The
            reason is kept in the record, not shown to them.
          </p>
        </hlm-dialog-header>
        <ui-form-field label="Reason" for="revoke-reason" required>
          <textarea hlmTextarea id="revoke-reason" rows="3" [formControl]="revokeReason"></textarea>
        </ui-form-field>
        @if (problem(); as p) {
          <p class="text-sm text-destructive" role="alert">{{ p.detail }}</p>
        }
        <hlm-dialog-footer>
          <button hlmBtn variant="outline" type="button" (click)="revokeFor.set(null)">
            Cancel
          </button>
          <button
            hlmBtn
            variant="destructive"
            type="button"
            [disabled]="saving()"
            (click)="revoke()"
          >
            {{ saving() ? 'Revoking…' : 'Revoke link' }}
          </button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class PatientLinksCard {
  private readonly api = inject(Api);

  readonly patientId = input.required<string>();

  protected readonly links = resource({
    params: () => this.patientId(),
    loader: ({ params }) => this.api.invoke(listPatientLinks, { patientId: params }),
  });
  protected readonly loadError = computed(() =>
    this.links.error() ? (toProblem(this.links.error()).detail ?? 'Please try again.') : null,
  );
  protected readonly active = computed(() =>
    (this.links.hasValue() ? this.links.value() : []).filter((l) => l.status === 'active'),
  );
  protected readonly ended = computed(() =>
    (this.links.hasValue() ? this.links.value() : []).filter((l) => l.status === 'revoked'),
  );

  protected readonly relationshipLabel = (value: string) => labelOf(RELATIONSHIPS, value);
  protected readonly access = describeAccess;

  protected readonly saving = signal(false);
  protected readonly problem = signal<Problem | null>(null);

  // ---- Change access ----
  protected readonly accessFor = signal<LinkResponse | null>(null);
  protected readonly canBook = signal(true);
  protected readonly canViewRecords = signal(false);

  protected openAccess(link: LinkResponse): void {
    this.canBook.set(link.canBook);
    this.canViewRecords.set(link.canViewRecords);
    this.problem.set(null);
    this.accessFor.set(link);
  }

  protected async saveAccess(): Promise<void> {
    const link = this.accessFor();
    if (!link || this.saving()) {
      return;
    }
    await this.act(async () => {
      await this.api.invoke(setLinkPermissions, {
        id: link.id,
        body: {
          canBook: this.canBook(),
          canViewRecords: this.canViewRecords(),
          version: link.version,
        },
      });
      this.accessFor.set(null);
      toast.success('Access updated', { description: link.requester.displayName });
    });
  }

  // ---- Revoke ----
  protected readonly revokeFor = signal<LinkResponse | null>(null);
  protected readonly revokeReason = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(500)],
  });

  protected openRevoke(link: LinkResponse): void {
    this.revokeReason.reset('');
    this.problem.set(null);
    this.revokeFor.set(link);
  }

  protected async revoke(): Promise<void> {
    const link = this.revokeFor();
    if (!link || this.saving()) {
      return;
    }
    this.revokeReason.markAsTouched();
    const reason = this.revokeReason.value.trim();
    if (this.revokeReason.invalid || !reason) {
      this.revokeReason.setErrors({ server: 'Say why the link is ending.' });
      return;
    }
    await this.act(async () => {
      await this.api.invoke(revokeLink, { id: link.id, body: { reason, version: link.version } });
      this.revokeFor.set(null);
      toast.success('Link revoked', { description: link.requester.displayName });
    });
  }

  private async act(work: () => Promise<void>): Promise<void> {
    this.saving.set(true);
    this.problem.set(null);
    try {
      await work();
      this.links.reload();
    } catch (error) {
      const problem = toProblem(error);
      if (problem.code === 'concurrency_conflict' || problem.code === 'link_not_active') {
        this.links.reload();
      }
      this.problem.set(problem);
    } finally {
      this.saving.set(false);
    }
  }
}
