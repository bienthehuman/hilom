import { expect, test, type Page } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';

/** A unique, code-safe ward code per project and run. */
const uniqueCode = () =>
  `E${test.info().project.name[0]}${Date.now().toString(36)}${Math.floor(Math.random() * 100)}`.toUpperCase();

async function addBed(page: Page, label: string): Promise<void> {
  await page.getByLabel('New bed label').fill(label);
  await page.getByRole('button', { name: 'Add bed' }).click();
  await expect(
    page.getByRole('list', { name: 'Beds' }).getByText(label, { exact: true }),
  ).toBeVisible();
}

test.describe('wards', () => {
  test('pages have no serious accessibility violations', async ({ page }) => {
    await page.goto('/facility/wards');
    await expect(page.getByRole('heading', { level: 1, name: 'Wards' })).toBeVisible();
    await expectNoSeriousA11yViolations(page);

    await page.getByRole('button', { name: 'New ward' }).click();
    await expect(page.getByRole('heading', { name: 'New ward' })).toBeVisible();
    await expectNoSeriousA11yViolations(page);
  });

  test('a manager sets up a ward and its beds', async ({ page }) => {
    const code = uniqueCode();
    await page.goto('/facility/wards');
    await page.getByRole('button', { name: 'New ward' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Code').fill(code.toLowerCase());
    await dialog.getByLabel('Name').fill('Medical Ward');
    await dialog.getByRole('combobox', { name: /^Type/ }).click();
    await page.getByRole('option', { name: 'General', exact: true }).click();
    await dialog.getByRole('button', { name: 'Add ward' }).click();

    // Straight to the new ward's page, ready for beds.
    await expect(
      page.getByRole('heading', { level: 1, name: `${code} · Medical Ward` }),
    ).toBeVisible();
    await expect(page.getByText('Capacity: No beds')).toBeVisible();

    await addBed(page, 'Bed 10');
    await addBed(page, 'Bed 2');
    await addBed(page, 'Bed 1');
    await expect(page.getByRole('list', { name: 'Beds' }).getByRole('listitem')).toHaveText([
      /Bed 1\b/,
      /Bed 2\b/,
      /Bed 10\b/,
    ]);
    await expect(page.getByText('Capacity: 3 beds')).toBeVisible();

    await page.getByRole('button', { name: 'Actions for bed Bed 2' }).click();
    await page.getByRole('menuitem', { name: 'Take out of service' }).click();
    await expect(page.getByText('Capacity: 2 beds')).toBeVisible();
    await expect(page.getByText('1 out of service')).toBeVisible();
    await expectNoSeriousA11yViolations(page);

    await page.getByRole('button', { name: 'Deactivate' }).click();
    await page.getByRole('button', { name: 'Deactivate ward' }).click();
    await expect(page.getByText('Ward deactivated')).toBeVisible();
    await expect(page.getByText('Capacity: No beds')).toBeVisible();
  });
});
