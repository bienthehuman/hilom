import { expect, test, type Browser, type Page } from '@playwright/test';

import { expectNoSeriousA11yViolations } from '../support/accessibility';
import { apiPost, createStaff, hospitalDay, mondayOf, signInAs } from '../support/staff';

/**
 * Phase 2 demo script (docs/BUILD_PLAN.md, "Phase 2: Patients + Scheduling"), run end to end through the UI
 * as each person:
 *
 * 1. Dr. Santos publishes his availability.
 * 2. A daughter registers on the portal and asks to be linked to her father.
 * 3. The front desk verifies the link (representative, may book, can't see records).
 * 4. She books a slot with Dr. Santos for her father.
 * 5. Dr. Santos opens the father's record and confirms the request.
 * 6. The front desk sees it on the day list and opens the record.
 * 7. Every view of the father's record shows up in the audit log.
 *
 * Only the hospital's setup (a specialization, the father's registration, the front desk account, and
 * Dr. Santos's employee record and invite) is made through the API.
 */

const PASSWORD = 'Milestone-Pass-2026';

/** A staff user with a role, made by the administrator like on the Users screen. */
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
  await expect(page.getByRole('navigation', { name: 'Main' })).toBeVisible();
  return page;
}

async function choose(page: Page, label: string | RegExp, option: string | RegExp): Promise<void> {
  await page.getByRole('combobox', { name: label }).click();
  await page.getByRole('option', { name: option }).click();
  await expect(page.getByRole('listbox')).toHaveCount(0);
}

const sidebarLink = (page: Page, name: string) =>
  page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name, exact: true });

test('Phase 2: a daughter is linked to her father, books Dr. Santos, and he confirms', async ({
  page,
  browser,
}) => {
  test.setTimeout(180_000);
  const tag =
    `${test.info().project.name[0]}${Date.now().toString(36)}${Math.floor(Math.random() * 1e3)}`.toUpperCase();
  const father = `DelaCruz${tag}`;
  const doctorName = `Dr. Jose Santos${tag}`;
  // Next week's Monday, the first day the doctor publishes (he moves the availability page one week on).
  const monday = mondayOf(hospitalDay(7));

  // ---- Hospital setup (administrator, through the API) ----
  await page.goto('/');
  const cardiology = await apiPost(page, '/api/workforce/specializations', {
    name: `Cardiology ${tag}`,
  });
  const patient = await apiPost<{ id: string; mrn: string }>(page, '/api/patients', {
    firstName: 'Juan',
    middleName: 'Santos',
    lastName: father,
    birthDate: '1958-03-21',
    sex: 'male',
    address: '12 Mabini St., Marikina City',
    contactNumber: '0917 555 0199',
    emergencyContactName: 'Maria Dela Cruz',
    emergencyContactRelation: 'Daughter',
    emergencyContactNumber: '0918 555 0123',
  });
  const frontDeskName = `Rina Desk ${tag}`;
  const frontDeskEmail = await createUser(page, frontDeskName, 'Front Desk');
  const santos = await createStaff(page, 'Jose', `Santos${tag}`, 'Physician', {
    professionalType: 'physician',
    licenseNumber: '0123456',
    physician: {
      specializationId: cardiology.id,
      acceptsAppointments: true,
      consultationMinutes: 30,
    },
  });

  // ---- 1. Dr. Santos publishes next week's mornings ----
  const doctor = await signInAs(browser, santos);
  await sidebarLink(doctor, 'Availability').click();
  await expect(doctor.getByText(`${doctorName} · Cardiology ${tag}`)).toBeVisible();
  await doctor.getByRole('button', { name: 'Next week' }).click();
  await doctor.getByRole('button', { name: 'Publish slots' }).click();
  const publish = doctor.getByRole('dialog');
  await publish.locator('#publish-start').fill('09:00');
  await publish.locator('#publish-end').fill('10:00');
  await publish.getByRole('button', { name: 'Publish', exact: true }).click();
  await expect(doctor.getByText('Published 10 slots')).toBeVisible();

  // ---- 2. The daughter registers and asks to be linked to her father ----
  const phone = await browser.newContext({
    storageState: { cookies: [], origins: [] },
    viewport: { width: 360, height: 740 },
    colorScheme: test.info().project.name === 'dark' ? 'dark' : 'light',
  });
  const daughter = await phone.newPage();
  await daughter.goto('/portal/register');
  await daughter.getByLabel('Your full name').fill('Maria Dela Cruz');
  await daughter.getByLabel('Email').fill(`maria.${tag.toLowerCase()}@family.test`);
  await daughter.getByLabel('Password', { exact: true }).fill(PASSWORD);
  await daughter.getByRole('button', { name: 'Create account' }).click();
  await expect(daughter.getByText('Link a patient to start booking')).toBeVisible();
  await daughter.getByRole('link', { name: 'Link a patient' }).click();
  await choose(daughter, /^You are the patient's/, 'Son or daughter');
  await daughter.getByLabel("Patient's full name").fill(`Juan Santos ${father}`);
  await daughter.getByLabel("Patient's date of birth").fill('1958-03-21');
  await daughter.getByLabel('Medical record number').fill(patient.mrn);
  await daughter.getByRole('button', { name: 'Send request' }).click();
  await expect(daughter.getByRole('list', { name: 'Link requests' })).toContainText(
    'Pending verification',
  );

  // ---- 3. The front desk checks her ID and verifies the link ----
  const desk = await signIn(browser, frontDeskEmail);
  await sidebarLink(desk, 'Link requests').click();
  const request = desk.getByRole('listitem').filter({ hasText: `Juan Santos ${father}` });
  await request.getByRole('button', { name: 'Review' }).click();
  const review = desk.getByRole('dialog');
  // The request carried the MRN from his card, so the search finds exactly him.
  await review.getByRole('radio', { name: new RegExp(`${father}, Juan Santos`) }).check();
  await choose(desk, /^Relationship, as confirmed/, 'Representative');
  await expect(review.getByRole('checkbox', { name: /Book and cancel visits/ })).toBeChecked();
  await expect(review.getByRole('checkbox', { name: /See records/ })).not.toBeChecked();
  await review.getByLabel('Note').fill('Checked her driver’s license');
  await review.getByRole('button', { name: 'Verify link' }).click();
  await expect(desk.getByText('Link verified')).toBeVisible();

  // ---- 4. She books Dr. Santos for her father ----
  // She comes back later; the link is now active.
  await daughter.reload();
  const linked = daughter.getByRole('list', { name: 'Linked patients' });
  await expect(linked).toContainText(`${father}, Juan Santos`);
  await expect(linked).toContainText("Can book visits · can't see records");
  await daughter
    .getByRole('navigation', { name: 'Portal' })
    .getByRole('link', { name: 'Appointments' })
    .click();
  await daughter.getByRole('link', { name: 'Book a visit' }).click();
  await choose(daughter, 'Doctor', doctorName);
  const times = daughter.getByRole('radiogroup', { name: 'Free times' });
  await times.getByRole('radio', { name: /09:00–09:30$/ }).click();
  await daughter
    .getByLabel('Why do they want to see the doctor?')
    .fill('Chest pain when climbing stairs');
  await expectNoSeriousA11yViolations(daughter);
  await daughter.getByRole('button', { name: 'Request appointment' }).click();
  const upcoming = daughter.getByRole('list', { name: /^Upcoming for / });
  await expect(upcoming).toContainText('Waiting for the doctor to confirm.');

  // ---- 5. Dr. Santos reads the chart, then confirms ----
  await sidebarLink(doctor, 'Requests').click();
  const inbox = doctor.getByRole('list', { name: 'Requests waiting' });
  await expect(inbox).toContainText('"Chest pain when climbing stairs"');
  await expect(inbox).toContainText('Booked by Maria Dela Cruz');
  await inbox.getByRole('link', { name: `${father}, Juan Santos` }).click();
  await expect(
    doctor.getByRole('heading', { level: 1, name: `${father}, Juan Santos` }),
  ).toBeVisible();
  await doctor.goBack();
  await doctor
    .getByRole('list', { name: 'Requests waiting' })
    .getByRole('button', { name: new RegExp(`^Confirm ${father}`) })
    .click();
  await expect(doctor.getByText('No requests waiting')).toBeVisible();
  await expectNoSeriousA11yViolations(doctor);

  // ---- 6. The front desk sees it on the day list ----
  await sidebarLink(desk, 'Appointments').click();
  await desk.getByLabel('Day', { exact: true }).fill(monday);
  await choose(desk, 'Doctor', doctorName);
  const visit = desk
    .getByRole('list', { name: /^Appointments, / })
    .getByRole('listitem')
    .filter({ hasText: father });
  await expect(visit).toContainText('09:00–09:30');
  await expect(visit).toContainText('Confirmed');
  await expectNoSeriousA11yViolations(desk);
  await visit.getByRole('link', { name: `${father}, Juan Santos` }).click();
  await expect(
    desk.getByRole('heading', { level: 1, name: `${father}, Juan Santos` }),
  ).toBeVisible();

  // The daughter sees the confirmation.
  await daughter.reload();
  await expect(daughter.getByRole('list', { name: /^Upcoming for / })).toContainText('Confirmed');

  // ---- 7. Every view of the father's record is in the audit log ----
  for (const viewer of [doctorName.replace('Dr. ', ''), frontDeskName]) {
    await page.goto('/admin/audit');
    await page.getByLabel('Who').fill(viewer);
    await choose(page, 'Event', 'Patient record opened');
    // Record views are their own kind of entry, next to named events and data changes.
    await choose(page, 'Show', 'Everything');
    const rows = page.getByRole('table', { name: 'Audit log' }).getByRole('row');
    // A header row and one entry: opening the record twice in 15 minutes is one view.
    await expect(rows).toHaveCount(2);
    await rows
      .nth(1)
      .getByRole('button', { name: /^Details of / })
      .click();
    const details = page.getByRole('dialog');
    await expect(details).toContainText(viewer);
    await expect(details).toContainText(patient.id);
    await page.keyboard.press('Escape');
  }

  await doctor.context().close();
  await desk.context().close();
  await phone.close();
});
