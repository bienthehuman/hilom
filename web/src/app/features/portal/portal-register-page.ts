import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { ThemeToggle } from '@hilom/ui/theme-toggle';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideEye, lucideEyeOff, lucideHeartPulse } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { AuthService } from '../../core/auth/auth.service';
import { type Problem, toProblem } from '../../core/http/problem';

/**
 * Patients and family members create their own portal account. It reaches no records until the hospital
 * verifies a link to a patient (docs/adr/0003-patient-account-links.md).
 */
@Component({
  selector: 'app-portal-register-page',
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
  ],
  providers: [provideIcons({ lucideCircleAlert, lucideEye, lucideEyeOff, lucideHeartPulse })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'portal flex min-h-dvh flex-col bg-background' },
  template: `
    <div class="flex justify-end p-4">
      <ui-theme-toggle />
    </div>

    <main class="flex flex-1 items-start justify-center px-4 pt-[4vh] pb-16">
      <div class="flex w-full max-w-sm flex-col gap-6">
        <div class="flex flex-col items-center gap-3 text-center">
          <span
            class="flex size-11 items-center justify-center rounded-lg bg-primary text-primary-foreground"
          >
            <ng-icon name="lucideHeartPulse" size="24" aria-hidden="true" />
          </span>
          <div>
            <h1 class="text-2xl font-semibold tracking-tight">Create your portal account</h1>
            <p class="mt-1 text-muted-foreground">
              For patients and the family members who help them.
            </p>
          </div>
        </div>

        @if (problem(); as p) {
          <div hlmAlert variant="destructive">
            <ng-icon name="lucideCircleAlert" aria-hidden="true" />
            <h2 hlmAlertTitle>Couldn't create your account</h2>
            <p hlmAlertDescription>{{ p.detail ?? 'Please try again.' }}</p>
          </div>
        }

        <form hlmCard [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <div hlmCardContent class="flex flex-col gap-5">
            <ui-form-field label="Your full name" for="register-name">
              <input
                hlmInput
                id="register-name"
                formControlName="displayName"
                autocomplete="name"
              />
            </ui-form-field>

            <ui-form-field label="Email" for="register-email">
              <input
                hlmInput
                id="register-email"
                type="email"
                formControlName="email"
                autocomplete="email"
                autocapitalize="none"
                spellcheck="false"
              />
            </ui-form-field>

            <ui-form-field label="Password" for="register-password" hint="At least 12 characters">
              <div class="relative">
                <input
                  hlmInput
                  id="register-password"
                  class="w-full pe-12"
                  [type]="showPassword() ? 'text' : 'password'"
                  formControlName="password"
                  autocomplete="new-password"
                />
                <button
                  hlmBtn
                  variant="ghost"
                  size="icon-sm"
                  type="button"
                  class="absolute end-0 top-1/2 -translate-y-1/2"
                  [attr.aria-label]="showPassword() ? 'Hide password' : 'Show password'"
                  [attr.aria-pressed]="showPassword()"
                  (click)="showPassword.set(!showPassword())"
                >
                  <ng-icon
                    [name]="showPassword() ? 'lucideEyeOff' : 'lucideEye'"
                    aria-hidden="true"
                  />
                </button>
              </div>
            </ui-form-field>

            <button hlmBtn type="submit" class="w-full" [disabled]="submitting()">
              {{ submitting() ? 'Creating your account…' : 'Create account' }}
            </button>
          </div>
        </form>

        <p class="text-center text-sm text-muted-foreground">
          Your account starts empty. The hospital checks who you are before linking a patient's
          records to it.
        </p>
        <p class="text-center text-muted-foreground">
          Already have an account?
          <a
            class="font-medium text-primary underline-offset-4 hover:underline"
            routerLink="/portal/login"
            >Sign in</a
          >
        </p>
      </div>
    </main>
  `,
})
export class PortalRegisterPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    displayName: ['', [Validators.required, Validators.maxLength(200)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(320)]],
    password: ['', [Validators.required, Validators.minLength(12), Validators.maxLength(128)]],
  });

  protected readonly submitting = signal(false);
  protected readonly showPassword = signal(false);
  protected readonly problem = signal<Problem | null>(null);

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.submitting()) {
      return;
    }

    const { displayName, email, password } = this.form.getRawValue();
    this.submitting.set(true);
    this.problem.set(null);
    try {
      await this.auth.register(displayName.trim(), email.trim(), password);
      toast.success('Welcome to Hilom', { description: 'Your account is ready.' });
      await this.router.navigateByUrl('/portal');
    } catch (error) {
      const problem = toProblem(error);
      let placed = false;
      for (const [field, messages] of Object.entries(problem.errors ?? {})) {
        const control = this.form.get(field);
        if (control && messages[0]) {
          control.setErrors({ server: messages[0] });
          placed = true;
        }
      }
      if (problem.code === 'email_taken') {
        this.form.controls.email.setErrors({ server: problem.detail });
      } else if (!placed) {
        this.problem.set(problem);
      }
    } finally {
      this.submitting.set(false);
    }
  }
}
