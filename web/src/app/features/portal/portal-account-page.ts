import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { HlmButtonImports } from '@hilom/ui/button';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLogOut } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { AuthService } from '../../core/auth/auth.service';

/** Who is signed in, and signing out. */
@Component({
  selector: 'app-portal-account-page',
  imports: [NgIcon, HlmButtonImports],
  providers: [provideIcons({ lucideLogOut })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="mb-6 text-2xl font-semibold tracking-tight">Account</h1>
    @if (auth.user(); as user) {
      <dl class="flex flex-col gap-4 rounded-lg border bg-card p-6">
        <div>
          <dt class="text-sm text-muted-foreground">Name</dt>
          <dd class="font-medium">{{ user.displayName }}</dd>
        </div>
        <div>
          <dt class="text-sm text-muted-foreground">Email</dt>
          <dd class="font-medium break-all">{{ user.email }}</dd>
        </div>
      </dl>
    }
    <button
      hlmBtn
      variant="outline"
      type="button"
      class="mt-6 w-full"
      [disabled]="signingOut()"
      (click)="signOut()"
    >
      <ng-icon name="lucideLogOut" aria-hidden="true" data-icon="inline-start" />
      {{ signingOut() ? 'Signing out…' : 'Sign out' }}
    </button>
  `,
})
export class PortalAccountPage {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly signingOut = signal(false);

  protected async signOut(): Promise<void> {
    this.signingOut.set(true);
    try {
      await this.auth.logout();
    } catch {
      toast.warning('Signed out on this device', {
        description:
          "We couldn't reach the hospital's server, so the session may stay open until it expires.",
      });
    } finally {
      this.signingOut.set(false);
      await this.router.navigateByUrl('/portal/login');
    }
  }
}
