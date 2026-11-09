import { isDevMode } from '@angular/core';

export interface NavItem {
  label: string;
  route: string;
  /** Lucide icon name; register it in AppShell's provideIcons. */
  icon: string;
  /** Exact match for the home route, prefix match otherwise. */
  exact?: boolean;
  /** Permission needed to see the item. Enforced from slice 0.8. */
  permission?: string;
}

export interface NavSection {
  label: string;
  items: NavItem[];
}

/**
 * The sidebar. Each module adds its section as it's built.
 * The sidebar shows only items the user has permission for (slice 0.8); the API enforces it regardless.
 */
export function navigation(): NavSection[] {
  const sections: NavSection[] = [
    {
      label: 'Overview',
      items: [{ label: 'Home', route: '/', icon: 'lucideHouse', exact: true }],
    },
  ];

  if (isDevMode()) {
    sections.push({
      label: 'Development',
      items: [{ label: 'Design system', route: '/design', icon: 'lucidePalette' }],
    });
  }

  return sections;
}
