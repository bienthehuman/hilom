import { expect, test, type Page } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { createStaff, signInAs } from './support/staff';

/** Unique per project and run, so light and dark runs don't collide. */
const unique = (prefix: string) =>
  `${prefix}${test.info().project.name}${Date.now()}${Math.floor(Math.random() * 1e4)}`;

/** Picks from a select. Required fields' names end in "(required)", so match the start of the label. */
async function choose(page: Page, label: string, option: string): Promise<void> {
  await page.getByRole('combobox', { name: new RegExp(`^${label}\\b`) }).click();
  await page.getByRole('option', { name: option, exact: true }).click();
}

test.describe('patients', () => {
  test('pages have no serious accessibility violations', async ({ page }) => {
    await page.goto('/patients');
    await expect(page.getByRole('heading', { level: 1, name: 'Patients' })).toBeVisible();
    await expectNoSeriousA11yViolations(page);

    await page.getByRole('link', { name: 'Register patient' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Register patient' })).toBeVisible();
    await expectNoSeriousA11yViolations(page);
  });

  test('front desk registers a patient, finds them and updates their contact number', async ({
    page,
    browser,
  }) => {
    await page.goto('/');
    const frontDesk = await signInAs(
      browser,
      await createStaff(page, 'Rina', unique('Desk'), 'Front Desk'),
    );
    const lastName = unique('DelaCruz');

    const sidebar = frontDesk.getByRole('navigation', { name: 'Main' });
    await sidebar.getByRole('link', { name: 'Patients' }).click();
    await frontDesk.getByLabel('Search').fill(lastName);
    await expect(frontDesk.getByText('No patients match')).toBeVisible();

    await frontDesk.getByRole('link', { name: 'Register patient' }).click();
    await frontDesk.getByLabel('First name').fill('Juan');
    await frontDesk.getByLabel('Middle name').fill('Santos');
    await frontDesk.getByLabel('Last name').fill(lastName);
    await frontDesk.getByRole('button', { name: 'Register patient' }).click();
    await expect(frontDesk.getByText('Date of birth is required.')).toBeVisible();
    await expect(frontDesk.getByText('Home address is required.')).toBeVisible();

    await frontDesk.getByLabel('Date of birth').fill('1958-03-21');
    await choose(frontDesk, 'Sex', 'Male');
    await choose(frontDesk, 'Civil status', 'Married');
    await frontDesk.getByLabel('Home address').fill('12 Mabini St., Marikina City');
    // Two fields are labelled "Contact number" (the patient's and the emergency contact's).
    await frontDesk.locator('#contact-number').fill('0917 555 0199');
    await frontDesk.locator('#emergency-name').fill('Maria Dela Cruz');
    await frontDesk.getByLabel('Relationship').fill('Daughter');
    await frontDesk.locator('#emergency-number').fill('0918 555 0123');
    await frontDesk.getByRole('button', { name: 'Register patient' }).click();

    // The record opens with the patient header: name last-name first, a new MRN, age and sex.
    await expect(frontDesk.getByText('Patient registered')).toBeVisible();
    const heading = frontDesk.getByRole('heading', {
      level: 1,
      name: `${lastName}, Juan Santos`,
    });
    await expect(heading).toBeVisible();
    await expect(frontDesk.getByText(/^MRN-\d{6,}$/)).toBeVisible();
    await expect(frontDesk.getByText(/^\d+ y · Male$/)).toBeVisible();
    await expect(frontDesk.getByText('Maria Dela Cruz (Daughter)')).toBeVisible();
    await expectNoSeriousA11yViolations(frontDesk);
    const mrn = (await frontDesk.getByText(/^MRN-\d{6,}$/).textContent())!.trim();

    // Found by MRN and by date of birth.
    await sidebar.getByRole('link', { name: 'Patients' }).click();
    await frontDesk.getByLabel('Search').fill(mrn);
    await expect(frontDesk.getByRole('link', { name: `${lastName}, Juan Santos` })).toBeVisible();
    await frontDesk.getByLabel('Search').fill(lastName);
    await frontDesk.getByLabel('Date of birth').fill('1990-01-01');
    await expect(frontDesk.getByText('No patients match')).toBeVisible();
    await frontDesk.getByLabel('Date of birth').fill('1958-03-21');
    await frontDesk.getByRole('link', { name: `${lastName}, Juan Santos` }).click();

    await frontDesk.getByRole('link', { name: 'Edit' }).click();
    await expect(frontDesk.getByLabel('First name')).toHaveValue('Juan');
    await frontDesk.locator('#contact-number').fill('0919 555 0000');
    await frontDesk.getByRole('button', { name: 'Save changes' }).click();

    await expect(frontDesk.getByText('Changes saved')).toBeVisible();
    await expect(heading).toBeVisible();
    await expect(frontDesk.getByText('0919 555 0000')).toBeVisible();
    await frontDesk.context().close();
  });
});
