import { TestBed } from '@angular/core/testing';

import { THEME_STORAGE_KEY, ThemeService } from './theme.service';

describe('ThemeService', () => {
  const root = document.documentElement;

  beforeEach(() => {
    localStorage.clear();
    root.classList.remove('dark');
  });

  function create(): ThemeService {
    const service = TestBed.inject(ThemeService);
    TestBed.tick();
    return service;
  }

  it('follows the system setting when nothing is saved', () => {
    const theme = create();

    expect(theme.preference()).toBe('system');
  });

  it('applies dark mode to the page and remembers the choice', () => {
    const theme = create();

    theme.setPreference('dark');
    TestBed.tick();

    expect(theme.resolved()).toBe('dark');
    expect(root.classList.contains('dark')).toBe(true);
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');
  });

  it('switches back to light', () => {
    const theme = create();
    theme.setPreference('dark');
    TestBed.tick();

    theme.setPreference('light');
    TestBed.tick();

    expect(root.classList.contains('dark')).toBe(false);
  });

  it('restores a saved choice', () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'dark');

    expect(create().preference()).toBe('dark');
  });

  it('ignores a corrupted saved value', () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'purple');

    expect(create().preference()).toBe('system');
  });
});
