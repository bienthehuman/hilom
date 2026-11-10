import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  resource,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HlmAlertDialogImports } from '@hilom/ui/alert-dialog';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { StatusPill } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideMailPlus } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../../api/api';
import { listInvites } from '../../../api/fn/workforce/list-invites';
import { revokeInvite } from '../../../api/fn/workforce/revoke-invite';
import { sendInvite } from '../../../api/fn/workforce/send-invite';
import { listRoles } from '../../../api/fn/access/list-roles';
import { type EmployeeResponse } from '../../../api/models/employee-response';
import { type InviteResponse } from '../../../api/models/invite-response';
import { formatDateTime } from '../../../core/format/date-time';
import { type Problem, toProblem } from '../../../core/http/problem';
import { RolePicker } from '../../admin/users/role-picker';

/**
 * Whether the employee can sign in, the invites sent to them, and sending or revoking one.
 * Shown to people who can send invites (`workforce.invites.send`).
 */
@Component({
  selector: 'app-employee-access-card',
  imports: [
    ReactiveFormsModule,
    NgIcon,
    FormField,
    RolePicker,
    StatusPill,
    HlmAlertDialogImports,
    HlmButtonImports,
    HlmCardImports,
    HlmDialogImports,
    HlmInputImports,
    HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideMailPlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section hlmCard aria-labelledby="access-heading">
      <div hlmCardHeader class="flex flex-wrap items-start justify-between gap-3">
        <div class="flex flex-col gap-1">
          <h2 id="access-heading" hlmCardTitle>Sign-in access</h2>
          <p hlmCardDescription>
            @if (employee().hasAccount) {
              Has an account. Manage their roles under Administration → Users.
            } @else if (pending()) {
              Invite sent. Waiting for them to set a password.
            } @else {
              No account yet. Send an invite so they can set a password.
            }
          </p>
        </div>
        @if (canInvite()) {
          <button hlmBtn variant="outline" type="button" (click)="openSend()">
            <ng-icon name="lucideMailPlus" aria-hidden="true" data-icon="inline-start" />
            {{
              invites.hasValue() && invites.value().length > 0 ? 'Send new invite' : 'Send invite'
            }}
          </button>
        }
      </div>
      <div hlmCardContent>
        @if (invites.error()) {
          <p class="text-sm text-destructive" role="alert">
            Couldn't load invites.
            <button hlmBtn variant="link" size="sm" type="button" (click)="invites.reload()">
              Try again
            </button>
          </p>
        } @else if (invites.isLoading() && !invites.hasValue()) {
          <div hlmSkeleton class="h-12 w-full rounded-md" aria-busy="true"></div>
        } @else if (inviteList().length === 0) {
          <p class="text-sm text-muted-foreground">No invites sent yet.</p>
        } @else {
          <ul class="flex flex-col divide-y" aria-label="Invites">
            @for (invite of inviteList(); track invite.id) {
              <li
                class="flex flex-wrap items-center justify-between gap-3 py-3 first:pt-0 last:pb-0"
              >
                <div class="flex min-w-0 flex-col gap-0.5">
                  <div class="flex flex-wrap items-center gap-2">
                    <span class="font-medium break-all">{{ invite.email }}</span>
                    <ui-status-pill kind="invite" [status]="invite.state" />
                  </div>
                  <span class="text-xs text-muted-foreground" data-numeric>
                    {{ describe(invite) }}
                  </span>
                  @if (invite.roles.length > 0) {
                    <span class="text-xs text-muted-foreground">
                      Roles: {{ roleNames(invite) }}
                    </span>
                  }
                </div>
                @if (invite.state === 'pending') {
                  <button
                    hlmBtn
                    variant="ghost"
                    size="sm"
                    type="button"
                    [attr.aria-label]="'Revoke invite to ' + invite.email"
                    (click)="confirmRevoke.set(invite)"
                  >
                    Revoke
                  </button>
                }
              </li>
            }
          </ul>
        }
      </div>
    </section>

    <!-- Send invite -->
    <hlm-dialog [state]="sendOpen() ? 'open' : 'closed'" (closed)="sendOpen.set(false)">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="sm:max-w-lg">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>Invite {{ employee().firstName }} to sign in</h2>
          <p hlmDialogDescription>
            They get an email with a link to set a password. The link works once, and the email says
            when it expires.
          </p>
        </hlm-dialog-header>
        <form
          id="send-invite-form"
          class="flex flex-col gap-4"
          [formGroup]="sendForm"
          (ngSubmit)="send()"
          novalidate
        >
          <ui-form-field
            label="Email"
            for="invite-email"
            hint="Becomes their sign-in email"
            required
          >
            <input
              hlmInput
              id="invite-email"
              type="email"
              formControlName="email"
              autocomplete="off"
              spellcheck="false"
            />
          </ui-form-field>
          <div class="max-h-[40vh] overflow-y-auto">
            <app-role-picker [roles]="roleOptions()" [(selected)]="roleIds" />
          </div>
          @if (sendProblem(); as problem) {
            <p class="text-sm text-destructive" role="alert">{{ problem.detail }}</p>
          }
        </form>
        <hlm-dialog-footer>
          <button hlmBtn variant="outline" type="button" (click)="sendOpen.set(false)">
            Cancel
          </button>
          <button hlmBtn type="submit" form="send-invite-form" [disabled]="sending()">
            {{ sending() ? 'Sending…' : 'Send invite' }}
          </button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>

    <!-- Confirm revoke -->
    <hlm-alert-dialog
      [state]="confirmRevoke() ? 'open' : 'closed'"
      (closed)="confirmRevoke.set(null)"
    >
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <hlm-alert-dialog-header>
          <h2 hlmAlertDialogTitle>Revoke the invite to {{ confirmRevoke()?.email }}?</h2>
          <p hlmAlertDialogDescription>
            The link stops working right away. You can send a new invite afterwards.
          </p>
        </hlm-alert-dialog-header>
        <hlm-alert-dialog-footer>
          <button hlmAlertDialogCancel type="button">Keep it</button>
          <button hlmAlertDialogAction variant="destructive" type="button" (click)="revoke()">
            Revoke invite
          </button>
        </hlm-alert-dialog-footer>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class EmployeeAccessCard {
  private readonly api = inject(Api);

  readonly employee = input.required<EmployeeResponse>();

  protected readonly invites = resource({
    params: () => this.employee().id,
    loader: ({ params }) => this.api.invoke(listInvites, { employeeId: params }),
  });
  protected readonly inviteList = computed(() =>
    this.invites.hasValue() ? this.invites.value() : [],
  );
  protected readonly pending = computed(() =>
    this.inviteList().some((invite) => invite.state === 'pending'),
  );
  protected readonly canInvite = computed(
    () => !this.employee().hasAccount && this.employee().status !== 'separated' && !this.pending(),
  );

  protected describe(invite: InviteResponse): string {
    const sent = `Sent ${formatDateTime(invite.sentAt)}${invite.sentBy ? ` by ${invite.sentBy}` : ''}`;
    switch (invite.state) {
      case 'accepted':
        return `${sent} · accepted ${formatDateTime(invite.acceptedAt)}`;
      case 'revoked':
        return `${sent} · revoked ${formatDateTime(invite.revokedAt)}`;
      case 'expired':
        return `${sent} · expired ${formatDateTime(invite.expiresAt)}`;
      default:
        return `${sent} · expires ${formatDateTime(invite.expiresAt)}`;
    }
  }

  protected roleNames(invite: InviteResponse): string {
    return invite.roles.map((role) => role.name).join(', ');
  }

  // ---- Send ----
  private readonly roles = resource({
    params: () => (this.sendOpen() ? true : undefined),
    loader: () => this.api.invoke(listRoles),
  });
  protected readonly roleOptions = computed(() =>
    this.roles.hasValue() ? this.roles.value() : [],
  );

  protected readonly sendOpen = signal(false);
  protected readonly sending = signal(false);
  protected readonly sendProblem = signal<Problem | null>(null);
  protected readonly roleIds = signal<string[]>([]);
  protected readonly sendForm = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(320)]],
  });

  protected openSend(): void {
    this.sendForm.reset({ email: this.employee().email ?? '' });
    this.roleIds.set([]);
    this.sendProblem.set(null);
    this.sendOpen.set(true);
  }

  protected async send(): Promise<void> {
    this.sendForm.markAllAsTouched();
    if (this.sendForm.invalid || this.sending()) {
      return;
    }

    const email = this.sendForm.getRawValue().email.trim();
    this.sending.set(true);
    this.sendProblem.set(null);
    try {
      await this.api.invoke(sendInvite, {
        employeeId: this.employee().id,
        body: { email, roleIds: this.roleIds() },
      });
      this.sendOpen.set(false);
      toast.success('Invite sent', { description: `A link to set a password went to ${email}.` });
      this.invites.reload();
    } catch (error) {
      const problem = toProblem(error);
      const fieldMessage = problem.errors?.['email']?.[0];
      if (fieldMessage || problem.code === 'email_taken') {
        this.sendForm.controls.email.setErrors({ server: fieldMessage ?? problem.detail });
      } else {
        this.sendProblem.set(problem);
      }
    } finally {
      this.sending.set(false);
    }
  }

  // ---- Revoke ----
  protected readonly confirmRevoke = signal<InviteResponse | null>(null);

  protected async revoke(): Promise<void> {
    const invite = this.confirmRevoke();
    if (!invite) {
      return;
    }
    this.confirmRevoke.set(null);
    try {
      await this.api.invoke(revokeInvite, { id: invite.id });
      toast.success('Invite revoked', {
        description: `The link sent to ${invite.email} no longer works.`,
      });
    } catch (error) {
      toast.error("Couldn't revoke the invite", {
        description: toProblem(error).detail ?? 'Please try again.',
      });
    } finally {
      this.invites.reload();
    }
  }
}
