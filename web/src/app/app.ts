import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HlmToaster } from '@hilom/ui/sonner';
import { ThemeService } from '@hilom/ui/theme';

/**
 * Root shell: the active page plus app-wide toasts, which follow the current theme.
 * The toaster loads once the browser is idle, keeping it out of the startup bundle.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, HlmToaster],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <router-outlet />
    @defer (on idle) {
      <hlm-toaster [theme]="theme.resolved()" position="bottom-right" />
    }
  `,
})
export class App {
  protected readonly theme = inject(ThemeService);
}
