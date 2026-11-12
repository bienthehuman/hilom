import { readFile } from 'node:fs/promises';

import { expect, test } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { apiPost } from './support/staff';

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

  test("a patient's access history leads to the log, with related entries, export and an integrity check", async ({
    page,
  }) => {
    await page.goto('/');
    const lastName = `Audited${test.info().project.name}${Date.now()}`;
    const patient = await apiPost<{ id: string; mrn: string }>(page, '/api/patients', {
      firstName: 'Lorna',
      lastName,
      birthDate: '1971-06-02',
      sex: 'female',
      address: '4 Rizal Ave., Pasig City',
      contactNumber: '0917 555 0142',
      emergencyContactName: 'Ben Villanueva',
      emergencyContactRelation: 'Son',
      emergencyContactNumber: '0918 555 0100',
    });

    await page.goto(`/patients/${patient.id}`);
    await expect(page.getByRole('heading', { level: 1, name: `${lastName}, Lorna` })).toBeVisible();
    await page.getByRole('tab', { name: 'Access history' }).click();
    const history = page.getByRole('table', { name: 'Access history' });
    await expect(
      history.getByRole('row').filter({ hasText: 'Patient record opened' }),
    ).toBeVisible();
    await expect(history.getByRole('row').filter({ hasText: 'Patient registered' })).toBeVisible();
    await expect(history.getByRole('row').filter({ hasText: 'Patient created' })).toContainText(
      'contactNumber',
    );
    await expectNoSeriousA11yViolations(page);

    await page.getByRole('link', { name: 'Open in audit log' }).click();
    await expect(page).toHaveURL(new RegExp(`/admin/audit\\?patientId=${patient.id}`));
    await expect(page.getByText(`Showing entries about patient ${patient.mrn}`)).toBeVisible();

    // The registration event lists the data change written by the same request.
    const registered = page.getByRole('row').filter({ hasText: 'Patient registered' });
    await registered.getByRole('button', { name: /^Details of Patient registered/ }).click();
    const related = page.getByRole('list', { name: 'Related entries' });
    await related.getByRole('button', { name: /Patient created/ }).click();
    await expect(page.getByRole('heading', { name: 'Patient created' })).toBeVisible();
    await expectNoSeriousA11yViolations(page);
    await page.keyboard.press('Escape');

    const download = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Export CSV' }).click();
    const file = await download;
    expect(file.suggestedFilename()).toMatch(/^audit-log-\d{4}-\d{2}-\d{2}\.csv$/);
    const text = await readFile(await file.path(), 'utf8');
    expect(text).toContain('occurred_at,event,label');
    expect(text).toContain('patients.patient.registered');

    await page.getByRole('button', { name: 'Check integrity' }).click();
    await expect(page.getByRole('heading', { name: 'The log is intact' })).toBeVisible();
    await expectNoSeriousA11yViolations(page);
  });
});
