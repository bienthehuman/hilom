import { expect, test, type Browser, type Page } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { apiPost } from './support/staff';

const PASSWORD = 'Portal-E2E-Password-2026';

/** Picks from a select. Required fields' names end in "(required)", so match the start of the label. */
async function choose(page: Page, label: string, option: string): Promise<void> {
  await page.getByRole('combobox', { name: new RegExp(`^${label}`) }).click();
  await page.getByRole('option', { name: option, exact: true }).click();
}

/** A daughter's portal account, on a phone. */
async function portalUser(browser: Browser): Promise<Page> {
  const context = await browser.newContext({
    storageState: { cookies: [], origins: [] },
    viewport: { width: 360, height: 740 },
    colorScheme: test.info().project.name === 'dark' ? 'dark' : 'light',
  });
  const page = await context.newPage();
  await page.goto('/portal/register');
  await page.getByLabel('Your full name').fill('Maria Dela Cruz');
  await page
    .getByLabel('Email')
    .fill(`maria.${test.info().project.name}.${Date.now()}@family.test`);
  await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(page.getByText('Link a patient to start booking')).toBeVisible();
  return page;
}

test('a daughter is linked to her father, then the link is revoked', async ({ page, browser }) => {
  const lastName = `Linked${test.info().project.name}${Date.now()}`;
  await page.goto('/');
  const patient = await apiPost<{ id: string; mrn: string }>(page, '/api/patients', {
    firstName: 'Juan',
    middleName: 'Santos',
    lastName,
    birthDate: '1958-03-21',
    sex: 'male',
    address: '12 Mabini St., Marikina City',
    contactNumber: '0917 555 0199',
    emergencyContactName: 'Maria Dela Cruz',
    emergencyContactRelation: 'Daughter',
    emergencyContactNumber: '0918 555 0123',
  });

  // ---- The daughter asks to be linked ----
  const daughter = await portalUser(browser);
  await daughter.getByRole('link', { name: 'Link a patient' }).click();
  await daughter.getByRole('button', { name: 'Send request' }).click();
  await expect(daughter.getByText("Patient's full name is required.")).toBeVisible();
  await expectNoSeriousA11yViolations(daughter);
  await choose(daughter, "You are the patient's", 'Son or daughter');
  await daughter.getByLabel("Patient's full name").fill(`Juan Santos ${lastName}`);
  await daughter.getByLabel("Patient's date of birth").fill('1958-03-21');
  await daughter.getByLabel('Anything else').fill('He is my father.');
  await daughter.getByRole('button', { name: 'Send request' }).click();
  const requests = daughter.getByRole('list', { name: 'Link requests' });
  await expect(requests).toContainText(`Juan Santos ${lastName}`);
  await expect(requests).toContainText('Pending verification');

  // ---- The front desk verifies it ----
  await page.goto('/link-requests');
  await expect(page.getByRole('heading', { level: 1, name: 'Portal link requests' })).toBeVisible();
  const request = page.getByRole('listitem').filter({ hasText: `Juan Santos ${lastName}` });
  await expect(request).toContainText('the son or daughter of');
  await expectNoSeriousA11yViolations(page);
  await request.getByRole('button', { name: 'Review' }).click();

  const dialog = page.getByRole('dialog');
  // The search starts from the requested name and date of birth.
  await dialog.getByRole('radio', { name: new RegExp(`${lastName}, Juan Santos`) }).check();
  await expect(dialog.getByRole('checkbox', { name: /See records/ })).not.toBeChecked();
  await dialog.getByRole('checkbox', { name: /See records/ }).click();
  await expectNoSeriousA11yViolations(page);
  await dialog.getByRole('button', { name: 'Verify link' }).click();
  await expect(page.getByText('Link verified')).toBeVisible();

  // ---- The daughter now sees her father ----
  await daughter.reload();
  const linked = daughter.getByRole('list', { name: 'Linked patients' });
  await expect(linked).toContainText(`${lastName}, Juan Santos`);
  await expect(linked).toContainText(patient.mrn);
  await expect(linked).toContainText("You're their son or daughter");
  await expect(linked).toContainText('Can book visits · can see records');
  await expectNoSeriousA11yViolations(daughter);

  // ---- The front desk revokes it from the patient's record ----
  await page.goto(`/patients/${patient.id}`);
  const access = page.getByRole('region', { name: 'Portal access' });
  await expect(access).toContainText('Maria Dela Cruz');
  await access.getByRole('button', { name: 'Actions for Maria Dela Cruz' }).click();
  await page.getByRole('menuitem', { name: 'Revoke link' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Revoke link' }).click();
  await expect(page.getByRole('dialog').getByText('Say why the link is ending.')).toBeVisible();
  await page.getByRole('dialog').getByLabel('Reason').fill('Patient asked to remove access');
  await page.getByRole('dialog').getByRole('button', { name: 'Revoke link' }).click();
  await expect(access).toContainText('No portal accounts are linked');
  await expect(access).toContainText('Patient asked to remove access');

  await daughter.reload();
  await expect(daughter.getByRole('list', { name: 'Linked patients' })).toBeHidden();
  await expect(daughter.getByRole('list', { name: 'Link requests' })).toContainText('Revoked');
  await daughter.context().close();
});
