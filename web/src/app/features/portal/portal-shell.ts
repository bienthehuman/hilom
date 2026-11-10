import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ThemeToggle } from '@hilom/ui/theme-toggle';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCalendarDays,
  lucideCircleUserRound,
  lucideHeartPulse,
  lucideUsersRound,
} from '@ng-icons/lucide';

interface PortalTab {
  label: string;
  route: string;
  icon: string;
}

/**
 * The patient portal: mobile first, a top bar and bottom tabs (docs/DESIGN_SYSTEM.md §6). Same tokens as the
 * staff app, with 16px type and 44px touch targets (the `portal` class).
 */
@Component({
  selector: 'app-portal-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, NgIcon, ThemeToggle],
  providers: [
    provideIcons({ lucideCalendarDays, lucideCircleUserRound, lucideHeartPulse, lucideUsersRound }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'portal flex min-h-dvh flex-col bg-background' },
  template: `
    <a
      href="#portal-main"
      class="sr-only z-50 rounded-md bg-primary px-3 py-2 text-primary-foreground focus:not-sr-only focus:fixed focus:top-2 focus:left-2"
      >Skip to main content</a
    >

    <header class="sticky top-0 z-10 border-b bg-card/95 backdrop-blur">
      <div class="mx-auto flex h-14 max-w-2xl items-center justify-between px-4">
        <a routerLink="/portal" class="flex items-center gap-2 rounded-md font-semibold">
          <span
            class="flex size-8 items-center justify-center rounded-lg bg-primary text-primary-foreground"
          >
            <ng-icon name="lucideHeartPulse" aria-hidden="true" />
          </span>
          Hilom
        </a>
        <ui-theme-toggle />
      </div>
    </header>

    <main id="portal-main" tabindex="-1" class="mx-auto w-full max-w-2xl flex-1 px-4 pt-6 pb-28">
      <router-outlet />
    </main>

    <nav
      aria-label="Portal"
      class="fixed inset-x-0 bottom-0 z-10 border-t bg-card pb-[env(safe-area-inset-bottom)]"
    >
      <ul class="mx-auto grid max-w-2xl grid-cols-3">
        @for (tab of tabs; track tab.route) {
          <li>
            <a
              [routerLink]="tab.route"
              routerLinkActive="text-primary"
              ariaCurrentWhenActive="page"
              class="flex min-h-14 flex-col items-center justify-center gap-0.5 text-sm text-muted-foreground hover:text-foreground focus-visible:outline-offset-[-2px]"
            >
              <ng-icon [name]="tab.icon" size="22" aria-hidden="true" />
              {{ tab.label }}
            </a>
          </li>
        }
      </ul>
    </nav>
  `,
})
export class PortalShell {
  protected readonly tabs: readonly PortalTab[] = [
    { label: 'Appointments', route: '/portal/appointments', icon: 'lucideCalendarDays' },
    { label: 'Patients', route: '/portal/patients', icon: 'lucideUsersRound' },
    { label: 'Account', route: '/portal/account', icon: 'lucideCircleUserRound' },
  ];
}
