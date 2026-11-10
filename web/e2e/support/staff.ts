import { expect, request as playwrightRequest, type Browser, type Page } from '@playwright/test';

import { MAILPIT_URL } from './accounts';

export const STAFF_PASSWORD = 'Staff-E2E-Password-2026';

/** POSTs to the API as the page's signed-in user, with the antiforgery header the app would send. */
export async function apiPost<T = { id: string }>(
  page: Page,
  path: string,
  data: object,
): Promise<T> {
  const xsrf = (await page.context().cookies()).find((c) => c.name === 'XSRF-TOKEN')?.value ?? '';
  const response = await page.request.post(path, { data, headers: { 'X-XSRF-TOKEN': xsrf } });
  expect(response.ok(), await response.text()).toBe(true);
  return (await response.json()) as T;
}

/** Today in hospital time, plus `offset` days, as YYYY-MM-DD. */
export function hospitalDay(offset = 0): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Manila' }).format(
    new Date(Date.now() + offset * 86_400_000),
  );
}

/** Monday of the week containing `date`. */
export function mondayOf(date: string): string {
  const day = new Date(`${date}T00:00:00Z`);
  day.setUTCDate(day.getUTCDate() - ((day.getUTCDay() + 6) % 7));
  return day.toISOString().slice(0, 10);
}

export interface Staff {
  employeeId: string;
  email: string;
  firstName: string;
  lastName: string;
}

/**
 * A nurse with a sign-in account holding `role`, made the way HR does it: an employee record, an invite
 * (read from Mailpit) and an accepted invite. `admin` must be a page signed in as the administrator.
 */
export async function createStaff(
  admin: Page,
  firstName: string,
  lastName: string,
  role: string,
): Promise<Staff> {
  const tag = `${Date.now().toString(36)}${Math.floor(Math.random() * 1e4)}`;
  const department = await apiPost(admin, '/api/workforce/departments', { name: `Nursing ${tag}` });
  const position = await apiPost(admin, '/api/workforce/positions', { name: `Nurse ${tag}` });
  const employee = await apiPost(admin, '/api/workforce/employees', {
    firstName,
    lastName,
    sex: 'female',
    birthDate: '1995-04-12',
    departmentId: department.id,
    positionId: position.id,
    professionalType: 'nurse',
    licenseNumber: '0456789',
    employmentType: 'full_time',
    hiredOn: '2020-06-01',
  });

  const roles = (await (await admin.request.get('/api/access/roles')).json()) as {
    id: string;
    name: string;
  }[];
  const roleId = roles.find((r) => r.name === role)?.id;
  expect(roleId, `role ${role}`).toBeTruthy();
  const email = `${firstName}.${tag}@hilom.test`.toLowerCase();
  await apiPost(admin, `/api/workforce/employees/${employee.id}/invites`, {
    email,
    roleIds: [roleId],
  });

  // Read the token from the invite email and accept it, as the nurse would from the link.
  let token = '';
  await expect
    .poll(async () => {
      const search = await admin.request.get(`${MAILPIT_URL}/api/v1/search`, {
        params: { query: `to:"${email}"` },
      });
      const id = ((await search.json()) as { messages: { ID: string }[] }).messages[0]?.ID;
      if (!id) {
        return false;
      }
      const text = (
        (await (await admin.request.get(`${MAILPIT_URL}/api/v1/message/${id}`)).json()) as {
          Text: string;
        }
      ).Text;
      token = /\/invite#([\w-]+)/.exec(text)?.[1] ?? '';
      return token !== '';
    })
    .toBe(true);

  const anonymous = await playwrightRequest.newContext({ baseURL: new URL(admin.url()).origin });
  await anonymous.get('/api/access/antiforgery');
  const xsrf =
    (await anonymous.storageState()).cookies.find((c) => c.name === 'XSRF-TOKEN')?.value ?? '';
  const accepted = await anonymous.post('/api/workforce/invites/accept', {
    data: { token, password: STAFF_PASSWORD },
    headers: { 'X-XSRF-TOKEN': xsrf },
  });
  expect(accepted.ok(), await accepted.text()).toBe(true);
  await anonymous.dispose();

  return { employeeId: employee.id, email, firstName, lastName };
}

/** A fresh browser session signed in as the staff member. */
export async function signInAs(browser: Browser, staff: Staff): Promise<Page> {
  const context = await browser.newContext({ storageState: { cookies: [], origins: [] } });
  const page = await context.newPage();
  await page.goto('/login');
  await page.getByLabel('Email').fill(staff.email);
  await page.getByLabel('Password', { exact: true }).fill(STAFF_PASSWORD);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('navigation', { name: 'Main' })).toBeVisible();
  return page;
}
