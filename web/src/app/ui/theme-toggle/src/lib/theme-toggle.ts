import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmDropdownMenuImports } from '@hilom/ui/dropdown-menu';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideMonitor, lucideMoon, lucideSun } from '@ng-icons/lucide';

import { type ThemePreference, ThemeService } from '@hilom/ui/theme';

interface ThemeOption {
  value: ThemePreference;
  label: string;
  icon: string;
}

/** Icon button in the top bar that switches between light, dark and system themes. */
@Component({
  selector: 'ui-theme-toggle',
  imports: [HlmButtonImports, HlmDropdownMenuImports, NgIcon],
  providers: [provideIcons({ lucideSun, lucideMoon, lucideMonitor })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      hlmBtn
      variant="ghost"
      size="icon"
      type="button"
      [hlmDropdownMenuTrigger]="menu"
      [attr.aria-label]="'Theme: ' + currentLabel()"
    >
      <ng-icon [name]="currentIcon()" aria-hidden="true" />
    </button>

    <ng-template #menu>
      <hlm-dropdown-menu class="w-40">
        <hlm-dropdown-menu-label>Theme</hlm-dropdown-menu-label>
        @for (option of options; track option.value) {
          <button
            hlmDropdownMenuRadio
            type="button"
            [checked]="theme.preference() === option.value"
            (triggered)="theme.setPreference(option.value)"
          >
            <hlm-dropdown-menu-radio-indicator />
            <ng-icon [name]="option.icon" aria-hidden="true" />
            {{ option.label }}
          </button>
        }
      </hlm-dropdown-menu>
    </ng-template>
  `,
})
export class ThemeToggle {
  protected readonly theme = inject(ThemeService);

  protected readonly options: readonly ThemeOption[] = [
    { value: 'light', label: 'Light', icon: 'lucideSun' },
    { value: 'dark', label: 'Dark', icon: 'lucideMoon' },
    { value: 'system', label: 'System', icon: 'lucideMonitor' },
  ];

  private readonly current = computed(
    () => this.options.find((o) => o.value === this.theme.preference()) ?? this.options[2]!,
  );

  protected readonly currentIcon = computed(() => this.current().icon);
  protected readonly currentLabel = computed(() => this.current().label);
}
