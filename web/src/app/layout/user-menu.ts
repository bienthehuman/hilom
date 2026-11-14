import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { HlmAvatarImports } from '@hilom/ui/avatar';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmDropdownMenuImports } from '@hilom/ui/dropdown-menu';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideKeyRound, lucideLogOut } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { CHANGE_PASSWORD_URL } from '../core/auth/auth.guards';
import { AuthService } from '../core/auth/auth.service';

/**
 * Avatar button in the top bar of the staff app and the patient portal: who is signed in, changing the
 * password, and signing out.
 */
@Component({
  selector: 'app-user-menu',
  imports: [HlmAvatarImports, HlmButtonImports, HlmDropdownMenuImports, NgIcon],
  providers: [provideIcons({ lucideLogOut, lucideKeyRound })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The name is the button's label (WCAG 2.5.3); on small screens it's for screen readers only. -->
    <button
      hlmBtn
      variant="ghost"
      type="button"
      class="gap-2 px-1.5 sm:pe-2.5"
      [hlmDropdownMenuTrigger]="menu"
      align="end"
    >
      <hlm-avatar class="size-7" aria-hidden="true">
        <span hlmAvatarFallback class="bg-accent text-xs font-semibold text-accent-foreground">{{
          initials()
        }}</span>
      </hlm-avatar>
      <span class="sr-only max-w-40 truncate sm:not-sr-only">{{ displayName() }}</span>
    </button>

    <ng-template #menu>
      <hlm-dropdown-menu class="w-60">
        <hlm-dropdown-menu-label class="flex flex-col gap-0.5 font-normal">
          <span class="font-medium text-foreground">{{ displayName() }}</span>
          <span class="truncate text-xs text-muted-foreground">{{ email() }}</span>
        </hlm-dropdown-menu-label>
        <hlm-dropdown-menu-separator />
        <button hlmDropdownMenuItem type="button" (triggered)="changePassword()">
          <ng-icon name="lucideKeyRound" aria-hidden="true" />Change password
        </button>
        <button hlmDropdownMenuItem type="button" [disabled]="signingOut()" (triggered)="signOut()">
          <ng-icon name="lucideLogOut" aria-hidden="true" />Sign out
        </button>
      </hlm-dropdown-menu>
    </ng-template>
  `,
})
export class UserMenu {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly signingOut = signal(false);
  protected readonly displayName = computed(() => this.auth.user()?.displayName ?? '');
  protected readonly email = computed(() => this.auth.user()?.email ?? '');
  protected readonly initials = computed(() =>
    this.displayName()
      .split(/[\s,]+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]?.toUpperCase())
      .join(''),
  );

  protected changePassword(): void {
    void this.router.navigate([CHANGE_PASSWORD_URL], {
      queryParams: { returnUrl: this.router.url },
    });
  }

  protected async signOut(): Promise<void> {
    // Read before signing out clears the user.
    const signInPage = this.auth.isPortal() ? '/portal/login' : '/login';
    this.signingOut.set(true);
    try {
      await this.auth.logout();
    } catch {
      toast.warning('Signed out on this device', {
        description: "We couldn't reach the server, so the session may stay open until it expires.",
      });
    } finally {
      this.signingOut.set(false);
      await this.router.navigateByUrl(signInPage);
    }
  }
}
