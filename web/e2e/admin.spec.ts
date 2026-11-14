import { expect, test, type Page } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { replaceTemporaryPassword } from './support/accounts';

const TEMP_PASSWORD = 'Temporary-Pass-2026';

/** Unique per project and run, so light and dark runs don't collide. */
const unique = (prefix: string) =>
  `${prefix}-${test.info().project.name}-${Date.now()}-${Math.floor(Math.random() * 1e4)}`;

async function createUser(page: Page, name: string, email: string, role: string): Promise<void> {
  await page.goto('/admin/users');
  await page.getByRole('button', { name: 'New user' }).click();
  const sheet = page.getByRole('dialog');
  await sheet.getByLabel('Full name').fill(name);
  await sheet.getByLabel('Email').fill(email);
  await sheet.getByLabel('Temporary password').fill(TEMP_PASSWORD);
  await sheet.getByRole('checkbox', { name: new RegExp(`^${role}`) }).click();
  await sheet.getByRole('button', { name: 'Create user' }).click();
  await expect(page.getByText('User created')).toBeVisible();
}

test.describe('user administration', () => {
  test('pages have no serious accessibility violations', async ({ page }) => {
    await page.goto('/admin/users');
    await expect(page.getByRole('heading', { level: 1, name: 'Users' })).toBeVisible();
    await expect(page.getByText('System Administrator').first()).toBeVisible();
    await expect(page.getByRole('combobox', { name: 'Status' })).toContainText('All users');
    await expectNoSeriousA11yViolations(page);

    await page.getByRole('button', { name: 'New user' }).click();
    // The sheet's outer dialog container has no size of its own; check the panel's heading instead.
    await expect(page.getByRole('heading', { name: 'New user' })).toBeVisible();
    await expectNoSeriousA11yViolations(page);
  });

  test('a new user replaces the temporary password, then sees only what their role allows', async ({
    page,
    browser,
  }) => {
    const email = `${unique('nurse')}@hilom.test`;
    await createUser(page, 'Reyes, Ana', email, 'Staff Nurse');

    await page.getByLabel('Search', { exact: true }).fill(email);
    await expect(page.getByText(email)).toBeVisible();

    const nurse = await browser.newContext({ storageState: { cookies: [], origins: [] } });
    const nursePage = await nurse.newPage();
    await nursePage.goto('/login');
    await nursePage.getByLabel('Email').fill(email);
    await nursePage.getByLabel('Password', { exact: true }).fill(TEMP_PASSWORD);
    await nursePage.getByRole('button', { name: 'Sign in' }).click();

    // Held on the change-password page: going elsewhere leads straight back.
    await expect(
      nursePage.getByRole('heading', { level: 1, name: 'Choose your own password' }),
    ).toBeVisible();
    await expectNoSeriousA11yViolations(nursePage);
    await nursePage.goto('/admin/users');
    await expect(nursePage).toHaveURL(/\/account\/password/);

    await replaceTemporaryPassword(nursePage, TEMP_PASSWORD, 'Ana-Chose-This-2026');

    // Lands where they were headed. The nurse can't manage users, so that's the no-access page.
    await expect(nursePage.getByText("You don't have access to this page")).toBeVisible();
    await nursePage.goto('/');
    await expect(nursePage.getByRole('heading', { level: 1, name: /Reyes, Ana/ })).toBeVisible();

    const nav = nursePage.getByRole('navigation', { name: 'Main' });
    await expect(nav.getByRole('link', { name: 'Home' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Users' })).toHaveCount(0);

    await nursePage.goto('/admin/users');
    await expect(nursePage.getByText("You don't have access to this page")).toBeVisible();
    await nurse.close();
  });

  test('disabling a user asks for confirmation and updates their status', async ({ page }) => {
    const id = unique('leaver');
    const name = `Cruz, Leo ${id}`;
    await createUser(page, name, `${id}@hilom.test`, 'Front Desk');
    await page.getByLabel('Search', { exact: true }).fill(id);
    // Wait for the search to apply (it waits for a pause in typing) so only this user is listed.
    await expect(page.getByRole('table', { name: 'Users' }).getByRole('row')).toHaveCount(2);

    await page.getByRole('button', { name: `Actions for ${name}` }).click();
    await page.getByRole('menuitem', { name: 'Disable' }).click();
    await expect(page.getByRole('alertdialog')).toContainText(`Disable ${name}?`);
    await page.getByRole('button', { name: 'Disable user' }).click();

    await expect(page.getByText('User disabled')).toBeVisible();
    await expect(page.getByRole('row', { name: new RegExp(id) })).toContainText('Disabled');
  });
});

test.describe('role administration', () => {
  test('pages have no serious accessibility violations', async ({ page }) => {
    await page.goto('/admin/roles');
    await expect(page.getByRole('heading', { level: 1, name: 'Roles' })).toBeVisible();
    await expect(page.getByText('Protected')).toBeVisible();
    await expectNoSeriousA11yViolations(page);

    await page.getByRole('link', { name: /Physician/ }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Physician' })).toBeVisible();
    await expectNoSeriousA11yViolations(page);
  });

  test('the System Administrator role is read-only', async ({ page }) => {
    await page.goto('/admin/roles');
    await page.getByRole('link', { name: /System Administrator/ }).click();

    await expect(page.getByText('Protected role')).toBeVisible();
    await expect(page.getByLabel('Name')).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Save role' })).toHaveCount(0);
  });

  test('creates a role from the permission grid', async ({ page }) => {
    const name = unique('Ward clerks');
    await page.goto('/admin/roles/new');
    await page.getByLabel('Name').fill(name);
    await page.getByLabel('See demographics and allergies: Ward').check();

    // Mixing portal-linked and staff-wide access shows a warning.
    await page.getByLabel('See appointments: Linked').check();
    await expect(page.getByText('Portal and staff access mixed')).toBeVisible();
    await page.getByLabel('See appointments: not granted').check();
    await expect(page.getByText('Portal and staff access mixed')).toHaveCount(0);

    await page.getByRole('button', { name: 'Create role' }).click();

    await expect(page.getByText('Role created')).toBeVisible();
    await expect(page).toHaveURL(/\/admin\/roles\/[0-9a-f-]{36}$/);
    await expect(page.getByLabel('See demographics and allergies: Ward')).toBeChecked();
  });
});
