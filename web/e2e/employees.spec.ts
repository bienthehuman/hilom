import { expect, test, type Page } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';

/** Unique per project and run, so light and dark runs don't collide. */
const unique = (prefix: string) =>
  `${prefix}${test.info().project.name}${Date.now()}${Math.floor(Math.random() * 1e4)}`;

/** Adds a reference list entry through the API (the lists have their own spec). */
async function addLookup(page: Page, list: string, name: string): Promise<void> {
  await page.goto('/');
  const xsrf = (await page.context().cookies()).find((c) => c.name === 'XSRF-TOKEN')?.value ?? '';
  const response = await page.request.post(`/api/workforce/${list}`, {
    data: { name },
    headers: { 'X-XSRF-TOKEN': xsrf },
  });
  expect(response.status()).toBe(201);
}

/** Picks from a select. Required fields' names end in "(required)", so match the start of the label. */
async function choose(page: Page, label: string, option: string): Promise<void> {
  await page.getByRole('combobox', { name: new RegExp(`^${label}\\b`) }).click();
  await page.getByRole('option', { name: option, exact: true }).click();
}

test.describe('employees', () => {
  test('pages have no serious accessibility violations', async ({ page }) => {
    await page.goto('/workforce/employees');
    await expect(page.getByRole('heading', { level: 1, name: 'Employees' })).toBeVisible();
    await expect(page.getByRole('combobox', { name: 'Status' })).toContainText('Active');
    await expectNoSeriousA11yViolations(page);

    await page.getByRole('link', { name: 'New employee' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'New employee' })).toBeVisible();
    await expectNoSeriousA11yViolations(page);
  });

  test('HR adds a physician, edits them and puts them on leave', async ({ page }) => {
    const id = unique('');
    const department = `Medicine ${id}`;
    const position = `Consultant ${id}`;
    const specialization = `Cardiology ${id}`;
    const lastName = `Santos${id}`;
    await addLookup(page, 'departments', department);
    await addLookup(page, 'positions', position);
    await addLookup(page, 'specializations', specialization);

    await page.goto('/workforce/employees/new');
    await page.getByLabel('First name').fill('Jose');
    await page.getByLabel('Last name').fill(lastName);
    await choose(page, 'Sex', 'Male');
    await page.getByLabel('Date of birth').fill('1980-02-14');
    await choose(page, 'Department', department);
    await choose(page, 'Position', position);
    await choose(page, 'Professional type', 'Physician');

    // The physician section appears, and the license becomes required.
    await expect(page.getByRole('heading', { name: 'Physician profile' })).toBeVisible();
    await page.getByLabel('Hired on').fill('2015-07-01');
    await page.getByRole('button', { name: 'Add employee' }).click();
    await expect(page.getByText('License number is required.')).toBeVisible();
    await expect(page.getByText('Specialization is required.')).toBeVisible();

    await page.getByLabel('License number').fill('0123456');
    await choose(page, 'Specialization', specialization);
    await page.getByRole('button', { name: 'Add employee' }).click();

    await expect(page.getByText('Employee added')).toBeVisible();
    await expect(page.getByRole('heading', { level: 1, name: `Jose ${lastName}` })).toBeVisible();
    await expect(page.getByText(/^EMP-\d{4}-\d{4,}$/)).toBeVisible();
    await expect(page.getByText(specialization)).toBeVisible();
    await expectNoSeriousA11yViolations(page);

    await page.getByRole('link', { name: 'Edit' }).click();
    await expect(page.getByLabel('First name')).toHaveValue('Jose');
    await page.getByLabel('Middle name').fill('Rizal');
    await page.getByRole('button', { name: 'Save changes' }).click();
    await expect(
      page.getByRole('heading', { level: 1, name: `Jose Rizal ${lastName}` }),
    ).toBeVisible();

    await page.getByRole('button', { name: 'Change status' }).click();
    await choose(page, 'Status', 'On leave');
    await page.getByRole('button', { name: 'Save status' }).click();
    await expect(page.getByText('Status updated')).toBeVisible();
    await expect(page.getByRole('main').getByText('On leave', { exact: true })).toBeVisible();

    // On-leave staff are hidden by the default "Active" filter.
    await page.goto('/workforce/employees');
    await page.getByLabel('Search').fill(lastName);
    await expect(page.getByText('No employees match')).toBeVisible();
    await choose(page, 'Status', 'All statuses');
    await expect(page.getByRole('link', { name: `${lastName}, Jose R.` })).toBeVisible();
  });
});
