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
 * The patient portal (docs/DESIGN_SYSTEM.md §6). Phones get a top bar and bottom tabs; from the `md`
 * breakpoint the tabs move into the top bar and the content widens, so it reads as a website on a
 * computer rather than a stretched phone app. Same tokens as the staff app, with 16px type and 44px touch
 * targets (the `portal` class). Only one of the two navs is displayed at a time, so there's one Portal
 * landmark.
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
      <div class="mx-auto flex h-14 max-w-2xl items-center gap-6 px-4 md:h-16 md:max-w-5xl md:px-6">
        <a routerLink="/portal" class="flex items-center gap-2 rounded-md font-semibold">
          <span
            class="flex size-8 items-center justify-center rounded-lg bg-primary text-primary-foreground"
          >
            <ng-icon name="lucideHeartPulse" aria-hidden="true" />
          </span>
          Hilom
          <span class="hidden font-normal text-muted-foreground lg:inline">Patient portal</span>
        </a>

        <nav aria-label="Portal" class="hidden h-full md:block">
          <ul class="flex h-full items-stretch gap-1">
            @for (tab of tabs; track tab.route) {
              <li class="flex">
                <a
                  [routerLink]="tab.route"
                  routerLinkActive="border-primary text-foreground"
                  ariaCurrentWhenActive="page"
                  class="flex min-h-11 items-center gap-2 border-b-2 border-transparent px-3 text-muted-foreground hover:text-foreground focus-visible:outline-offset-[-2px]"
                >
                  <ng-icon [name]="tab.icon" size="18" aria-hidden="true" />
                  {{ tab.label }}
                </a>
              </li>
            }
          </ul>
        </nav>

        <ui-theme-toggle class="ms-auto" />
      </div>
    </header>

    <main
      id="portal-main"
      tabindex="-1"
      class="mx-auto w-full max-w-2xl flex-1 px-4 pt-6 pb-28 md:max-w-5xl md:px-6 md:pt-8 md:pb-16"
    >
      <router-outlet />
    </main>

    <nav
      aria-label="Portal"
      class="fixed inset-x-0 bottom-0 z-10 border-t bg-card pb-[env(safe-area-inset-bottom)] md:hidden"
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
