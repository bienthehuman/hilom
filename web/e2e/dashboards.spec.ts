import { expect, test } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { apiPost, createStaff, hospitalDay, signInAs } from './support/staff';

test('each role lands on a dashboard of its own tiles, scoped to what it may see', async ({
  page,
  browser,
}) => {
  test.setTimeout(120_000);
  const tag = `${test.info().project.name}${Date.now()}${Math.floor(Math.random() * 1e4)}`;
  const code = () =>
    `D${Date.now().toString(36).slice(-4)}${Math.floor(Math.random() * 1000)}`.toUpperCase();
  const wardA = code();
  const wardB = code();

  // ---- Admin sets up two wards, a nurse on ward A, a doctor with an inpatient on ward B ----
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
  const nurse = await createStaff(page, 'Rosa', `Reyes${tag}`, 'Staff Nurse');
  const chief = await createStaff(page, 'Carla', `Cruz${tag}`, 'Chief Nurse');
  const desk = await createStaff(page, 'Dina', `Diaz${tag}`, 'Front Desk');

  const wards: { id: string; code: string }[] = [];
  for (const wardCode of [wardA, wardB]) {
    const ward = await apiPost(page, '/api/facility/wards', {
      code: wardCode,
      name: `Medical ${wardCode}`,
      wardType: 'general',
    });
    await apiPost(page, `/api/facility/wards/${ward.id}/beds`, { label: 'Bed 1' });
    wards.push({ id: ward.id, code: wardCode });
  }
  const bedB = await apiPost(page, `/api/facility/wards/${wards[1]!.id}/beds`, { label: 'Bed 2' });
  await apiPost(page, '/api/workforce/ward-assignments', {
    employeeId: nurse.employeeId,
    wardId: wards[0]!.id,
    startsOn: hospitalDay(),
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
  await apiPost(page, '/api/clinical/admissions', {
    patientId: patient.id,
    attendingPhysicianId: doctor.employeeId,
    bedId: bedB.id,
    admittingDiagnosis: 'Community-acquired pneumonia',
    reason: 'Needs IV antibiotics and oxygen',
  });

  // ---- Administrator: audit and users, and the operational tiles their permissions reach ----
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Good');
  await expect(page.getByRole('region', { name: 'Critical audit events' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Users' }).getByText('Active')).toBeVisible();
  await expect(page.getByRole('region', { name: 'Ward occupancy' })).toContainText(wardA);
  await expectNoSeriousA11yViolations(page);

  // The audit tile opens the log already filtered to critical events.
  await page
    .getByRole('region', { name: 'Critical audit events' })
    .getByRole('link', { name: 'Open audit log' })
    .click();
  await expect(page).toHaveURL(/\/admin\/audit\?severity=critical&from=\d{4}-\d{2}-\d{2}/);
  await expect(page.getByRole('combobox', { name: 'Severity' })).toContainText('Critical');

  // ---- Staff nurse: only the ward they are assigned to ----
  const nursePage = await signInAs(browser, nurse);
  const mine = nursePage.getByRole('region', { name: 'My wards' });
  await expect(mine).toContainText(wardA);
  await expect(mine).not.toContainText(wardB);
  await expect(nursePage.getByRole('region', { name: 'Swap inbox' })).toBeVisible();
  await expect(nursePage.getByRole('region', { name: 'My shifts' })).toBeVisible();
  await expect(nursePage.getByRole('region', { name: 'Critical audit events' })).toHaveCount(0);
  await expect(nursePage.getByRole('region', { name: 'Users' })).toHaveCount(0);
  await expectNoSeriousA11yViolations(nursePage);
  await nursePage.context().close();

  // ---- Chief nurse: every ward, and who is rostered on them ----
  const chiefPage = await signInAs(browser, chief);
  const occupancy = chiefPage.getByRole('region', { name: 'Ward occupancy' });
  await expect(occupancy).toContainText(wardA);
  await expect(occupancy).toContainText(wardB);
  const staffing = chiefPage.getByRole('region', { name: 'Staffing today' });
  await expect(staffing).toContainText(wardA);
  await expect(staffing).toContainText('No one rostered');
  await expectNoSeriousA11yViolations(chiefPage);
  await chiefPage.context().close();

  // ---- Physician: their own inpatients, not the ward board ----
  const doctorPage = await signInAs(browser, doctor);
  const inpatients = doctorPage.getByRole('region', { name: 'My inpatients' });
  await expect(inpatients).toContainText(`DelaCruz${tag}, Juan`);
  await expect(doctorPage.getByRole('region', { name: "Today's appointments" })).toBeVisible();
  await expectNoSeriousA11yViolations(doctorPage);
  await inpatients.getByRole('link', { name: `DelaCruz${tag}, Juan` }).click();
  await expect(
    doctorPage.getByRole('heading', { name: `DelaCruz${tag}, Juan`, level: 1 }),
  ).toBeVisible();
  await doctorPage.context().close();

  // ---- Front desk: the day list and link requests ----
  const deskPage = await signInAs(browser, desk);
  await expect(deskPage.getByRole('region', { name: 'Day list' })).toBeVisible();
  await expect(deskPage.getByRole('region', { name: 'Pending link requests' })).toBeVisible();
  await expect(deskPage.getByRole('region', { name: 'My inpatients' })).toHaveCount(0);
  await expectNoSeriousA11yViolations(deskPage);
  await deskPage.context().close();
});
