import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, isDevMode } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HlmButtonImports } from '@hilom/ui/button';
import { ThemeToggle } from '@hilom/ui/theme-toggle';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideHeartPulse, lucidePalette } from '@ng-icons/lucide';

type ApiState = 'checking' | 'online' | 'offline';

/**
 * Temporary landing page: proves tokens, font, icons and the /api proxy work end to end.
 * Replaced by the login page and app shell in slice 0.6.
 */
@Component({
  selector: 'app-foundation-page',
  imports: [NgIcon, RouterLink, HlmButtonImports, ThemeToggle],
  providers: [provideIcons({ lucideHeartPulse, lucidePalette })],
  templateUrl: './foundation-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FoundationPage {
  private readonly health = httpResource.text(() => '/health/ready');

  protected readonly showDesignLink = isDevMode();

  protected readonly apiState = computed<ApiState>(() => {
    if (this.health.isLoading()) {
      return 'checking';
    }
    return this.health.hasValue() && this.health.value() === 'Healthy' ? 'online' : 'offline';
  });

  protected readonly tokens = [
    { label: 'Primary', swatch: 'bg-primary' },
    { label: 'Success', swatch: 'bg-success' },
    { label: 'Warning', swatch: 'bg-warning' },
    { label: 'Danger', swatch: 'bg-destructive' },
    { label: 'Info', swatch: 'bg-info' },
  ] as const;
}
