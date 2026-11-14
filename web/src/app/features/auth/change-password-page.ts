import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
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
import { Router } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { ThemeToggle } from '@hilom/ui/theme-toggle';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideEye, lucideEyeOff, lucideKeyRound } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { landingUrl } from '../../core/auth/auth.guards';
import { AuthService } from '../../core/auth/auth.service';
import { type Problem, toProblem } from '../../core/http/problem';

const MIN_PASSWORD = 12;

function matchesPassword(control: AbstractControl): ValidationErrors | null {
  const password = control.parent?.get('newPassword')?.value as string | undefined;
  return control.value && control.value !== password
    ? { mismatch: { message: "The passwords don't match." } }
    : null;
}

function differsFromCurrent(control: AbstractControl): ValidationErrors | null {
  const current = control.parent?.get('currentPassword')?.value as string | undefined;
  return control.value && control.value === current
    ? { unchanged: { message: 'Choose a password different from your current one.' } }
    : null;
}

/**
 * Changing your own password. Required right after signing in with a temporary password an administrator
 * set (the guards send the user here and nowhere else); otherwise reached from the account menu.
 */
@Component({
  selector: 'app-change-password-page',
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
  providers: [provideIcons({ lucideKeyRound, lucideCircleAlert, lucideEye, lucideEyeOff })],
  templateUrl: './change-password-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChangePasswordPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Where the user was headed; they continue there afterwards. */
  readonly returnUrl = input<string>();

  /** Decided once on arrival, so the wording doesn't flip after a successful change. */
  protected readonly required = this.auth.mustChangePassword();
  protected readonly email = computed(() => this.auth.user()?.email ?? '');

  protected readonly form = inject(FormBuilder).nonNullable.group({
    currentPassword: ['', Validators.required],
    newPassword: [
      '',
      [Validators.required, Validators.minLength(MIN_PASSWORD), differsFromCurrent],
    ],
    confirm: ['', [Validators.required, matchesPassword]],
  });

  protected readonly showPassword = signal(false);
  protected readonly submitting = signal(false);
  protected readonly signingOut = signal(false);
  protected readonly problem = signal<Problem | null>(null);

  constructor() {
    const destroyRef = inject(DestroyRef);
    const { currentPassword, newPassword, confirm } = this.form.controls;
    currentPassword.valueChanges
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe(() => newPassword.updateValueAndValidity({ emitEvent: false }));
    newPassword.valueChanges
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe(() => confirm.updateValueAndValidity({ emitEvent: false }));
  }

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.problem.set(null);
    try {
      const { currentPassword, newPassword } = this.form.getRawValue();
      await this.auth.changePassword(currentPassword, newPassword);
      toast.success('Password changed', {
        description: 'Use your new password next time you sign in.',
      });
      await this.router.navigateByUrl(this.destination());
    } catch (error) {
      const problem = toProblem(error);
      const current = problem.errors?.['currentPassword']?.[0];
      const next = problem.errors?.['newPassword']?.[0];
      if (current) {
        this.form.controls.currentPassword.setErrors({ server: current });
      } else if (next) {
        this.form.controls.newPassword.setErrors({ server: next });
      } else {
        this.problem.set(problem);
      }
    } finally {
      this.submitting.set(false);
    }
  }

  /** Not now: back where the user came from. Only offered when the change isn't required. */
  protected cancel(): void {
    void this.router.navigateByUrl(this.destination());
  }

  /** The way out for someone who doesn't know the temporary password (e.g. a shared screen). */
  protected async signOut(): Promise<void> {
    const portal = this.auth.isPortal();
    this.signingOut.set(true);
    try {
      await this.auth.logout();
    } catch {
      // Signed out on this device either way.
    } finally {
      this.signingOut.set(false);
      await this.router.navigateByUrl(portal ? '/portal/login' : '/login');
    }
  }

  private destination(): string {
    return landingUrl(this.auth.isPortal(), this.returnUrl());
  }
}
