import { expect, type Locator, type Page } from '@playwright/test';

/** The development System Administrator, seeded from api/src/Hilom.Api/appsettings.Development.json. */
export const ADMIN = {
  email: process.env['E2E_ADMIN_EMAIL'] ?? 'admin@hilom.test',
  password: process.env['E2E_ADMIN_PASSWORD'] ?? 'Hilom-Dev-Admin-2026',
  displayName: 'System Administrator',
};

/** Signed-in browser state shared by tests (written by auth.setup.ts, git-ignored). */
export const ADMIN_STORAGE_STATE = 'e2e/.auth/admin.json';

/** Mailpit's web API, which catches the emails the API sends (docker compose locally, a service in CI). */
export const MAILPIT_URL =
  process.env['E2E_MAILPIT_URL'] ??
  `http://localhost:${process.env['HILOM_MAIL_UI_PORT'] ?? '8025'}`;

/**
 * A password input by its label. `getByLabel` alone also finds the field's "Show …" button, whose
 * accessible name repeats the label.
 */
export function passwordField(page: Page, label: string | RegExp): Locator {
  return page.locator('input').and(page.getByLabel(label));
}

/**
 * Someone an administrator created signs in with the temporary password and is held on the change-password
 * page; this replaces it, the way they would, and waits until they're through.
 */
export async function replaceTemporaryPassword(
  page: Page,
  temporaryPassword: string,
  newPassword: string,
): Promise<void> {
  await expect(
    page.getByRole('heading', { level: 1, name: 'Choose your own password' }),
  ).toBeVisible();
  await passwordField(page, 'Temporary password').fill(temporaryPassword);
  // Case-sensitive, so it skips "Confirm new password".
  await passwordField(page, /New password/).fill(newPassword);
  await page.getByLabel('Confirm new password').fill(newPassword);
  await page.getByRole('button', { name: 'Change password' }).click();
  await expect(page.getByText('Password changed')).toBeVisible();
}

/** The code in the newest confirmation email sent to this address (Mailpit catches it). */
export async function emailCode(page: Page, email: string): Promise<string> {
  let code: string | undefined;
  await expect
    .poll(async () => {
      const search = await page.request.get(`${MAILPIT_URL}/api/v1/search`, {
        params: { query: `to:"${email}" subject:"Your Hilom code"` },
      });
      const { messages } = (await search.json()) as { messages: { Subject: string }[] };
      code = /[0-9]{6}/.exec(messages[0]?.Subject ?? '')?.[0];
      return code !== undefined;
    })
    .toBe(true);
  return code!;
}

/**
 * Creates a portal account on the sign-up page and confirms the email with the code from Mailpit, the way
 * a family member would. Leaves the page on the welcome page's "Who will you use Hilom for?" step.
 */
export async function registerPortalAccount(
  page: Page,
  account: { name: string; email: string; password: string },
): Promise<void> {
  await page.goto('/portal/register');
  await page.getByLabel('Your full name').fill(account.name);
  await page.getByLabel('Email').fill(account.email);
  await passwordField(page, 'Password').fill(account.password);
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Confirm your email' })).toBeVisible();
  await page.getByLabel('Code').fill(await emailCode(page, account.email));
  await page.getByRole('button', { name: 'Confirm email' }).click();
  await expect(
    page.getByRole('heading', { level: 1, name: 'Who will you use Hilom for?' }),
  ).toBeVisible();
}
