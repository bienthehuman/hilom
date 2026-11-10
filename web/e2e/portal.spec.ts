import { expect, test } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';

const PASSWORD = 'Portal-E2E-Password-2026';

/** Unique per project and run, so light and dark runs don't collide. */
const uniqueEmail = () =>
  `maria.${test.info().project.name}.${Date.now()}${Math.floor(Math.random() * 1e4)}@family.test`;

// The portal is designed for phones from 360px wide (docs/DESIGN_SYSTEM.md §6).
test.use({ viewport: { width: 360, height: 740 }, storageState: { cookies: [], origins: [] } });

test.describe('patient portal', () => {
  test('a daughter creates an account and lands in an empty portal', async ({ page }) => {
    const email = uniqueEmail();

    await page.goto('/portal/login');
    await expect(
      page.getByRole('heading', { level: 1, name: 'Sign in to the patient portal' }),
    ).toBeVisible();
    await expectNoSeriousA11yViolations(page);

    await page.getByRole('link', { name: 'Create an account' }).click();
    await expect(
      page.getByRole('heading', { level: 1, name: 'Create your portal account' }),
    ).toBeVisible();
    await page.getByRole('button', { name: 'Create account' }).click();
    await expect(page.getByText('Your full name is required.')).toBeVisible();
    await expectNoSeriousA11yViolations(page);

    await page.getByLabel('Your full name').fill('Maria Dela Cruz');
    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
    await page.getByRole('button', { name: 'Create account' }).click();

    // Signed in straight away, on the Patients tab, with nothing linked yet.
    await expect(page).toHaveURL(/\/portal\/patients$/);
    await expect(page.getByText('No linked patients yet')).toBeVisible();
    const tabs = page.getByRole('navigation', { name: 'Portal' });
    await expect(tabs.getByRole('link', { name: 'Patients' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    await expectNoSeriousA11yViolations(page);

    // Touch targets are at least 44px tall, and body text is 16px.
    const tabBox = await tabs.getByRole('link', { name: 'Appointments' }).boundingBox();
    expect(tabBox?.height).toBeGreaterThanOrEqual(44);
    expect(
      await page
        .locator('main p')
        .first()
        .evaluate((p) => getComputedStyle(p).fontSize),
    ).toBe('16px');

    await tabs.getByRole('link', { name: 'Appointments' }).click();
    await expect(page.getByText('No upcoming appointments')).toBeVisible();

    // Staff screens send portal accounts back to the portal.
    await page.goto('/patients');
    await expect(page).toHaveURL(/\/portal\/patients$/);

    await tabs.getByRole('link', { name: 'Account' }).click();
    await expect(page.getByText(email)).toBeVisible();
    await expectNoSeriousA11yViolations(page);
    await page.getByRole('button', { name: 'Sign out' }).click();
    await expect(page).toHaveURL(/\/portal\/login$/);

    // Signing in from the staff sign-in page still lands in the portal.
    await page.goto('/login');
    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page).toHaveURL(/\/portal\/patients$/);
  });

  test('an email can only be used once', async ({ page }) => {
    const email = uniqueEmail();
    for (const attempt of [1, 2]) {
      await page.goto('/portal/register');
      await page.getByLabel('Your full name').fill('Maria Dela Cruz');
      await page.getByLabel('Email').fill(email);
      await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
      await page.getByRole('button', { name: 'Create account' }).click();
      if (attempt === 1) {
        await expect(page).toHaveURL(/\/portal\/patients$/);
        await page.getByRole('link', { name: 'Account' }).click();
        await page.getByRole('button', { name: 'Sign out' }).click();
        await expect(page).toHaveURL(/\/portal\/login$/);
      }
    }

    await expect(page.getByText('An account with this email already exists.')).toBeVisible();
  });
});
