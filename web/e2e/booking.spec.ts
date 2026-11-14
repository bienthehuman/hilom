import { expect, test, type Browser, type Page } from '@playwright/test';

import { registerPortalAccount } from './support/accounts';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { apiPost } from './support/staff';

/** A Monday at least a week ahead (hospital time is close enough to local here). */
function nextMonday(): string {
  const day = new Date();
  day.setUTCDate(day.getUTCDate() + 7);
  while (day.getUTCDay() !== 1) {
    day.setUTCDate(day.getUTCDate() + 1);
  }
  return day.toISOString().slice(0, 10);
}

/** A cardiologist with two 30-minute slots next Monday, 09:00 and 09:30, and a patient. */
async function setUp(page: Page, tag: string) {
  await page.goto('/');
  const specialization = await apiPost(page, '/api/workforce/specializations', {
    name: `Cardiology ${tag}`,
  });
  const department = await apiPost(page, '/api/workforce/departments', { name: `Medicine ${tag}` });
  const position = await apiPost(page, '/api/workforce/positions', { name: `Consultant ${tag}` });
  const physician = await apiPost(page, '/api/workforce/employees', {
    firstName: 'Jose',
    lastName: `Santos${tag}`,
    sex: 'male',
    birthDate: '1980-02-14',
    departmentId: department.id,
    positionId: position.id,
    professionalType: 'physician',
    licenseNumber: '0123456',
    employmentType: 'full_time',
    hiredOn: '2015-07-01',
    physician: {
      specializationId: specialization.id,
      acceptsAppointments: true,
      consultationMinutes: 30,
    },
  });
  const monday = nextMonday();
  await apiPost<{ created: number }>(page, '/api/scheduling/slots/publish', {
    physicianId: physician.id,
    from: monday,
    to: monday,
    days: ['monday'],
    startTime: '09:00',
    endTime: '10:00',
  });
  const patient = await apiPost(page, '/api/patients', {
    firstName: 'Juan',
    lastName: `DelaCruz${tag}`,
    birthDate: '1958-03-21',
    sex: 'male',
    address: '12 Mabini St., Marikina City',
    contactNumber: '0917 555 0199',
    emergencyContactName: 'Maria Dela Cruz',
    emergencyContactRelation: 'Daughter',
    emergencyContactNumber: '0918 555 0123',
  });
  return { specialization: `Cardiology ${tag}`, doctor: `Dr. Jose Santos${tag}`, patient };
}

/** Picks from a select by its label. */
async function choose(page: Page, label: string, option: string): Promise<void> {
  await page.getByRole('combobox', { name: label }).click();
  await page.getByRole('option', { name: option, exact: true }).click();
}

/** A daughter's portal account on a phone, linked to the patient by the front desk with booking allowed. */
async function linkedDaughter(browser: Browser, admin: Page, patientId: string): Promise<Page> {
  const context = await browser.newContext({
    storageState: { cookies: [], origins: [] },
    viewport: { width: 360, height: 740 },
    colorScheme: test.info().project.name === 'dark' ? 'dark' : 'light',
  });
  const daughter = await context.newPage();
  const email = `maria.${test.info().project.name}.${Date.now()}@family.test`;
  await registerPortalAccount(daughter, {
    name: 'Maria Dela Cruz',
    email,
    password: 'Portal-E2E-Password-2026',
  });
  await apiPost(daughter, '/api/patients/links', {
    patientName: 'Juan Dela Cruz',
    patientBirthDate: '1958-03-21',
    relationship: 'child',
  });

  const queue = (await (
    await admin.request.get('/api/patients/links?status=pending&pageSize=100')
  ).json()) as {
    items: { id: string; version: number; requester: { email: string } }[];
  };
  const link = queue.items.find((l) => l.requester.email === email)!;
  await apiPost(admin, `/api/patients/links/${link.id}/verify`, {
    patientId,
    relationship: 'child',
    canBook: true,
    canViewRecords: false,
    version: link.version,
  });
  return daughter;
}

test('front desk books from the patient record, and a daughter books from the portal', async ({
  page,
  browser,
}) => {
  const tag = `${test.info().project.name}${Date.now()}${Math.floor(Math.random() * 1e4)}`;
  const { specialization, doctor, patient } = await setUp(page, tag);

  // ---- Front desk, from the patient's record ----
  await page.goto(`/patients/${patient.id}`);
  // The record's button, not the sidebar link of the same name.
  await page.locator('#main-content').getByRole('link', { name: 'Book appointment' }).click();
  await expect(
    page.getByRole('heading', { level: 2, name: new RegExp(`DelaCruz${tag}, Juan`) }),
  ).toBeVisible();
  await choose(page, 'Specialization', specialization);
  // The first day with free times is chosen for you: next Monday, with both slots.
  const times = page.getByRole('radiogroup', { name: 'Free times' });
  await expect(times.getByRole('radio')).toHaveCount(2);
  await expectNoSeriousA11yViolations(page);

  await page.getByRole('button', { name: 'Book appointment' }).click();
  await expect(page.getByText('Choose a free time.')).toBeVisible();
  await times.getByRole('radio', { name: /09:00–09:30$/ }).click();
  await expect(times.getByRole('radio', { name: /09:00–09:30$/ })).toHaveAttribute(
    'aria-checked',
    'true',
  );
  await page.locator('#book-reason').fill('Chest pain when climbing stairs');
  await page.getByRole('button', { name: 'Book appointment' }).click();
  await expect(page.getByRole('heading', { name: 'Appointment requested' })).toBeVisible();
  await expect(
    page.locator('#main-content').getByText(new RegExp(`${doctor} · .*09:00–09:30`)),
  ).toBeVisible();
  await expectNoSeriousA11yViolations(page);

  // ---- The daughter, in the portal: only the 09:30 slot is left ----
  const daughter = await linkedDaughter(browser, page, patient.id);
  await daughter.goto('/portal/appointments');
  // The visit the front desk just booked for her father is already there.
  const upcoming = daughter.getByRole('list', { name: /^Upcoming for / });
  await expect(upcoming).toContainText('09:00–09:30');
  await daughter.getByRole('link', { name: 'Book another visit' }).click();
  await expect(daughter.getByRole('heading', { level: 1, name: 'Book a visit' })).toBeVisible();
  // Her father is the only patient she can book for, so he's chosen already.
  await expect(
    daughter.getByRole('radio', { name: new RegExp(`DelaCruz${tag}, Juan`) }),
  ).toBeChecked();
  await choose(daughter, 'Specialization', specialization);
  const free = daughter.getByRole('radiogroup', { name: 'Free times' });
  await expect(free.getByRole('radio')).toHaveCount(1);
  await free.getByRole('radio', { name: /09:30–10:00$/ }).click();
  await daughter.getByLabel('Why do they want to see the doctor?').fill('Follow-up for his heart');
  await expectNoSeriousA11yViolations(daughter);
  await daughter.getByRole('button', { name: 'Request appointment' }).click();

  await expect(upcoming).toContainText('09:30–10:00');
  await expect(upcoming).toContainText(doctor);
  await expect(upcoming).toContainText('Waiting for the doctor to confirm.');
  await expectNoSeriousA11yViolations(daughter);

  // She can cancel what she booked: the link allows booking.
  const mine = upcoming.getByRole('listitem').filter({ hasText: '09:30–10:00' });
  await mine.getByRole('button', { name: 'Cancel this appointment' }).click();
  await daughter.getByLabel('Why are you cancelling?').fill('Papa feels better now');
  await daughter.getByRole('button', { name: 'Cancel appointment' }).click();
  const past = daughter.getByRole('list', { name: /^Past for / });
  await expect(past).toContainText('Papa feels better now');
  await expect(upcoming).not.toContainText('09:30–10:00');
  await daughter.context().close();
});
