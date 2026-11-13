import { expect, test } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { ADMIN } from './support/accounts';

// These tests start signed out.
test.use({ storageState: { cookies: [], origins: [] } });

test.describe('sign-in', () => {
  test('has no serious accessibility violations', async ({ page }) => {
    await page.goto('/login');
    await expect(page.getByRole('heading', { level: 1, name: 'Sign in to Hilom' })).toBeVisible();

    await expectNoSeriousA11yViolations(page);
  });

  test('offers no demo logins when demo mode is off', async ({ page }) => {
    await page.goto('/login');
    await expect(page.getByRole('heading', { level: 1, name: 'Sign in to Hilom' })).toBeVisible();

    await expect(page.getByRole('heading', { name: 'Try the demo' })).toHaveCount(0);
  });

  test.describe('demo logins', () => {
    // The e2e API isn't in demo mode, so the offer is staged here, using the development administrator as the "demo" account.
    test.beforeEach(async ({ page }) => {
      await page.route('**/api/demo/accounts', (route) =>
        route.fulfill({
          json: {
            enabled: true,
            password: ADMIN.password,
            accounts: [
              {
                role: 'System Administrator',
                email: ADMIN.email,
                displayName: ADMIN.displayName,
                description: 'Manage users and roles, read the audit log.',
                portal: false,
              },
            ],
          },
        }),
      );
    });

    test('are listed and have no serious accessibility violations', async ({ page }) => {
      await page.goto('/login');

      await expect(page.getByRole('heading', { name: 'Try the demo' })).toBeVisible();
      await expectNoSeriousA11yViolations(page);
    });

    test('sign in with one click, by keyboard', async ({ page }) => {
      await page.goto('/login');
      const demo = page.getByRole('button', { name: /System Administrator/ });
      await demo.focus();
      await page.keyboard.press('Enter');

      await expect(
        page.getByRole('heading', { level: 1, name: /System Administrator/ }),
      ).toBeVisible();
    });
  });

  test('explains wrong credentials', async ({ page }) => {
    await page.goto('/login');
    await page.getByLabel('Email').fill('nobody@hilom.test');
    await page.getByLabel('Password', { exact: true }).fill('Not-The-Password-1');
    await page.getByRole('button', { name: 'Sign in' }).click();

    await expect(page.getByText('Email or password is incorrect.')).toBeVisible();
    await expect(page.getByLabel('Password', { exact: true })).toHaveValue('');
  });

  test('sends signed-out visitors to sign-in and back to where they were going', async ({
    page,
  }) => {
    await page.goto('/design');
    await expect(page).toHaveURL(/\/login\?returnUrl=%2Fdesign/);

    await page.getByLabel('Email').fill(ADMIN.email);
    await page.getByLabel('Password', { exact: true }).fill(ADMIN.password);
    await page.getByRole('button', { name: 'Sign in' }).click();

    await expect(page).toHaveURL(/\/design$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Design system' })).toBeVisible();
  });

  test('signs in and out', async ({ page }) => {
    await page.goto('/login');
    await page.getByLabel('Email').fill(ADMIN.email);
    await page.getByLabel('Password', { exact: true }).fill(ADMIN.password);
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(
      page.getByRole('heading', { level: 1, name: /System Administrator/ }),
    ).toBeVisible();

    await page.getByRole('button', { name: ADMIN.displayName }).click();
    await page.getByRole('menuitem', { name: 'Sign out' }).click();

    await expect(page).toHaveURL(/\/login/);
    await page.goto('/');
    await expect(page).toHaveURL(/\/login/);
  });
});
