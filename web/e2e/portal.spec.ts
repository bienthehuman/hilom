import { expect, test } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { emailCode, passwordField, registerPortalAccount } from './support/accounts';

const PASSWORD = 'Portal-E2E-Password-2026';

/** Unique per project and run, so light and dark runs don't collide. */
const uniqueEmail = () =>
  `maria.${test.info().project.name}.${Date.now()}${Math.floor(Math.random() * 1e4)}@family.test`;

// The portal is designed for phones from 360px wide (docs/DESIGN_SYSTEM.md §6).
test.use({ viewport: { width: 360, height: 740 }, storageState: { cookies: [], origins: [] } });

test.describe('patient portal', () => {
  test('a daughter creates an account, confirms her email and asks to link herself', async ({
    page,
  }) => {
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
    await passwordField(page, 'Password').fill(PASSWORD);
    await page.getByRole('button', { name: 'Create account' }).click();

    // Signed in straight away, then held on the welcome page until the email is confirmed.
    await expect(page).toHaveURL(/\/portal\/welcome$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Confirm your email' })).toBeVisible();
    await expect(page.getByText(email)).toBeVisible();
    await expectNoSeriousA11yViolations(page);
    await page.goto('/portal/appointments');
    await expect(page).toHaveURL(/\/portal\/welcome$/);

    const code = await emailCode(page, email);
    await page.getByLabel('Code').fill(code === '000000' ? '111111' : '000000');
    await page.getByRole('button', { name: 'Confirm email' }).click();
    await expect(page.getByText("That code isn't right.")).toBeVisible();
    await page.getByLabel('Code').fill(code);
    await page.getByRole('button', { name: 'Confirm email' }).click();

    // Who the account is for. "Both" starts with her own record.
    await expect(
      page.getByRole('heading', { level: 1, name: 'Who will you use Hilom for?' }),
    ).toBeVisible();
    await page.getByRole('button', { name: 'Continue' }).click();
    await expect(page.getByText('Choose one to continue.')).toBeVisible();
    await expectNoSeriousA11yViolations(page);
    await page.getByRole('radio', { name: /Both/ }).check();
    await page.getByRole('button', { name: 'Continue' }).click();

    await expect(
      page.getByRole('heading', { level: 1, name: 'Link your own record' }),
    ).toBeVisible();
    await expect(page.getByLabel('Your full name')).toHaveValue('Maria Dela Cruz');
    await expect(page.getByRole('combobox', { name: /You are the patient's/ })).toHaveCount(0);
    await page.getByLabel('Your date of birth').fill('1986-07-14');
    await page.getByRole('button', { name: 'Send request' }).click();

    // Then someone she cares for; "I'm the patient" is no longer a choice.
    await expect(
      page.getByRole('heading', { level: 1, name: 'Link someone you care for' }),
    ).toBeVisible();
    await page.getByRole('combobox', { name: /You are the patient's/ }).click();
    await expect(page.getByRole('option', { name: "I'm the patient" })).toHaveCount(0);
    await page.keyboard.press('Escape');

    // Her own request waits for the hospital, and the Appointments tab says so.
    const tabs = page.getByRole('navigation', { name: 'Portal' });
    await tabs.getByRole('link', { name: 'Appointments' }).click();
    await expect(page.getByText('Waiting for the hospital')).toBeVisible();
    await expect(tabs.getByRole('link', { name: 'Appointments' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    await expectNoSeriousA11yViolations(page);

    // Touch targets are at least 44px tall.
    const tabBox = await tabs.getByRole('link', { name: 'Appointments' }).boundingBox();
    expect(tabBox?.height).toBeGreaterThanOrEqual(44);

    await tabs.getByRole('link', { name: 'Patients' }).click();
    await expect(page.getByRole('list', { name: 'Link requests' })).toContainText(
      'Maria Dela Cruz',
    );
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

    // The account menu in the top bar, as in the staff app.
    await page.getByRole('button', { name: 'Maria Dela Cruz' }).click();
    await expect(page.getByRole('menu')).toContainText(email);
    await expect(page.getByRole('menuitem', { name: 'Change password' })).toBeVisible();
    await expectNoSeriousA11yViolations(page);
    await page.getByRole('menuitem', { name: 'Sign out' }).click();
    await expect(page).toHaveURL(/\/portal\/login$/);

    // Signing in from the staff sign-in page still lands in the portal.
    await page.goto('/login');
    await page.getByLabel('Email').fill(email);
    await passwordField(page, 'Password').fill(PASSWORD);
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page).toHaveURL(/\/portal\/appointments$/);
  });

  test('an email can only be used once', async ({ page }) => {
    const email = uniqueEmail();
    for (const attempt of [1, 2]) {
      await page.goto('/portal/register');
      await page.getByLabel('Your full name').fill('Maria Dela Cruz');
      await page.getByLabel('Email').fill(email);
      await passwordField(page, 'Password').fill(PASSWORD);
      await page.getByRole('button', { name: 'Create account' }).click();
      if (attempt === 1) {
        // "Wrong email?" on the welcome page signs out, back to sign-up.
        await expect(page).toHaveURL(/\/portal\/welcome$/);
        await page.getByRole('button', { name: 'Wrong email? Sign out' }).click();
        await expect(page).toHaveURL(/\/portal\/register$/);
      }
    }

    await expect(page.getByText('An account with this email already exists.')).toBeVisible();
  });
});

test.describe('the portal on a computer', () => {
  test.use({ viewport: { width: 1280, height: 800 } });

  test('puts the tabs in the top bar and uses the wider screen', async ({ page }) => {
    await registerPortalAccount(page, {
      name: 'Maria Dela Cruz',
      email: uniqueEmail(),
      password: PASSWORD,
    });
    await page.getByRole('button', { name: 'Skip for now' }).click();
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

    // Change password from the account menu, then back where they were.
    await page.getByRole('button', { name: 'Maria Dela Cruz' }).click();
    await page.getByRole('menuitem', { name: 'Change password' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Change password' })).toBeVisible();
    // Every password field has its own show/hide button.
    for (const field of ['current password', 'new password', 'confirmed password']) {
      await expect(page.getByRole('button', { name: `Show ${field}` })).toBeVisible();
    }
    await page.getByRole('button', { name: 'Show confirmed password' }).click();
    await expect(page.locator('#confirm-password')).toHaveAttribute('type', 'text');
    await expect(page.locator('#new-password')).toHaveAttribute('type', 'password');
    await expectNoSeriousA11yViolations(page);
    await page.getByRole('button', { name: 'Cancel' }).click();
    await expect(page).toHaveURL(/\/portal\/appointments$/);
  });
});
