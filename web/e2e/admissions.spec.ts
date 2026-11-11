import { expect, test } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { apiPost, createStaff, hospitalDay, signInAs } from './support/staff';

test('the front desk admits a patient into a free bed and the ward nurse sees the stay', async ({
  page,
  browser,
}) => {
  test.setTimeout(90_000);
  const tag = `${test.info().project.name}${Date.now()}${Math.floor(Math.random() * 1e4)}`;
  const wardCode =
    `A${Date.now().toString(36).slice(-5)}${Math.floor(Math.random() * 100)}`.toUpperCase();

  // ---- Admin sets up a doctor, a front desk clerk, a ward nurse, a ward with two beds and a patient ----
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
  const clerk = await createStaff(page, 'Ana', `Lim${tag}`, 'Front Desk');
  const nurse = await createStaff(page, 'Rosa', `Reyes${tag}`, 'Staff Nurse');
  const ward = await apiPost(page, '/api/facility/wards', {
    code: wardCode,
    name: `Medical ${tag}`,
    wardType: 'general',
  });
  await apiPost(page, `/api/facility/wards/${ward.id}/beds`, { label: 'Bed 1' });
  await apiPost(page, `/api/facility/wards/${ward.id}/beds`, { label: 'Bed 2' });
  await apiPost(page, '/api/workforce/ward-assignments', {
    employeeId: nurse.employeeId,
    wardId: ward.id,
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

  // ---- The front desk admits from the patient's record ----
  const desk = await signInAs(browser, clerk);
  await desk.goto(`/patients/${patient.id}`);
  await desk.getByRole('link', { name: 'Admit', exact: true }).click();
  await expect(desk.getByRole('heading', { name: 'Admit patient', level: 1 })).toBeVisible();
  await expect(desk.getByRole('heading', { name: `DelaCruz${tag}, Juan` })).toBeVisible();

  // Nothing is sent until the form is complete.
  await desk.getByRole('button', { name: 'Admit patient' }).click();
  await expect(desk.getByText('Choose a free bed.')).toBeVisible();

  await desk.getByRole('combobox', { name: /^Attending physician/ }).click();
  await desk.getByRole('option', { name: new RegExp(`^Dr\\. Jose Santos${tag}`) }).click();
  await desk.getByLabel('Admitting diagnosis').fill('Community-acquired pneumonia');
  await desk.getByLabel('Reason for admission').fill('Needs IV antibiotics and oxygen');
  const wardBeds = desk.getByRole('group', { name: new RegExp(`^${wardCode}`) });
  await expect(wardBeds).toContainText('2 free beds');
  await expectNoSeriousA11yViolations(desk);
  // The radio is visually hidden; people click the bed's tile.
  await wardBeds.getByText('Bed 2', { exact: true }).click();
  await expect(wardBeds.getByRole('radio', { name: `${wardCode}, Bed 2` })).toBeChecked();
  await desk.getByRole('button', { name: 'Admit patient' }).click();

  await expect(desk.getByRole('heading', { name: 'Patient admitted' })).toBeVisible();
  await expect(desk.getByText(`${wardCode} · Medical ${tag}, Bed 2`).first()).toBeVisible();
  // The front desk can't open clinical encounters, only the record.
  await expect(desk.getByRole('link', { name: 'Open the stay' })).toHaveCount(0);

  // The bed is taken now: admitting someone else offers only Bed 1.
  await desk.goto('/clinical/admit');
  await desk
    .getByRole('searchbox', { name: 'Find the patient by name or MRN' })
    .fill(`DelaCruz${tag}`);
  await desk.getByRole('button', { name: new RegExp(`^Choose DelaCruz${tag}`) }).click();
  await expect(wardBeds).toContainText('1 free bed');
  await expect(wardBeds.getByRole('radio')).toHaveCount(1);
  await desk.context().close();

  // ---- The ward nurse finds the stay through their ward ----
  const ward3 = await signInAs(browser, nurse);
  await ward3
    .getByRole('navigation', { name: 'Main' })
    .getByRole('link', { name: 'Encounters' })
    .click();
  const encounters = ward3.getByRole('table', { name: 'Encounters' });
  await encounters.getByRole('link', { name: new RegExp(`^DelaCruz${tag}, Juan`) }).click();
  await expect(
    ward3.getByRole('heading', { name: `DelaCruz${tag}, Juan`, level: 1 }),
  ).toBeVisible();
  await expect(ward3.getByText(`${wardCode} · Bed 2`).first()).toBeVisible();
  await expect(ward3.getByRole('heading', { name: 'Stay summary' })).toBeVisible();
  await expect(ward3.getByText('Community-acquired pneumonia')).toBeVisible();
  await expectNoSeriousA11yViolations(ward3);
  await ward3.context().close();
});
