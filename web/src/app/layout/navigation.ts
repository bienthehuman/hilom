import { type PermissionScope } from '../api/models/permission-scope';
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
  /** Only show when the permission is held at one of these scopes (e.g. a ward roster needs `all` or `ward`). */
  scopes?: readonly PermissionScope[];
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
      label: 'Patients',
      items: [
        {
          label: 'Patients',
          route: '/patients',
          icon: 'lucideContact',
          permission: 'patients.view',
        },
        {
          label: 'Link requests',
          route: '/link-requests',
          icon: 'lucideLink',
          permission: 'patients.links.manage',
        },
      ],
    },
    {
      label: 'Workforce',
      items: [
        {
          label: 'Employees',
          route: '/workforce/employees',
          icon: 'lucideIdCard',
          permission: 'workforce.employees.view',
        },
        {
          label: 'Duty roster',
          route: '/workforce/roster',
          icon: 'lucideCalendarDays',
          permission: 'workforce.roster.view',
          scopes: ['all', 'ward'],
        },
        {
          label: 'My schedule',
          route: '/workforce/my-schedule',
          icon: 'lucideCalendarCheck',
          permission: 'workforce.roster.view',
        },
        {
          label: 'Shift swaps',
          route: '/workforce/swaps',
          icon: 'lucideArrowLeftRight',
          permission: 'workforce.swaps.request',
          exact: true,
        },
        {
          label: 'Swap approvals',
          route: '/workforce/swaps/review',
          icon: 'lucideClipboardCheck',
          permission: 'workforce.swaps.approve',
        },
        {
          label: 'Shifts',
          route: '/workforce/shifts',
          icon: 'lucideClock',
          permission: 'workforce.shifts.manage',
        },
        {
          label: 'Reference lists',
          route: '/workforce/lists',
          icon: 'lucideListChecks',
          permission: 'workforce.employees.manage',
        },
      ],
    },
    {
      label: 'Facility',
      items: [
        {
          label: 'Wards',
          route: '/facility/wards',
          icon: 'lucideBedDouble',
          permission: 'facility.wards.view',
        },
      ],
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
        {
          label: 'Audit log',
          route: '/admin/audit',
          icon: 'lucideScrollText',
          permission: 'access.audit.view',
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
  scopesOf: (permission: string) => readonly PermissionScope[],
): NavSection[] {
  const visible = (item: NavItem) => {
    if (!item.permission) {
      return true;
    }
    const held = scopesOf(item.permission);
    return item.scopes ? held.some((scope) => item.scopes?.includes(scope)) : held.length > 0;
  };
  return sections
    .map((section) => ({ ...section, items: section.items.filter(visible) }))
    .filter((section) => section.items.length > 0);
}
