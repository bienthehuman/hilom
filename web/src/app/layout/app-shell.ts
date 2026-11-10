import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { HlmSidebarImports, HlmSidebarService, provideHlmSidebarConfig } from '@hilom/ui/sidebar';
import { ThemeToggle } from '@hilom/ui/theme-toggle';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeftRight,
  lucideBedDouble,
  lucideCalendarCheck,
  lucideCalendarDays,
  lucideClipboardCheck,
  lucideClock,
  lucideContact,
  lucideHeartPulse,
  lucideHouse,
  lucideIdCard,
  lucideListChecks,
  lucidePalette,
  lucideScrollText,
  lucideShieldCheck,
  lucideUsers,
} from '@ng-icons/lucide';

import { AuthService } from '../core/auth/auth.service';
import { navigation, visibleNavigation } from './navigation';
import { UserMenu } from './user-menu';

/**
 * Signed-in layout: sidebar (collapses to icons, a drawer on phones, Ctrl/⌘+B), top bar, and the page.
 */
@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    NgIcon,
    HlmSidebarImports,
    ThemeToggle,
    UserMenu,
  ],
  providers: [
    provideIcons({
      lucideArrowLeftRight,
      lucideBedDouble,
      lucideCalendarCheck,
      lucideCalendarDays,
      lucideClipboardCheck,
      lucideClock,
      lucideContact,
      lucideHeartPulse,
      lucideHouse,
      lucideIdCard,
      lucideListChecks,
      lucidePalette,
      lucideScrollText,
      lucideShieldCheck,
      lucideUsers,
    }),
    // Provided here, not app-wide, so the sidebar code loads with the shell instead of at startup.
    provideHlmSidebarConfig({
      sidebarWidth: '15rem',
      sidebarCookieName: 'hilom.sidebar',
      closeMobileSidebarOnMenuButtonClick: true,
    }),
    HlmSidebarService,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app-shell.html',
})
export class AppShell {
  private readonly auth = inject(AuthService);
  private readonly allSections = navigation();

  /** Rebuilt whenever the user's permissions change. */
  protected readonly sections = computed(() =>
    visibleNavigation(this.allSections, (permission) => this.auth.scopes(permission)),
  );
}
