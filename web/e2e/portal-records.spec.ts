import { expect, test, type Browser, type Page } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { apiPost, createStaff, hospitalDay, signInAs } from './support/staff';

const PASSWORD = 'Portal-E2E-Password-2026';
const NURSING_SECRET = 'NURSING-NOTE-SECRET-TEXT';
const SUMMARY = 'Pneumonia resolved on IV antibiotics. Home on oral amoxicillin for 5 days.';

/** A portal account on a phone, linked to the patient by the front desk (the admin page stands in). */
async function linkedPortalUser(
  browser: Browser,
  admin: Page,
  patientId: string,
  canViewRecords: boolean,
): Promise<Page> {
  const context = await browser.newContext({
    storageState: { cookies: [], origins: [] },
    viewport: { width: 360, height: 740 },
    colorScheme: test.info().project.name === 'dark' ? 'dark' : 'light',
  });
  const portal = await context.newPage();
  const email = `maria.${test.info().project.name}.${Date.now()}${Math.floor(Math.random() * 1e4)}@family.test`;
  await portal.goto('/portal/register');
  await portal.getByLabel('Your full name').fill('Maria Dela Cruz');
  await portal.getByLabel('Email').fill(email);
  await portal.getByLabel('Password', { exact: true }).fill(PASSWORD);
  await portal.getByRole('button', { name: 'Create account' }).click();
  await expect(portal.getByText('No linked patients yet')).toBeVisible();
  await apiPost(portal, '/api/patients/links', {
    patientName: 'Juan Dela Cruz',
    patientBirthDate: '1958-03-21',
    relationship: 'child',
  });

  const queue = (await (
    await admin.request.get('/api/patients/links?status=pending&pageSize=100')
  ).json()) as { items: { id: string; version: number; requester: { email: string } }[] };
  const link = queue.items.find((l) => l.requester.email === email)!;
  await apiPost(admin, `/api/patients/links/${link.id}/verify`, {
    patientId,
    relationship: 'child',
    canBook: true,
    canViewRecords,
    version: link.version,
  });
  return portal;
}

test('a daughter with records access reads the visit, vitals and appointments; one without sees nothing', async ({
  page,
  browser,
}) => {
  test.setTimeout(180_000);
  const tag = `${test.info().project.name}${Date.now()}${Math.floor(Math.random() * 1e4)}`;
  const wardCode =
    `R${Date.now().toString(36).slice(-5)}${Math.floor(Math.random() * 100)}`.toUpperCase();

  // ---- Admin sets up a doctor, a ward and an admitted patient ----
  await page.goto('/');
  const specialization = await apiPost(page, '/api/workforce/specializations', {
    name: `Internal Medicine ${tag}`,
  });
  const doctor = await createStaff(page, 'Jose', `Santos${tag}`, 'Physician', {
    professionalType: 'physician',
    licenseNumber: '0123456',
    physician: {
      specializationId: specialization.id,
      acceptsAppointments: true,
      consultationMinutes: 30,
    },
  });
  const ward = await apiPost(page, '/api/facility/wards', {
    code: wardCode,
    name: `Medical ${tag}`,
    wardType: 'general',
  });
  const bed = await apiPost(page, `/api/facility/wards/${ward.id}/beds`, { label: 'Bed 1' });
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
  const { encounterId } = await apiPost<{ encounterId: string }>(page, '/api/clinical/admissions', {
    patientId: patient.id,
    attendingPhysicianId: doctor.employeeId,
    bedId: bed.id,
    admittingDiagnosis: 'Community-acquired pneumonia',
    reason: 'Needs IV antibiotics and oxygen',
  });

  // ---- The physician records vitals and a nursing note, then discharges ----
  const physician = await signInAs(browser, doctor);
  await apiPost(physician, `/api/clinical/encounters/${encounterId}/vital-signs`, {
    temperatureC: 38.4,
    systolicBp: 128,
    diastolicBp: 82,
    heartRate: 150,
  });
  await apiPost(physician, `/api/clinical/encounters/${encounterId}/notes`, {
    noteType: 'nursing',
    body: NURSING_SECRET,
  });
  const stay = (await (
    await physician.request.get(`/api/clinical/encounters/${encounterId}`)
  ).json()) as { admission: { version: number } };
  await apiPost(physician, `/api/clinical/admissions/${encounterId}/discharge`, {
    disposition: 'home',
    summary: SUMMARY,
    version: stay.admission.version,
  });
  await physician.context().close();

  // ---- A daughter with records access, on a phone ----
  const daughter = await linkedPortalUser(browser, page, patient.id, true);
  await daughter.goto('/portal/patients');
  const card = daughter.getByRole('listitem').filter({ hasText: `DelaCruz${tag}` });
  const viewRecords = card.getByRole('link', { name: 'View records' });
  await expect(viewRecords).toBeVisible();
  expect((await viewRecords.boundingBox())?.height).toBeGreaterThanOrEqual(44);
  await viewRecords.click();

  await expect(daughter.getByRole('heading', { level: 1, name: 'Records' })).toBeVisible();
  await expect(daughter.getByText(`DelaCruz${tag}`)).toBeVisible();
  const visits = daughter.getByRole('list', { name: 'Visits' });
  await expect(visits).toContainText('Ward stay');
  await expect(visits).toContainText('Needs IV antibiotics and oxygen');
  await expect(visits).toContainText(SUMMARY);
  const vitals = daughter.getByRole('list', { name: 'Vital signs' });
  await expect(vitals).toContainText('38.4');
  await expect(vitals).toContainText('128/82');
  await expect(vitals).toContainText('critically high');
  await expect(daughter.getByRole('heading', { name: 'Appointments' })).toBeVisible();
  await expect(daughter.getByText('No appointments')).toBeVisible();

  // Nursing notes never reach the portal. Mobile rules: 16px type, 44px targets.
  await expect(daughter.getByText(NURSING_SECRET)).toHaveCount(0);
  expect(
    await daughter
      .locator('main p')
      .first()
      .evaluate((p) => getComputedStyle(p).fontSize),
  ).toBe('16px');
  expect(
    (await daughter.getByRole('link', { name: 'Patients' }).first().boundingBox())?.height,
  ).toBeGreaterThanOrEqual(44);
  await expectNoSeriousA11yViolations(daughter);
  await daughter.context().close();

  // ---- A second daughter, whose link doesn't allow records, sees nothing ----
  const cousin = await linkedPortalUser(browser, page, patient.id, false);
  await cousin.goto('/portal/patients');
  await expect(cousin.getByText(`DelaCruz${tag}`)).toBeVisible();
  await expect(cousin.getByRole('link', { name: 'View records' })).toHaveCount(0);
  await cousin.goto(`/portal/patients/${patient.id}/records`);
  await expect(cousin.getByText("These records aren't available to you.")).toBeVisible();
  await expect(cousin.getByText(SUMMARY)).toHaveCount(0);
  await expectNoSeriousA11yViolations(cousin);
  await cousin.context().close();
});
