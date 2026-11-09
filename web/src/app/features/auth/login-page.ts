import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { ThemeToggle } from '@hilom/ui/theme-toggle';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCircleAlert,
  lucideEye,
  lucideEyeOff,
  lucideHeartPulse,
  lucideInfo,
} from '@ng-icons/lucide';

import { AuthService } from '../../core/auth/auth.service';
import { safeReturnUrl } from '../../core/auth/auth.guards';
import { type Problem, toProblem } from '../../core/http/problem';

@Component({
  selector: 'app-login-page',
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
  providers: [
    provideIcons({ lucideHeartPulse, lucideCircleAlert, lucideInfo, lucideEye, lucideEyeOff }),
  ],
  templateUrl: './login-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** From the query string: where to go after signing in. */
  readonly returnUrl = input<string>();
  /** From the query string: `expired` when the session ended. */
  readonly reason = input<string>();

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  protected readonly submitting = signal(false);
  protected readonly showPassword = signal(false);
  protected readonly problem = signal<Problem | null>(null);

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.problem.set(null);
    const { email, password } = this.form.getRawValue();

    try {
      await this.auth.login(email, password);
      await this.router.navigateByUrl(safeReturnUrl(this.returnUrl()));
    } catch (error) {
      const problem = toProblem(error);
      this.applyFieldErrors(problem);
      this.problem.set(problem);
      this.form.controls.password.reset();
    } finally {
      this.submitting.set(false);
    }
  }

  private applyFieldErrors(problem: Problem): void {
    for (const [field, messages] of Object.entries(problem.errors ?? {})) {
      const control = this.form.get(field);
      if (control && messages[0]) {
        control.setErrors({ server: messages[0] });
      }
    }
  }
}
