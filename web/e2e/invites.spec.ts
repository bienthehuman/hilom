import { expect, test, type APIRequestContext, type Page } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { MAILPIT_URL, passwordField } from './support/accounts';

const PASSWORD = 'Welcome-To-Hilom-2026';

const unique = (prefix: string) =>
  `${prefix}-${test.info().project.name}-${Date.now()}-${Math.floor(Math.random() * 1e4)}`;

/** POSTs to the API as the signed-in admin, with the antiforgery header the app would send. */
async function apiPost(page: Page, path: string, data: object): Promise<{ id: string }> {
  const xsrf = (await page.context().cookies()).find((c) => c.name === 'XSRF-TOKEN')?.value ?? '';
  const response = await page.request.post(path, { data, headers: { 'X-XSRF-TOKEN': xsrf } });
  expect(response.status(), await response.text()).toBe(201);
  return (await response.json()) as { id: string };
}

async function createNurse(page: Page, lastName: string): Promise<string> {
  await page.goto('/');
  const department = await apiPost(page, '/api/workforce/departments', { name: unique('Nursing') });
  const position = await apiPost(page, '/api/workforce/positions', { name: unique('Nurse') });
  const employee = await apiPost(page, '/api/workforce/employees', {
    firstName: 'Ana',
    lastName,
    sex: 'female',
    birthDate: '1995-04-12',
    departmentId: department.id,
    positionId: position.id,
    professionalType: 'nurse',
    licenseNumber: '0456789',
    employmentType: 'full_time',
    hiredOn: '2024-06-01',
  });
  return employee.id;
}

/** The invite link from the newest email Mailpit caught for this address. */
async function inviteLinkFor(request: APIRequestContext, email: string): Promise<string> {
  let link: string | undefined;
  await expect
    .poll(async () => {
      const search = await request.get(`${MAILPIT_URL}/api/v1/search`, {
        params: { query: `to:"${email}"` },
      });
      const { messages } = (await search.json()) as { messages: { ID: string }[] };
      const newest = messages[0];
      if (!newest) {
        return false;
      }
      const message = await request.get(`${MAILPIT_URL}/api/v1/message/${newest.ID}`);
      const { Text } = (await message.json()) as { Text: string };
      link = /https?:\/\/\S+\/invite#[\w-]+/.exec(Text)?.[0];
      return link !== undefined;
    })
    .toBe(true);
  return link!;
}

test('HR invites a nurse, who sets a password and signs in', async ({ page, browser, request }) => {
  const lastName = unique('Reyes');
  const email = `${unique('ana')}@hilom.test`.toLowerCase();
  const employeeId = await createNurse(page, lastName);

  await page.goto(`/workforce/employees/${employeeId}`);
  const access = page.getByRole('region', { name: 'Sign-in access' });
  await expect(access).toContainText('No account yet');
  await access.getByRole('button', { name: 'Send invite' }).click();

  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Email').fill(email);
  await dialog.getByRole('checkbox', { name: /^Staff Nurse/ }).click();
  await expectNoSeriousA11yViolations(page);
  await dialog.getByRole('button', { name: 'Send invite' }).click();
  await expect(page.getByText('Invite sent', { exact: true })).toBeVisible();
  await expect(access).toContainText('Waiting');
  await expect(access).toContainText('Roles: Staff Nurse');

  // The nurse opens the link from the email in their own browser.
  const link = await inviteLinkFor(request, email);
  const nurseContext = await browser.newContext({ storageState: { cookies: [], origins: [] } });
  const nurse = await nurseContext.newPage();
  await nurse.goto(link);
  await expect(nurse.getByRole('heading', { level: 1, name: 'Welcome, Ana' })).toBeVisible();
  await expect(nurse).toHaveURL(/\/invite$/);
  await expect(nurse.getByText(email)).toBeVisible();
  await expectNoSeriousA11yViolations(nurse);

  await passwordField(nurse, 'New password').fill(PASSWORD);
  await nurse.getByLabel('Confirm password').fill(`${PASSWORD}x`);
  await nurse.getByRole('button', { name: 'Set password' }).click();
  await expect(nurse.getByText("The passwords don't match.")).toBeVisible();
  await nurse.getByLabel('Confirm password').fill(PASSWORD);
  await nurse.getByRole('button', { name: 'Set password' }).click();

  await expect(nurse.getByText('Your account is ready')).toBeVisible();
  await expect(nurse.getByLabel('Email')).toHaveValue(email);
  await nurse.getByLabel('Password', { exact: true }).fill(PASSWORD);
  await nurse.getByRole('button', { name: 'Sign in' }).click();
  await expect(nurse.getByRole('heading', { level: 1, name: /Ana Reyes/ })).toBeVisible();

  // The link works only once.
  await nurse.goto(link);
  await expect(nurse.getByText('This invite was already used')).toBeVisible();
  await nurseContext.close();

  await page.reload();
  await expect(access).toContainText('Has an account');
  await expect(access).toContainText('Accepted');
});
