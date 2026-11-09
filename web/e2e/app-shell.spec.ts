import { expect, test } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { ADMIN } from './support/accounts';

test.describe('app shell', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await expect(
      page.getByRole('heading', { level: 1, name: /System Administrator/ }),
    ).toBeVisible();
  });

  test('has no serious accessibility violations', async ({ page }) => {
    await expectNoSeriousA11yViolations(page);
  });

  test('shows who is signed in', async ({ page }) => {
    await page.getByRole('button', { name: ADMIN.displayName }).click();

    await expect(page.getByRole('menu')).toContainText(ADMIN.email);
  });

  test('navigates with the sidebar and marks the current page', async ({ page }) => {
    const nav = page.getByRole('navigation', { name: 'Main' });

    await nav.getByRole('link', { name: 'Design system' }).click();

    await expect(page.getByRole('heading', { level: 1, name: 'Design system' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Design system' })).toHaveAttribute(
      'aria-current',
      'page',
    );
  });

  test('collapses the sidebar to icons', async ({ page }) => {
    const sidebar = page.locator('hlm-sidebar');

    await page.getByRole('button', { name: 'Toggle sidebar' }).click();

    await expect(sidebar).toHaveAttribute('data-state', 'collapsed');
  });
});
