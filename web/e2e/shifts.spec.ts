import { expect, test } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';

/** A unique shift code (letters and digits, at most 10) per project and run. */
const uniqueCode = () =>
  `N${test.info().project.name[0]}${Date.now().toString(36).slice(-6)}`.toUpperCase();

test.describe('shifts', () => {
  test('pages have no serious accessibility violations', async ({ page }) => {
    await page.goto('/workforce/shifts');
    await expect(page.getByRole('heading', { level: 1, name: 'Shifts' })).toBeVisible();
    await expectNoSeriousA11yViolations(page);

    await page.getByRole('button', { name: 'New shift' }).click();
    await expect(page.getByRole('heading', { name: 'New shift' })).toBeVisible();
    await expectNoSeriousA11yViolations(page);
  });

  test('a night shift runs into the next day', async ({ page }) => {
    const code = uniqueCode();
    await page.goto('/workforce/shifts');
    await page.getByRole('button', { name: 'New shift' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Code').fill(code.toLowerCase());
    await dialog.getByLabel('Name').fill('Night');
    await dialog.getByLabel('Starts').fill('10 PM');
    await dialog.getByRole('button', { name: 'Add shift' }).click();
    await expect(dialog.getByText('Use 24-hour time, like 06:00 or 22:00.')).toBeVisible();
    await dialog.getByLabel('Starts').fill('22:00');
    await dialog.getByLabel('Ends').fill('06:00');
    await dialog.getByRole('combobox', { name: /^Color/ }).click();
    await page.getByRole('option', { name: 'Night (violet)' }).click();
    await expect(dialog.getByText('Ends the next day.')).toBeVisible();
    await dialog.getByRole('button', { name: 'Add shift' }).click();

    await expect(page.getByText('Shift added')).toBeVisible();
    const row = page.getByRole('row', { name: new RegExp(code) });
    await expect(row).toContainText('22:00–06:00 next day');
    await expect(row).toContainText('8 h');

    await page.getByRole('button', { name: `Actions for ${code}` }).click();
    await page.getByRole('menuitem', { name: 'Edit' }).click();
    await dialog.getByLabel('Ends').fill('07:00');
    await dialog.getByRole('button', { name: 'Save changes' }).click();
    await expect(row).toContainText('22:00–07:00 next day');
    await expect(row).toContainText('9 h');

    await page.getByRole('button', { name: `Actions for ${code}` }).click();
    await page.getByRole('menuitem', { name: 'Deactivate' }).click();
    await expect(row).toContainText('Inactive');
  });
});
