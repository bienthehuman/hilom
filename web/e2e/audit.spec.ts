import { expect, test } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';

test.describe('audit log', () => {
  test('a failed sign-in shows up with its details', async ({ page, browser }) => {
    const email = `ghost-${test.info().project.name}-${Date.now()}@hilom.test`;

    const visitor = await browser.newContext({ storageState: { cookies: [], origins: [] } });
    const login = await visitor.newPage();
    await login.goto('/login');
    await login.getByLabel('Email').fill(email);
    await login.getByLabel('Password', { exact: true }).fill('Not-The-Password-1');
    await login.getByRole('button', { name: 'Sign in' }).click();
    await expect(login.getByText('Email or password is incorrect.')).toBeVisible();
    await visitor.close();

    await page.goto('/admin/audit');
    await expect(page.getByRole('heading', { level: 1, name: 'Audit log' })).toBeVisible();
    await expect(page.getByText('Signed in').first()).toBeVisible();
    await expectNoSeriousA11yViolations(page);

    await page.getByLabel('Who').fill(email);
    const row = page.getByRole('row').filter({ hasText: email });
    await expect(row).toContainText('Sign-in failed');
    await expect(row).toContainText('Critical');

    await row.getByRole('button', { name: /^Details of Sign-in failed/ }).click();
    await expect(page.getByRole('heading', { name: 'Sign-in failed' })).toBeVisible();
    await expect(page.getByText('"reason": "unknown_email"')).toBeVisible();
    await expectNoSeriousA11yViolations(page);
  });

  test('the sidebar links to it for administrators', async ({ page }) => {
    await page.goto('/');
    const nav = page.getByRole('navigation', { name: 'Main' });
    await nav.getByRole('link', { name: 'Audit log' }).click();
    await expect(page).toHaveURL(/\/admin\/audit$/);
  });
});
