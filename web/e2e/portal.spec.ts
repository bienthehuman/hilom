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

    // Signed in straight away, on the Appointments tab, which says to link a patient first.
    await expect(page).toHaveURL(/\/portal\/appointments$/);
    await expect(page.getByText('Link a patient to start booking')).toBeVisible();
    const tabs = page.getByRole('navigation', { name: 'Portal' });
    await expect(tabs.getByRole('link', { name: 'Appointments' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    await expectNoSeriousA11yViolations(page);

    // Touch targets are at least 44px tall.
    const tabBox = await tabs.getByRole('link', { name: 'Appointments' }).boundingBox();
    expect(tabBox?.height).toBeGreaterThanOrEqual(44);

    await page.getByRole('link', { name: 'Link a patient' }).click();
    await expect(page).toHaveURL(/\/portal\/patients\/link$/);
    await tabs.getByRole('link', { name: 'Patients' }).click();
    await expect(page.getByText('No linked patients yet')).toBeVisible();
    // Body text is 16px.
    expect(
      await page
        .locator('main p')
        .first()
        .evaluate((p) => getComputedStyle(p).fontSize),
    ).toBe('16px');

    // Staff screens send portal accounts back to the portal.
    await page.goto('/patients');
    await expect(page).toHaveURL(/\/portal\/appointments$/);

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
    await expect(page).toHaveURL(/\/portal\/appointments$/);
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
        await expect(page).toHaveURL(/\/portal\/appointments$/);
        await page.getByRole('link', { name: 'Account' }).click();
        await page.getByRole('button', { name: 'Sign out' }).click();
        await expect(page).toHaveURL(/\/portal\/login$/);
      }
    }

    await expect(page.getByText('An account with this email already exists.')).toBeVisible();
  });
});

test.describe('the portal on a computer', () => {
  test.use({ viewport: { width: 1280, height: 800 } });

  test('puts the tabs in the top bar and uses the wider screen', async ({ page }) => {
    await page.goto('/portal/register');
    await page.getByLabel('Your full name').fill('Maria Dela Cruz');
    await page.getByLabel('Email').fill(uniqueEmail());
    await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
    await page.getByRole('button', { name: 'Create account' }).click();
    await expect(page.getByText('Link a patient to start booking')).toBeVisible();

    // One Portal navigation on screen, in the header, not pinned to the bottom.
    const tabs = page.getByRole('navigation', { name: 'Portal' });
    await expect(tabs).toHaveCount(1);
    const header = page.getByRole('banner');
    await expect(header.getByRole('navigation', { name: 'Portal' })).toBeVisible();
    await expect(tabs.getByRole('link', { name: 'Appointments' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    expect(
      (await tabs.getByRole('link', { name: 'Patients' }).boundingBox())?.height,
    ).toBeGreaterThanOrEqual(44);

    // Wider than the phone column (672px).
    expect((await page.locator('main').boundingBox())?.width).toBeGreaterThan(900);
    await expectNoSeriousA11yViolations(page);

    await tabs.getByRole('link', { name: 'Account' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Account' })).toBeVisible();
    await expectNoSeriousA11yViolations(page);
  });
});
