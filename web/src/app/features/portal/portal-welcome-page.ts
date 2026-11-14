import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import {
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { Router } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { ThemeToggle } from '@hilom/ui/theme-toggle';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideHeartPulse, lucideMailCheck } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { AuthService } from '../../core/auth/auth.service';
import { type Problem, toProblem } from '../../core/http/problem';
import { type LinkFor } from './portal-link-request-page';

interface Choice {
  value: LinkFor;
  label: string;
  description: string;
}

/**
 * Where a new portal account starts: confirm the email with the code we sent, then say who the account is
 * for, which opens the right link request (yourself first, then someone you care for).
 */
@Component({
  selector: 'app-portal-welcome-page',
  imports: [
    ReactiveFormsModule,
    NgIcon,
    FormField,
    ThemeToggle,
    HlmAlertImports,
    HlmButtonImports,
    HlmCardImports,
    HlmInputImports,
  ],
  providers: [provideIcons({ lucideCircleAlert, lucideHeartPulse, lucideMailCheck })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'portal flex min-h-dvh flex-col bg-background' },
  template: `
    <div class="flex justify-end p-4">
      <ui-theme-toggle />
    </div>

    <main class="flex flex-1 items-start justify-center px-4 pt-[4vh] pb-16">
      <div class="flex w-full max-w-md flex-col gap-6">
        <div class="flex flex-col items-center gap-3 text-center">
          <span
            class="flex size-11 items-center justify-center rounded-lg bg-primary text-primary-foreground"
          >
            <ng-icon
              [name]="step() === 'verify' ? 'lucideMailCheck' : 'lucideHeartPulse'"
              size="24"
              aria-hidden="true"
            />
          </span>
          <p class="text-sm text-muted-foreground" data-numeric>Step {{ stepNumber() }} of 2</p>
          @if (step() === 'verify') {
            <div>
              <h1 class="text-2xl font-semibold tracking-tight">Confirm your email</h1>
              <p class="mt-1 text-muted-foreground">
                We sent a 6-digit code to <span class="font-medium break-all">{{ email() }}</span
                >. Enter it below.
              </p>
            </div>
          } @else {
            <div>
              <h1 class="text-2xl font-semibold tracking-tight">Who will you use Hilom for?</h1>
              <p class="mt-1 text-muted-foreground">
                We'll ask the hospital to link the right records to your account.
              </p>
            </div>
          }
        </div>

        @if (step() === 'verify') {
          @if (problem(); as p) {
            <div hlmAlert variant="destructive" role="alert">
              <ng-icon name="lucideCircleAlert" aria-hidden="true" />
              <h2 hlmAlertTitle>Couldn't confirm your email</h2>
              <p hlmAlertDescription>{{ p.detail ?? 'Please try again.' }}</p>
            </div>
          }

          <form hlmCard [formGroup]="codeForm" (ngSubmit)="verify()" novalidate>
            <div hlmCardContent class="flex flex-col gap-5">
              <ui-form-field
                label="Code"
                for="email-code"
                hint="It works for 15 minutes. Check your spam folder if you can't find it."
                required
              >
                <input
                  hlmInput
                  id="email-code"
                  class="w-full text-center text-xl tracking-[0.4em]"
                  formControlName="code"
                  inputmode="numeric"
                  autocomplete="one-time-code"
                  maxlength="6"
                  data-numeric
                />
              </ui-form-field>
              <button hlmBtn type="submit" class="w-full" [disabled]="verifying()">
                {{ verifying() ? 'Checking…' : 'Confirm email' }}
              </button>
            </div>
          </form>

          <div class="flex flex-col items-center gap-2 text-center">
            <button
              hlmBtn
              variant="outline"
              type="button"
              class="w-full"
              [disabled]="sending() || waitSeconds() > 0"
              (click)="resend()"
            >
              @if (waitSeconds() > 0) {
                <span data-numeric>Send a new code in {{ waitSeconds() }}s</span>
              } @else {
                {{ sending() ? 'Sending…' : 'Send a new code' }}
              }
            </button>
            <button
              hlmBtn
              variant="ghost"
              type="button"
              [disabled]="signingOut()"
              (click)="signOut()"
            >
              Wrong email? Sign out
            </button>
          </div>
        } @else {
          <form
            class="flex flex-col gap-5"
            [formGroup]="whoForm"
            (ngSubmit)="continue()"
            novalidate
          >
            <fieldset class="flex flex-col gap-3">
              <legend class="sr-only">Who will you use Hilom for?</legend>
              @for (choice of choices; track choice.value) {
                <label
                  class="flex cursor-pointer items-start gap-3 rounded-lg border bg-card p-4 has-checked:border-primary has-checked:ring-1 has-checked:ring-primary has-focus-visible:outline-2 has-focus-visible:outline-ring"
                >
                  <input
                    type="radio"
                    name="who"
                    class="mt-1 size-4 accent-primary focus-visible:outline-none"
                    [value]="choice.value"
                    formControlName="who"
                  />
                  <span class="flex flex-col gap-0.5">
                    <span class="font-medium">{{ choice.label }}</span>
                    <span class="text-sm text-muted-foreground">{{ choice.description }}</span>
                  </span>
                </label>
              }
              @if (who.touched && who.invalid) {
                <p class="text-sm text-destructive" role="alert">Choose one to continue.</p>
              }
            </fieldset>

            <button hlmBtn type="submit" class="w-full">Continue</button>
            <button hlmBtn variant="ghost" type="button" (click)="skip()">Skip for now</button>
          </form>
        }
      </div>
    </main>
  `,
})
export class PortalWelcomePage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly email = computed(() => this.auth.user()?.email ?? '');

  /** Confirmed accounts that come back here go straight to the second step. */
  protected readonly step = signal<'verify' | 'who'>(
    this.auth.mustVerifyEmail() ? 'verify' : 'who',
  );
  protected readonly stepNumber = computed(() => (this.step() === 'verify' ? 1 : 2));

  // ---- Step 1: the code ----
  protected readonly codeForm = inject(FormBuilder).nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(/^[0-9]{6}$/)]],
  });
  protected readonly verifying = signal(false);
  protected readonly sending = signal(false);
  protected readonly signingOut = signal(false);
  protected readonly problem = signal<Problem | null>(null);
  protected readonly waitSeconds = signal(0);

  // ---- Step 2: who for ----
  protected readonly choices: readonly Choice[] = [
    { value: 'self', label: 'Myself', description: 'Book visits and follow your own care.' },
    {
      value: 'other',
      label: 'Someone I care for',
      description: 'A parent, child, spouse, or someone you help.',
    },
    { value: 'both', label: 'Both', description: "We'll start with your own record." },
  ];
  protected readonly whoForm = new FormGroup({
    who: new FormControl<LinkFor | null>(null, Validators.required),
  });
  protected readonly who = this.whoForm.controls.who;

  private timer: ReturnType<typeof setInterval> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearInterval(this.timer));
  }

  protected async verify(): Promise<void> {
    const code = this.codeForm.controls.code;
    code.setValue(code.value.replace(/\s/g, ''));
    this.codeForm.markAllAsTouched();
    if (this.codeForm.invalid || this.verifying()) {
      return;
    }

    this.verifying.set(true);
    this.problem.set(null);
    try {
      await this.auth.verifyEmail(code.value);
      toast.success('Email confirmed');
      this.step.set('who');
    } catch (error) {
      const problem = toProblem(error);
      const message = problem.errors?.['code']?.[0];
      if (problem.code === 'email_code_wrong' && message) {
        code.setErrors({ server: message });
      } else {
        this.problem.set(problem);
      }
    } finally {
      this.verifying.set(false);
    }
  }

  protected async resend(): Promise<void> {
    this.sending.set(true);
    this.problem.set(null);
    try {
      await this.auth.sendEmailCode();
      this.codeForm.reset();
      toast.success('New code sent', { description: `Check ${this.email()}.` });
      this.startWait(60);
    } catch (error) {
      const problem = toProblem(error);
      if (
        problem.code === 'email_code_cooldown' &&
        typeof problem['retryAfterSeconds'] === 'number'
      ) {
        this.startWait(problem['retryAfterSeconds']);
      } else if (problem.code === 'email_already_verified') {
        await this.auth.refresh();
        this.step.set('who');
      } else {
        this.problem.set(problem);
      }
    } finally {
      this.sending.set(false);
    }
  }

  protected async continue(): Promise<void> {
    this.who.markAsTouched();
    if (this.who.invalid || !this.who.value) {
      return;
    }
    await this.router.navigate(['/portal/patients/link'], { queryParams: { for: this.who.value } });
  }

  protected async skip(): Promise<void> {
    await this.router.navigateByUrl('/portal/appointments');
  }

  protected async signOut(): Promise<void> {
    this.signingOut.set(true);
    try {
      await this.auth.logout();
    } catch {
      // Signed out on this device either way.
    } finally {
      this.signingOut.set(false);
      await this.router.navigateByUrl('/portal/register');
    }
  }

  private startWait(seconds: number): void {
    clearInterval(this.timer);
    this.waitSeconds.set(Math.max(0, Math.ceil(seconds)));
    this.timer = setInterval(() => {
      this.waitSeconds.update((left) => Math.max(0, left - 1));
      if (this.waitSeconds() === 0) {
        clearInterval(this.timer);
      }
    }, 1000);
  }
}
