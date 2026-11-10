import { expect, test, type Page } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { apiPost } from './support/staff';

/** Picks from a select. Required fields' names end in "(required)", so match the start of the label. */
async function choose(page: Page, label: string, option: string): Promise<void> {
  await page.getByRole('combobox', { name: new RegExp(`^${label}\\b`) }).click();
  await page.getByRole('option', { name: option, exact: true }).click();
}

/** Registers a patient through the API and opens their record. */
async function openNewPatient(page: Page): Promise<void> {
  await page.goto('/');
  const lastName = `Allergy${test.info().project.name}${Date.now()}`;
  const patient = await apiPost(page, '/api/patients', {
    firstName: 'Juan',
    lastName,
    birthDate: '1958-03-21',
    sex: 'male',
    address: '12 Mabini St., Marikina City',
    contactNumber: '0917 555 0199',
    emergencyContactName: 'Maria Dela Cruz',
    emergencyContactRelation: 'Daughter',
    emergencyContactNumber: '0918 555 0123',
  });
  await page.goto(`/patients/${patient.id}`);
  await expect(page.getByRole('heading', { level: 1, name: `${lastName}, Juan` })).toBeVisible();
}

async function addAllergy(
  page: Page,
  allergen: string,
  category: string,
  severity: string,
  reaction?: string,
): Promise<void> {
  await page.getByRole('button', { name: 'Add allergy' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Allergen').fill(allergen);
  await choose(page, 'Category', category);
  await choose(page, 'Severity', severity);
  if (reaction) {
    await dialog.getByLabel('Reaction').fill(reaction);
  }
  await dialog.getByRole('button', { name: 'Add allergy' }).click();
  await expect(dialog).toBeHidden();
}

test.describe('allergies', () => {
  test('a severe allergy shows in the header until it is entered in error', async ({ page }) => {
    await openNewPatient(page);
    const banner = page.getByTestId('severe-allergy-banner');
    await expect(page.getByText('No allergies recorded')).toBeVisible();
    await expect(banner).toBeHidden();

    await addAllergy(page, 'Shrimp', 'Food', 'Moderate', 'Hives');
    await expect(banner).toBeHidden();
    await addAllergy(page, 'Penicillin', 'Drug', 'Severe', 'Anaphylaxis');
    await expect(banner).toContainText('Penicillin (Anaphylaxis)');
    await expectNoSeriousA11yViolations(page);

    // The same allergen can't be active twice.
    await page.getByRole('button', { name: 'Add allergy' }).click();
    await page.getByRole('dialog').getByLabel('Allergen').fill('penicillin');
    await choose(page, 'Category', 'Drug');
    await choose(page, 'Severity', 'Mild');
    await page.getByRole('dialog').getByRole('button', { name: 'Add allergy' }).click();
    await expect(page.getByText('already has an active entry for this allergen')).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click();

    // Resolving keeps it in the history.
    await page.getByRole('button', { name: 'Actions for Shrimp' }).click();
    await page.getByRole('menuitem', { name: 'Mark resolved' }).click();
    await page.getByRole('dialog').getByLabel('Note').fill('Outgrown');
    await page.getByRole('dialog').getByRole('button', { name: 'Mark resolved' }).click();
    const history = page.getByRole('list', { name: 'Allergy history' });
    await expect(history).toContainText('Shrimp');
    await expect(history).toContainText('Outgrown');

    // Entered in error needs a reason, and removes the banner.
    await page.getByRole('button', { name: 'Actions for Penicillin' }).click();
    await page.getByRole('menuitem', { name: 'Entered in error' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('button', { name: 'Mark entered in error' }).click();
    await expect(dialog.getByText('Reason is required.')).toBeVisible();
    await dialog.getByLabel('Reason').fill('Wrong patient');
    await dialog.getByRole('button', { name: 'Mark entered in error' }).click();

    await expect(banner).toBeHidden();
    await expect(history).toContainText('Wrong patient');
    await expect(page.getByText('No active allergies.')).toBeVisible();
    await expectNoSeriousA11yViolations(page);
  });
});
