import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideHeartPulse } from '@ng-icons/lucide';

type ApiState = 'checking' | 'online' | 'offline';

/**
 * Temporary foundation page: proves tokens, font, icons and the /api proxy work end to end.
 * Replaced by the login page and app shell in slice 0.6.
 */
@Component({
  selector: 'app-root',
  imports: [NgIcon],
  viewProviders: [provideIcons({ lucideHeartPulse })],
  templateUrl: './app.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  private readonly health = httpResource.text(() => '/health/ready');

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
