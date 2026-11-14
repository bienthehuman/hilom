import { expect, type Page } from '@playwright/test';

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
  await page.getByLabel('Temporary password').fill(temporaryPassword);
  // Case-sensitive, so it skips "Confirm new password".
  await page.getByLabel(/New password/).fill(newPassword);
  await page.getByLabel('Confirm new password').fill(newPassword);
  await page.getByRole('button', { name: 'Change password' }).click();
  await expect(page.getByText('Password changed')).toBeVisible();
}
