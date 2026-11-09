import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, computed, effect, inject, signal } from '@angular/core';

export type ThemePreference = 'light' | 'dark' | 'system';
export type ResolvedTheme = 'light' | 'dark';

/** Same key the inline script in index.html reads, so the first paint matches. */
export const THEME_STORAGE_KEY = 'hilom-theme';

const PREFERENCES: readonly ThemePreference[] = ['light', 'dark', 'system'];

/**
 * Light/dark mode. Follows the system setting by default; a manual choice is remembered on this
 * device. Applies the `dark` class on <html>, which swaps every design token.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly media = this.document.defaultView?.matchMedia?.('(prefers-color-scheme: dark)');
  private readonly systemPrefersDark = signal(this.media?.matches ?? false);

  private readonly _preference = signal<ThemePreference>(this.readStoredPreference());

  /** What the user chose. */
  readonly preference = this._preference.asReadonly();

  /** What is actually shown. */
  readonly resolved = computed<ResolvedTheme>(() => {
    const preference = this._preference();
    if (preference === 'system') {
      return this.systemPrefersDark() ? 'dark' : 'light';
    }
    return preference;
  });

  constructor() {
    const onSystemChange = (event: MediaQueryListEvent) =>
      this.systemPrefersDark.set(event.matches);
    this.media?.addEventListener('change', onSystemChange);
    inject(DestroyRef).onDestroy(() => this.media?.removeEventListener('change', onSystemChange));

    effect(() => {
      this.document.documentElement.classList.toggle('dark', this.resolved() === 'dark');
    });
  }

  setPreference(preference: ThemePreference): void {
    this._preference.set(preference);
    try {
      this.document.defaultView?.localStorage.setItem(THEME_STORAGE_KEY, preference);
    } catch {
      // Storage can be blocked (private mode); the choice still applies for this session.
    }
  }

  private readStoredPreference(): ThemePreference {
    try {
      const stored = this.document.defaultView?.localStorage.getItem(THEME_STORAGE_KEY);
      return PREFERENCES.find((p) => p === stored) ?? 'system';
    } catch {
      return 'system';
    }
  }
}
