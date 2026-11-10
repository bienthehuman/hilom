import { Location } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  resource,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  type AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  type ValidationErrors,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { ThemeToggle } from '@hilom/ui/theme-toggle';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideEye, lucideEyeOff, lucideHeartPulse } from '@ng-icons/lucide';

import { Api } from '../../api/api';
import { acceptInvite } from '../../api/fn/workforce/accept-invite';
import { lookupInvite } from '../../api/fn/workforce/lookup-invite';
import { formatDateTime } from '../../core/format/date-time';
import { type Problem, toProblem } from '../../core/http/problem';

const MIN_PASSWORD = 12;

function matchesPassword(control: AbstractControl): ValidationErrors | null {
  const password = control.parent?.get('password')?.value as string | undefined;
  return control.value && control.value !== password
    ? { mismatch: { message: "The passwords don't match." } }
    : null;
}

/**
 * Where an invite link lands (`/invite#<token>`). The token is in the fragment so it never reaches a
 * server log, and it's cleared from the address bar once read.
 */
@Component({
  selector: 'app-accept-invite-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    NgIcon,
    FormField,
    ThemeToggle,
    HlmAlertImports,
    HlmButtonImports,
    HlmCardImports,
    HlmInputImports,
    HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideHeartPulse, lucideCircleAlert, lucideEye, lucideEyeOff })],
  templateUrl: './accept-invite-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AcceptInvitePage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  private readonly token = inject(ActivatedRoute).snapshot.fragment ?? '';

  protected readonly invite = resource({
    params: () => this.token || undefined,
    loader: ({ params }) => this.api.invoke(lookupInvite, { body: { token: params } }),
  });

  /** Why the link doesn't work, in words for the person holding it. */
  protected readonly linkProblem = (): { title: string; detail: string } | null => {
    if (!this.token) {
      return {
        title: 'This link is incomplete',
        detail: 'Open the link from your invite email again, or copy all of it.',
      };
    }
    if (!this.invite.error()) {
      return null;
    }
    const problem = toProblem(this.invite.error());
    const titles: Record<string, string> = {
      invite_used: 'This invite was already used',
      invite_expired: 'This invite has expired',
      invite_revoked: 'This invite was cancelled',
      invite_not_found: "This link doesn't work",
    };
    return {
      title: titles[problem.code ?? ''] ?? "Couldn't open this invite",
      detail: problem.detail ?? 'Please try again.',
    };
  };

  protected readonly expires = (iso: string) => formatDateTime(iso);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    password: ['', [Validators.required, Validators.minLength(MIN_PASSWORD)]],
    confirm: ['', [Validators.required, matchesPassword]],
  });

  protected readonly showPassword = signal(false);
  protected readonly submitting = signal(false);
  protected readonly problem = signal<Problem | null>(null);

  constructor() {
    // Keep the token out of the address bar, history and screenshots once it has been read.
    if (this.token) {
      inject(Location).replaceState('/invite');
    }

    this.form.controls.password.valueChanges
      .pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe(() => this.form.controls.confirm.updateValueAndValidity({ emitEvent: false }));
  }

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.problem.set(null);
    try {
      const { email } = await this.api.invoke(acceptInvite, {
        body: { token: this.token, password: this.form.getRawValue().password },
      });
      await this.router.navigate(['/login'], { queryParams: { email, reason: 'account_ready' } });
    } catch (error) {
      const problem = toProblem(error);
      const message = problem.errors?.['password']?.[0];
      if (message) {
        this.form.controls.password.setErrors({ server: message });
      } else {
        this.problem.set(problem);
      }
    } finally {
      this.submitting.set(false);
    }
  }
}
