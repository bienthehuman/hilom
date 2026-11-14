import { expect, test, type APIRequestContext, type Browser, type Page } from '@playwright/test';

import { expectNoSeriousA11yViolations } from '../support/accessibility';
import { MAILPIT_URL, replaceTemporaryPassword } from '../support/accounts';
import { apiPost, createStaff, hospitalDay, mondayOf, signInAs } from '../support/staff';

/**
 * Phase 1 demo script (docs/BUILD_PLAN.md, "Phase 1: Workforce + Facility"), run end to end through the UI
 * as each role:
 *
 * 1. HR creates a nurse and sends an invite.
 * 2. The nurse sets a password from the invite link.
 * 3. The Chief Nurse assigns them to Ward 3 and puts them on the roster.
 * 4. The nurse sees their own schedule.
 * 5. A head nurse sees only their own ward's roster.
 *
 * Only the hospital's setup (wards, shifts, lookups, the HR and Chief Nurse accounts) is made through the API.
 */

const PASSWORD = 'Milestone-Pass-2026';

const longDay = (date: string) =>
  new Intl.DateTimeFormat('en-PH', {
    weekday: 'long',
    month: 'long',
    day: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(`${date}T00:00:00Z`));

/** A user with a role, made by the administrator like on the Users screen (no employee record needed). */
async function createUser(admin: Page, name: string, role: string): Promise<string> {
  const roles = (await (await admin.request.get('/api/access/roles')).json()) as {
    id: string;
    name: string;
  }[];
  const email = `${name.toLowerCase().replace(/\W+/g, '.')}.${Date.now().toString(36)}@hilom.test`;
  await apiPost(admin, '/api/access/users', {
    email,
    displayName: name,
    password: PASSWORD,
    roleIds: [roles.find((r) => r.name === role)!.id],
  });
  return email;
}

async function signIn(browser: Browser, email: string): Promise<Page> {
  const context = await browser.newContext({ storageState: { cookies: [], origins: [] } });
  const page = await context.newPage();
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
  await page.getByRole('button', { name: 'Sign in' }).click();
  // Made by the administrator, so the password is temporary. Each user signs in once in this script.
  await replaceTemporaryPassword(page, PASSWORD, `${PASSWORD}-own`);
  await expect(page.getByRole('navigation', { name: 'Main' })).toBeVisible();
  return page;
}

async function inviteLink(request: APIRequestContext, email: string): Promise<string> {
  let link = '';
  await expect
    .poll(async () => {
      const search = await request.get(`${MAILPIT_URL}/api/v1/search`, {
        params: { query: `to:"${email}"` },
      });
      const id = ((await search.json()) as { messages: { ID: string }[] }).messages[0]?.ID;
      if (!id) {
        return false;
      }
      const text = (
        (await (await request.get(`${MAILPIT_URL}/api/v1/message/${id}`)).json()) as {
          Text: string;
        }
      ).Text;
      link = /https?:\/\/\S+\/invite#[\w-]+/.exec(text)?.[0] ?? '';
      return link !== '';
    })
    .toBe(true);
  return link;
}

async function choose(page: Page, label: string, option: string | RegExp): Promise<void> {
  await page.getByRole('combobox', { name: new RegExp(`^${label}\\b`) }).click();
  await page.getByRole('option', { name: option }).click();
  await expect(page.getByRole('listbox')).toHaveCount(0);
}

test('Phase 1: HR onboards a nurse, the Chief Nurse assigns and rosters them', async ({
  page,
  browser,
  request,
}) => {
  test.setTimeout(150_000);
  const tag = `${test.info().project.name[0]}${Date.now().toString(36)}`.toUpperCase();
  const ward3 = `W3-${tag}`;
  const ward5 = `W5-${tag}`;
  const shiftCode = `AM${tag}`.slice(0, 10);
  const lastName = `Reyes${tag}`;
  const nurseEmail = `ana.${tag.toLowerCase()}@hilom.test`;
  const dutyDay = hospitalDay(1);

  // ---- Hospital setup (administrator, through the API) ----
  await page.goto('/');
  const w3 = await apiPost(page, '/api/facility/wards', {
    code: ward3,
    name: 'Medical',
    wardType: 'general',
  });
  const w5 = await apiPost(page, '/api/facility/wards', {
    code: ward5,
    name: 'Surgical',
    wardType: 'surgical',
  });
  await apiPost(page, '/api/workforce/shifts', {
    code: shiftCode,
    name: 'Morning',
    startsAt: '06:00:00',
    endsAt: '14:00:00',
    color: 'am',
  });
  await apiPost(page, '/api/workforce/departments', { name: `Nursing Service ${tag}` });
  await apiPost(page, '/api/workforce/positions', { name: `Staff Nurse ${tag}` });
  const hrEmail = await createUser(page, `Helen HR ${tag}`, 'HR Officer');
  const chiefEmail = await createUser(page, `Carmen Chief ${tag}`, 'Chief Nurse');

  // ---- 1. HR creates the nurse and sends an invite ----
  const hr = await signIn(browser, hrEmail);
  await hr.goto('/workforce/employees/new');
  await hr.getByLabel('First name').fill('Ana');
  await hr.getByLabel('Last name').fill(lastName);
  await choose(hr, 'Sex', 'Female');
  await hr.getByLabel('Date of birth').fill('1995-04-12');
  await choose(hr, 'Department', `Nursing Service ${tag}`);
  await choose(hr, 'Position', `Staff Nurse ${tag}`);
  await choose(hr, 'Professional type', 'Nurse');
  await hr.getByLabel('License number').fill('0456789');
  await hr.getByLabel('Hired on').fill('2024-06-01');
  await hr.getByRole('button', { name: 'Add employee' }).click();
  await expect(hr.getByRole('heading', { level: 1, name: `Ana ${lastName}` })).toBeVisible();
  const profileUrl = hr.url();
  // The "Employee added" toast pauses while hovered, and the pointer is still where "Add employee" was.
  await hr.mouse.move(0, 0);
  await expect(hr.locator('[data-sonner-toast]')).toHaveCount(0);

  const access = hr.getByRole('region', { name: 'Sign-in access' });
  await access.getByRole('button', { name: 'Send invite' }).click();
  const inviteDialog = hr.getByRole('dialog');
  await inviteDialog.getByLabel('Email').fill(nurseEmail);
  await inviteDialog.getByRole('checkbox', { name: /^Staff Nurse/ }).click();
  await inviteDialog.getByRole('button', { name: 'Send invite' }).click();
  await expect(access).toContainText('Waiting');
  await hr.context().close();

  // ---- 2. The nurse sets a password from the email link ----
  const nurseContext = await browser.newContext({ storageState: { cookies: [], origins: [] } });
  const nurse = await nurseContext.newPage();
  await nurse.goto(await inviteLink(request, nurseEmail));
  await expect(nurse.getByRole('heading', { level: 1, name: 'Welcome, Ana' })).toBeVisible();
  await nurse.getByLabel('New password').fill(PASSWORD);
  await nurse.getByLabel('Confirm password').fill(PASSWORD);
  await nurse.getByRole('button', { name: 'Set password' }).click();
  await expect(nurse.getByText('Your account is ready')).toBeVisible();
  await nurse.getByLabel('Password', { exact: true }).fill(PASSWORD);
  await nurse.getByRole('button', { name: 'Sign in' }).click();
  await expect(nurse.getByRole('navigation', { name: 'Main' })).toBeVisible();

  // ---- 3. The Chief Nurse assigns her to Ward 3 and rosters her ----
  const chief = await signIn(browser, chiefEmail);
  await chief.goto(profileUrl);
  const wardCard = chief.getByRole('region', { name: 'Ward assignment' });
  await wardCard.getByRole('button', { name: 'Assign to ward' }).click();
  const assignDialog = chief.getByRole('dialog');
  await choose(chief, 'Ward', `${ward3} · Medical`);
  await assignDialog.getByRole('button', { name: 'Assign' }).click();
  await expect(wardCard).toContainText(`On ${ward3} · Medical`);

  await chief.goto(`/workforce/roster?ward=${w3.id}&week=${mondayOf(dutyDay)}`);
  const cell = `${lastName}, Ana, ${longDay(dutyDay)}`;
  await chief.getByRole('button', { name: `${cell}: off. Add a shift` }).click();
  await chief.getByRole('menuitem', { name: new RegExp(`^${shiftCode}`) }).click();
  await expect(chief.getByRole('button', { name: `${cell}: ${shiftCode}. Change` })).toBeVisible();
  await expectNoSeriousA11yViolations(chief);
  await chief.context().close();

  // ---- 4. The nurse sees her own schedule ----
  await nurse.goto(`/workforce/my-schedule?week=${mondayOf(dutyDay)}`);
  const day = nurse.getByRole('listitem').filter({ hasText: longDay(dutyDay) });
  await expect(day).toContainText(shiftCode);
  await expect(day).toContainText(`${ward3} · Medical`);
  await expectNoSeriousA11yViolations(nurse);
  await nurseContext.close();

  // ---- 5. A head nurse sees only her own ward's roster ----
  const head = await createStaff(page, 'Hana', `Head${tag}`, 'Head Nurse');
  await apiPost(page, '/api/workforce/ward-assignments', {
    employeeId: head.employeeId,
    wardId: w5.id,
    startsOn: hospitalDay(-1),
  });
  const headPage = await signInAs(browser, head);
  await headPage.goto(`/workforce/roster?ward=${w3.id}`);
  await expect(headPage.getByRole('combobox', { name: /^Ward/ })).toContainText(ward5);
  await headPage.getByRole('combobox', { name: /^Ward/ }).click();
  await expect(headPage.getByRole('option')).toHaveCount(1);
  await headPage.keyboard.press('Escape');
  await expect(headPage.getByRole('rowheader', { name: `${lastName}, Ana` })).toHaveCount(0);
  const outside = await headPage.request.get(`/api/workforce/roster/wards/${w3.id}`);
  expect(outside.status()).toBe(404);
  await headPage.context().close();
});
