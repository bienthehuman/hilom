import { isDevMode } from '@angular/core';

export interface NavItem {
  label: string;
  route: string;
  /** Lucide icon name; register it in AppShell's provideIcons. */
  icon: string;
  /** Exact match for the home route, prefix match otherwise. */
  exact?: boolean;
  /** Permission needed to see the item. Without it the item is hidden; the route guard and API enforce it too. */
  permission?: string;
}

export interface NavSection {
  label: string;
  items: NavItem[];
}

/** The whole sidebar. Each module adds its section as it's built. */
export function navigation(): NavSection[] {
  const sections: NavSection[] = [
    {
      label: 'Overview',
      items: [{ label: 'Home', route: '/', icon: 'lucideHouse', exact: true }],
    },
    {
      label: 'Administration',
      items: [
        {
          label: 'Users',
          route: '/admin/users',
          icon: 'lucideUsers',
          permission: 'access.users.view',
        },
        {
          label: 'Roles',
          route: '/admin/roles',
          icon: 'lucideShieldCheck',
          permission: 'access.roles.view',
        },
      ],
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

/** Only the items the user may open; sections left empty disappear. */
export function visibleNavigation(
  sections: NavSection[],
  can: (permission: string) => boolean,
): NavSection[] {
  return sections
    .map((section) => ({
      ...section,
      items: section.items.filter((item) => !item.permission || can(item.permission)),
    }))
    .filter((section) => section.items.length > 0);
}
