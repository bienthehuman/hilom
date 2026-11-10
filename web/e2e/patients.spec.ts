import { expect, test, type Page } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { apiPost, createStaff, signInAs } from './support/staff';

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
    await frontDesk.getByLabel('Search', { exact: true }).fill(lastName);
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
    await frontDesk.getByLabel('Search', { exact: true }).fill(mrn);
    await expect(frontDesk.getByRole('link', { name: `${lastName}, Juan Santos` })).toBeVisible();
    await frontDesk.getByLabel('Search', { exact: true }).fill(lastName);
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

  test('the top-bar search finds a patient by name or MRN from any page', async ({ page }) => {
    await page.goto('/');
    const lastName = unique('Search');
    const patient = await apiPost<{ id: string; mrn: string }>(page, '/api/patients', {
      firstName: 'Lorna',
      lastName,
      birthDate: '1990-07-04',
      sex: 'female',
      address: '5 Rizal Ave., Pasig City',
      contactNumber: '0917 555 0111',
      emergencyContactName: 'Ben Cruz',
      emergencyContactRelation: 'Husband',
      emergencyContactNumber: '0918 555 0222',
    });

    // Ctrl+K from another page, type, arrow down, Enter.
    await page.goto('/workforce/employees');
    const search = page.getByRole('combobox', { name: 'Search patients' });
    await expect(search).toBeVisible();
    await page.keyboard.press('ControlOrMeta+k');
    await expect(search).toBeFocused();
    await search.fill(lastName);
    const option = page.getByRole('option', { name: new RegExp(`^${lastName}, Lorna`) });
    await expect(option).toBeVisible();
    await expectNoSeriousA11yViolations(page);
    await search.press('ArrowDown');
    await search.press('Enter');
    await expect(page.getByRole('heading', { level: 1, name: `${lastName}, Lorna` })).toBeVisible();

    // By MRN, then "See all results" opens the registry filtered by what was typed.
    await search.fill(patient.mrn);
    await expect(page.getByRole('option', { name: /See all 1 result/ })).toBeVisible();
    await search.press('Enter');
    await expect(page).toHaveURL(new RegExp(`/patients\\?q=${patient.mrn}$`));
    await expect(page.getByLabel('Search', { exact: true })).toHaveValue(patient.mrn);
    await expect(page.getByRole('link', { name: `${lastName}, Lorna` })).toBeVisible();
  });
});
