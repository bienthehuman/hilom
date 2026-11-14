import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { HlmButtonImports } from '@hilom/ui/button';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideKeyRound, lucideLogOut } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { AuthService } from '../../core/auth/auth.service';

/** Who is signed in, changing the password, and signing out. */
@Component({
  selector: 'app-portal-account-page',
  imports: [NgIcon, RouterLink, HlmButtonImports],
  providers: [provideIcons({ lucideLogOut, lucideKeyRound })],
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
    <a
      hlmBtn
      variant="outline"
      class="mt-6 w-full"
      routerLink="/account/password"
      [queryParams]="{ returnUrl: '/portal/account' }"
    >
      <ng-icon name="lucideKeyRound" aria-hidden="true" data-icon="inline-start" />
      Change password
    </a>
    <button
      hlmBtn
      variant="outline"
      type="button"
      class="mt-3 w-full"
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
