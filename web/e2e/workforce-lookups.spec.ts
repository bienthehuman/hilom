import { expect, test } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';

/** Unique per project and run, so light and dark runs don't collide. */
const unique = (prefix: string) =>
  `${prefix} ${test.info().project.name} ${Date.now()}-${Math.floor(Math.random() * 1e4)}`;

test.describe('reference lists', () => {
  test('pages have no serious accessibility violations', async ({ page }) => {
    await page.goto('/workforce/lists');
    await expect(page).toHaveURL(/\/workforce\/lists\/departments$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Reference lists' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Departments' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    await expectNoSeriousA11yViolations(page);

    await page.getByRole('button', { name: 'New department' }).click();
    await expect(page.getByRole('heading', { name: 'New department' })).toBeVisible();
    await expectNoSeriousA11yViolations(page);
  });

  test('adds, renames, deactivates and reactivates an entry', async ({ page }) => {
    const name = unique('Cardiology');
    await page.goto('/workforce/lists/specializations');
    await expect(page).toHaveTitle('Specializations · Hilom');

    await page.getByRole('button', { name: 'New specialization' }).click();
    await page.getByRole('dialog').getByLabel('Name').fill(name);
    await page.getByRole('button', { name: 'Add' }).click();
    await expect(page.getByText('Specialization added')).toBeVisible();

    await page.getByLabel('Search').fill(name);
    const table = page.getByRole('table', { name: 'Specializations' });
    await expect(table.getByRole('row')).toHaveCount(2);

    await page.getByRole('button', { name: `Actions for ${name}` }).click();
    await page.getByRole('menuitem', { name: 'Rename' }).click();
    await page.getByRole('dialog').getByLabel('Name').fill(`${name} (adult)`);
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText('Specialization renamed')).toBeVisible();

    const row = table.getByRole('row', { name: new RegExp(`${name} \\(adult\\)`) });
    await page.getByRole('button', { name: `Actions for ${name} (adult)` }).click();
    await page.getByRole('menuitem', { name: 'Deactivate' }).click();
    await expect(page.getByText('Specialization deactivated')).toBeVisible();
    await expect(row).toContainText('Inactive');

    await page.getByRole('button', { name: `Actions for ${name} (adult)` }).click();
    await page.getByRole('menuitem', { name: 'Reactivate' }).click();
    await expect(row).toContainText('Active');
  });

  test('shows a duplicate name next to the field', async ({ page }) => {
    const name = unique('Nursing Service');
    await page.goto('/workforce/lists/departments');
    for (const attempt of [name, name.toUpperCase()]) {
      await page.getByRole('button', { name: 'New department' }).click();
      await page.getByRole('dialog').getByLabel('Name').fill(attempt);
      await page.getByRole('button', { name: 'Add' }).click();
    }

    await expect(page.getByRole('dialog')).toContainText(
      'Another department already has this name.',
    );
  });

  test('tabs switch between lists', async ({ page }) => {
    await page.goto('/workforce/lists/departments');
    await page.getByRole('link', { name: 'Positions' }).click();

    await expect(page).toHaveURL(/\/workforce\/lists\/positions$/);
    await expect(page.getByRole('button', { name: 'New position' })).toBeVisible();
    await expect(page.getByRole('table', { name: 'Positions' })).toBeVisible();
  });
});
