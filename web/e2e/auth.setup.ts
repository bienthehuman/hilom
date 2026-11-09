import { expect, test as setup } from '@playwright/test';

import { ADMIN, ADMIN_STORAGE_STATE } from './support/accounts';

/** Signs in once through the real page and saves the session for the other tests. */
setup('sign in as the System Administrator', async ({ page }) => {
  await page.goto('/login');
  await page.getByLabel('Email').fill(ADMIN.email);
  await page.getByLabel('Password', { exact: true }).fill(ADMIN.password);
  await page.getByRole('button', { name: 'Sign in' }).click();

  await expect(page.getByRole('heading', { level: 1, name: /System Administrator/ })).toBeVisible();
  await page.context().storageState({ path: ADMIN_STORAGE_STATE });
});
