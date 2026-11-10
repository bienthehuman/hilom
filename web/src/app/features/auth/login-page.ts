import {
  ChangeDetectionStrategy,
  Component,
  type OnInit,
  inject,
  input,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { ThemeToggle } from '@hilom/ui/theme-toggle';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCircleAlert,
  lucideCircleCheck,
  lucideEye,
  lucideEyeOff,
  lucideHeartPulse,
  lucideInfo,
} from '@ng-icons/lucide';

import { AuthService } from '../../core/auth/auth.service';
import { landingUrl } from '../../core/auth/auth.guards';
import { type Problem, toProblem } from '../../core/http/problem';

@Component({
  selector: 'app-login-page',
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
  providers: [
    provideIcons({
      lucideHeartPulse,
      lucideCircleAlert,
      lucideCircleCheck,
      lucideInfo,
      lucideEye,
      lucideEyeOff,
    }),
  ],
  templateUrl: './login-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** From route data: the patient portal's sign-in (larger type, links to sign-up). */
  readonly portal = input(false);
  /** From the query string: where to go after signing in. */
  readonly returnUrl = input<string>();
  /** From the query string: `expired` when the session ended, `account_ready` after accepting an invite. */
  readonly reason = input<string>();
  /** From the query string: pre-fills the email, e.g. right after accepting an invite. */
  readonly email = input<string>();

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  ngOnInit(): void {
    const email = this.email();
    if (email) {
      this.form.controls.email.setValue(email);
    }
  }

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
      await this.router.navigateByUrl(landingUrl(this.auth.isPortal(), this.returnUrl()));
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
