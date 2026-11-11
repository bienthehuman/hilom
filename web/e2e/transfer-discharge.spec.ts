import { expect, test } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { apiPost, createStaff, hospitalDay, signInAs } from './support/staff';

test('a head nurse moves a patient to another ward, and the physician discharges them', async ({
  page,
  browser,
}) => {
  test.setTimeout(120_000);
  const tag = `${test.info().project.name}${Date.now()}${Math.floor(Math.random() * 1e4)}`;
  const code = (prefix: string) =>
    `${prefix}${Date.now().toString(36).slice(-5)}${Math.floor(Math.random() * 100)}`.toUpperCase();
  const ward3Code = code('T');
  const ward5Code = code('U');

  // ---- Admin sets up a doctor, a head nurse on each ward, two wards and an admitted patient ----
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
  const ward3Nurse = await createStaff(page, 'Rosa', `Reyes${tag}`, 'Head Nurse');
  const ward5Nurse = await createStaff(page, 'Lea', `Garcia${tag}`, 'Head Nurse');
  const ward3 = await apiPost(page, '/api/facility/wards', {
    code: ward3Code,
    name: `Medical ${tag}`,
    wardType: 'general',
  });
  const ward5 = await apiPost(page, '/api/facility/wards', {
    code: ward5Code,
    name: `Telemetry ${tag}`,
    wardType: 'general',
  });
  const bed = await apiPost(page, `/api/facility/wards/${ward3.id}/beds`, { label: 'Bed 1' });
  await apiPost(page, `/api/facility/wards/${ward5.id}/beds`, { label: 'Bed 4' });
  for (const [nurse, ward] of [
    [ward3Nurse, ward3],
    [ward5Nurse, ward5],
  ] as const) {
    await apiPost(page, '/api/workforce/ward-assignments', {
      employeeId: nurse.employeeId,
      wardId: ward.id,
      startsOn: hospitalDay(),
    });
  }
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

  // ---- The Ward 3 head nurse moves the patient to Ward 5 ----
  const nurse3 = await signInAs(browser, ward3Nurse);
  await nurse3.goto(`/clinical/encounters/${encounterId}`);
  await expect(nurse3.getByText(`${ward3Code} · Bed 1`).first()).toBeVisible();
  // A ward stay ends by discharge, which nurses don't do.
  await expect(nurse3.getByRole('button', { name: 'Close encounter' })).toHaveCount(0);
  await expect(nurse3.getByRole('button', { name: 'Discharge' })).toHaveCount(0);
  await nurse3.getByRole('button', { name: 'Transfer' }).click();
  const transfer = nurse3.getByRole('dialog', { name: 'Transfer patient' });
  const ward5Beds = transfer.getByRole('group', { name: new RegExp(`^${ward5Code}`) });
  await expect(ward5Beds).toContainText('1 free bed');
  await expectNoSeriousA11yViolations(nurse3);

  // Nothing is sent until the form is complete.
  await transfer.getByRole('button', { name: 'Transfer patient' }).click();
  await expect(transfer.getByText('Choose a free bed.')).toBeVisible();
  await ward5Beds.getByText('Bed 4', { exact: true }).click();
  await transfer.getByLabel('Reason for transfer').fill('Needs cardiac monitoring');
  await transfer.getByRole('button', { name: 'Transfer patient' }).click();

  // The patient is off this nurse's wards now: back to the board, without them on it.
  await expect(nurse3).toHaveURL(/\/clinical\/census$/);
  await expect(nurse3.getByText("They're no longer on your wards")).toBeVisible();
  await expect(nurse3.getByText(`DelaCruz${tag}`)).toHaveCount(0);
  await nurse3.goto(`/clinical/encounters/${encounterId}`);
  await expect(nurse3.getByRole('heading', { name: 'Encounter not found' })).toBeVisible();
  await nurse3.context().close();

  // ---- The Ward 5 head nurse has them now ----
  const nurse5 = await signInAs(browser, ward5Nurse);
  await nurse5.goto('/clinical/census');
  await nurse5.getByRole('link', { name: new RegExp(`DelaCruz${tag}`) }).click();
  await expect(nurse5.getByText(`${ward5Code} · Bed 4`).first()).toBeVisible();
  const history = nurse5.getByRole('list', { name: 'Bed history' });
  await expect(history.getByRole('listitem')).toHaveCount(2);
  await expect(history).toContainText('Needs cardiac monitoring');
  await nurse5.context().close();

  // ---- The attending physician discharges ----
  const physician = await signInAs(browser, doctor);
  await physician.goto(`/clinical/encounters/${encounterId}`);
  await physician.getByRole('button', { name: 'Discharge' }).click();
  const discharge = physician.getByRole('dialog', { name: 'Discharge patient' });
  await discharge.getByRole('button', { name: 'Discharge patient' }).click();
  await expect(discharge.getByText('Discharge summary is required.')).toBeVisible();
  await discharge.getByRole('combobox', { name: /^Disposition/ }).click();
  await physician.getByRole('option', { name: 'Home' }).click();
  await discharge
    .getByLabel('Discharge summary')
    .fill('Pneumonia resolved on IV antibiotics. Home on oral amoxicillin for 5 days.');
  await expectNoSeriousA11yViolations(physician);
  await discharge.getByRole('button', { name: 'Discharge patient' }).click();

  await expect(discharge).toHaveCount(0);
  await expect(physician.getByText('Ward stay · Discharged')).toBeVisible();
  await expect(physician.getByRole('button', { name: 'Discharge' })).toHaveCount(0);
  await expect(physician.getByRole('button', { name: 'Transfer' })).toHaveCount(0);
  await expect(physician.getByText('Last ward and bed')).toBeVisible();

  // The summary is a note on the stay, which the physician can still edit.
  await physician.getByRole('tab', { name: 'Notes' }).click();
  await expect(physician.getByText('Discharge summary').first()).toBeVisible();
  await expect(physician.getByText('Home on oral amoxicillin')).toBeVisible();
  await expectNoSeriousA11yViolations(physician);
  await physician.context().close();
});
