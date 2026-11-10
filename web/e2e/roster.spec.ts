import { expect, test, type Page } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';

const unique = () =>
  `${test.info().project.name[0]}${Date.now().toString(36)}${Math.floor(Math.random() * 100)}`.toUpperCase();

async function apiPost(page: Page, path: string, data: object): Promise<{ id: string }> {
  const xsrf = (await page.context().cookies()).find((c) => c.name === 'XSRF-TOKEN')?.value ?? '';
  const response = await page.request.post(path, { data, headers: { 'X-XSRF-TOKEN': xsrf } });
  expect(response.status(), await response.text()).toBe(201);
  return (await response.json()) as { id: string };
}

/** Monday of this week and the week's days, in Manila. */
function thisWeek(): { monday: string; days: string[] } {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Manila' }).format(new Date());
  const date = new Date(`${today}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() - ((date.getUTCDay() + 6) % 7));
  const days = Array.from({ length: 7 }, (_, i) => {
    const d = new Date(date);
    d.setUTCDate(d.getUTCDate() + i);
    return d.toISOString().slice(0, 10);
  });
  return { monday: days[0]!, days };
}

const longDay = (date: string) =>
  new Intl.DateTimeFormat('en-PH', {
    weekday: 'long',
    month: 'long',
    day: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(`${date}T00:00:00Z`));

test('a manager rosters a nurse for the week', async ({ page }) => {
  const id = unique();
  const { monday, days } = thisWeek();
  await page.goto('/');
  const ward = await apiPost(page, '/api/facility/wards', {
    code: `R-${id}`,
    name: 'Roster',
    wardType: 'general',
  });
  await apiPost(page, '/api/workforce/shifts', {
    code: `A${id}`.slice(0, 10),
    name: 'Morning',
    startsAt: '06:00:00',
    endsAt: '14:00:00',
    color: 'am',
  });
  await apiPost(page, '/api/workforce/shifts', {
    code: `P${id}`.slice(0, 10),
    name: 'Afternoon',
    startsAt: '14:00:00',
    endsAt: '22:00:00',
    color: 'pm',
  });
  const am = `A${id}`.slice(0, 10);
  const pm = `P${id}`.slice(0, 10);
  const department = await apiPost(page, '/api/workforce/departments', { name: `Nursing ${id}` });
  const position = await apiPost(page, '/api/workforce/positions', { name: `Nurse ${id}` });
  const nurse = await apiPost(page, '/api/workforce/employees', {
    firstName: 'Ana',
    lastName: `Reyes${id}`,
    sex: 'female',
    birthDate: '1995-04-12',
    departmentId: department.id,
    positionId: position.id,
    professionalType: 'nurse',
    licenseNumber: '0456789',
    employmentType: 'full_time',
    hiredOn: '2020-06-01',
  });
  // On the ward from Tuesday to Sunday: Monday can't be rostered.
  await apiPost(page, '/api/workforce/ward-assignments', {
    employeeId: nurse.id,
    wardId: ward.id,
    startsOn: days[1],
  });

  await page.goto(`/workforce/roster?ward=${ward.id}&week=${monday}`);
  await expect(page.getByRole('heading', { level: 1, name: 'Duty roster' })).toBeVisible();
  const who = `Reyes${id}, Ana`;
  await expect(page.getByRole('rowheader', { name: who })).toBeVisible();
  await expect(page.getByText(`${who}, ${longDay(days[0]!)}: not on this ward`)).toBeAttached();
  await expectNoSeriousA11yViolations(page);

  // Tuesday: add AM, then change it to PM.
  const tuesday = `${who}, ${longDay(days[1]!)}`;
  await page.getByRole('button', { name: `${tuesday}: off. Add a shift` }).click();
  await expectNoSeriousA11yViolations(page);
  await page.getByRole('menuitem', { name: new RegExp(`^${am}`) }).click();
  await expect(page.getByRole('button', { name: `${tuesday}: ${am}. Change` })).toBeVisible();

  await page.getByRole('button', { name: `${tuesday}: ${am}. Change` }).click();
  await page.getByRole('menuitem', { name: new RegExp(`^${pm}`) }).click();
  await expect(page.getByRole('button', { name: `${tuesday}: ${pm}. Change` })).toBeVisible();

  // Wednesday too, then take Tuesday off again.
  const wednesday = `${who}, ${longDay(days[2]!)}`;
  await page.getByRole('button', { name: `${wednesday}: off. Add a shift` }).click();
  await page.getByRole('menuitem', { name: new RegExp(`^${am}`) }).click();
  await expect(page.getByRole('button', { name: `${wednesday}: ${am}. Change` })).toBeVisible();

  await page.getByRole('button', { name: `${tuesday}: ${pm}. Change` }).click();
  await page.getByRole('menuitem', { name: 'Remove from roster' }).click();
  await expect(page.getByRole('button', { name: `${tuesday}: off. Add a shift` })).toBeVisible();

  // The week is kept in the URL.
  await page.getByRole('button', { name: 'Next week' }).click();
  await expect(page).toHaveURL(new RegExp(`week=`));
  await expect(page.getByRole('button', { name: `${wednesday}: ${am}. Change` })).toHaveCount(0);
});

test('my schedule explains when an account has no employee record', async ({ page }) => {
  await page.goto('/workforce/my-schedule');
  await expect(page.getByRole('heading', { level: 1, name: 'My schedule' })).toBeVisible();
  await expect(page.getByText('No schedule for this account')).toBeVisible();
  await expectNoSeriousA11yViolations(page);
});
