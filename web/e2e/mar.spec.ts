import { expect, test } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { apiPost, createStaff, hospitalDay, signInAs } from './support/staff';

test('a ward nurse records doses on the MAR after the allergy check, and voids one entered in error', async ({
  page,
  browser,
}) => {
  test.setTimeout(90_000);
  const tag = `${test.info().project.name}${Date.now()}${Math.floor(Math.random() * 1e4)}`;
  const wardCode =
    `M${Date.now().toString(36).slice(-5)}${Math.floor(Math.random() * 100)}`.toUpperCase();

  // ---- Admin sets up a doctor, a ward nurse, a ward and an admitted patient with allergies ----
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
  const ward = await apiPost(page, '/api/facility/wards', {
    code: wardCode,
    name: `Medical ${tag}`,
    wardType: 'general',
  });
  const bed = await apiPost(page, `/api/facility/wards/${ward.id}/beds`, { label: 'Bed 1' });
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
  for (const [allergen, severity] of [
    ['Penicillin', 'severe'],
    ['Amoxicillin', 'moderate'],
  ] as const) {
    await apiPost(page, `/api/patients/${patient.id}/allergies`, {
      allergen,
      category: 'drug',
      severity,
      reaction: 'Rash',
    });
  }
  const stay = await apiPost<{ encounterId: string }>(page, '/api/clinical/admissions', {
    patientId: patient.id,
    attendingPhysicianId: doctor.employeeId,
    bedId: bed.id,
    admittingDiagnosis: 'Community-acquired pneumonia',
    reason: 'Needs IV antibiotics',
  });

  // ---- The doctor orders two medications (one matches an allergy by name) ----
  const dr = await signInAs(browser, doctor);
  for (const [name, dose, route, frequency] of [
    ['Amoxicillin', '500 mg', 'oral', 'every 8 hours'],
    ['Paracetamol', '1 g', 'oral', 'every 6 hours as needed'],
  ]) {
    await apiPost(dr, `/api/clinical/encounters/${stay.encounterId}/orders`, {
      orderType: 'medication',
      details: `${name} as ordered`,
      medicationName: name,
      dose,
      route,
      frequency,
    });
  }
  // Physicians read the MAR but don't record on it.
  await dr.goto(`/clinical/encounters/${stay.encounterId}`);
  await dr.getByRole('tab', { name: 'MAR', exact: true }).click();
  const drMar = dr.getByRole('tabpanel', { name: 'MAR', exact: true });
  await expect(drMar.getByRole('heading', { name: /^Paracetamol/ })).toBeVisible();
  await expect(drMar.getByRole('button', { name: /^Record dose/ })).toHaveCount(0);
  await dr.context().close();

  // ---- The nurse opens the stay's MAR ----
  const n = await signInAs(browser, nurse);
  await n.goto(`/clinical/encounters/${stay.encounterId}`);
  await n.getByRole('tab', { name: 'MAR', exact: true }).click();
  const mar = n.getByRole('tabpanel', { name: 'MAR', exact: true });
  const amox = mar
    .getByRole('list', { name: 'Medication orders, active first' })
    .getByRole('listitem')
    .filter({ has: n.getByRole('heading', { name: /^Amoxicillin/ }) });
  await expect(amox).toContainText('Allergy: Amoxicillin');
  await expect(amox).toContainText('No doses recorded yet.');
  await expectNoSeriousA11yViolations(n);

  // ---- Recording: the allergy warning comes first, and a missed dose needs a reason ----
  await amox.getByRole('button', { name: 'Record dose of Amoxicillin' }).click();
  const dialog = n.getByRole('dialog');
  await expect(dialog.getByTestId('allergy-warning')).toContainText(
    'Possible allergy: Amoxicillin',
  );
  await expect(dialog.getByTestId('allergy-warning')).toContainText('Penicillin');
  await expect(dialog.getByRole('textbox', { name: /^Dose given\b/ })).toHaveValue('500 mg');
  await expectNoSeriousA11yViolations(n);

  await dialog.getByRole('combobox', { name: /^Outcome\b/ }).click();
  await n.getByRole('option', { name: 'Held', exact: true }).click();
  await expect(dialog.getByRole('textbox', { name: /^Dose given\b/ })).toHaveCount(0);
  await dialog.getByRole('button', { name: 'Record dose' }).click();
  await expect(dialog.getByText('Reason not given is required.', { exact: true })).toBeVisible();
  await expect(dialog.getByText('Confirm you checked the allergies.')).toBeVisible();
  await dialog
    .getByRole('textbox', { name: /^Reason not given\b/ })
    .fill('Possible allergy; asked Dr. Santos to review');
  await dialog.getByRole('checkbox', { name: /I checked the allergies/ }).click();
  await dialog.getByRole('button', { name: 'Record dose' }).click();
  await expect(n.getByText('Missed dose recorded')).toBeVisible();
  const doses = amox.getByRole('list', { name: 'Doses of Amoxicillin, newest first' });
  await expect(doses.getByRole('listitem')).toHaveCount(1);
  await expect(doses).toContainText('Held');
  await expect(doses).toContainText('Reason: Possible allergy; asked Dr. Santos to review');

  // ---- A dose given, then voided as entered in error: it stays, struck through ----
  const para = mar
    .getByRole('list', { name: 'Medication orders, active first' })
    .getByRole('listitem')
    .filter({ has: n.getByRole('heading', { name: /^Paracetamol/ }) });
  await expect(para).not.toContainText('Allergy:');
  await para.getByRole('button', { name: 'Record dose of Paracetamol' }).click();
  await expect(dialog.getByTestId('allergy-warning')).toContainText(
    'Check these 2 allergies before giving',
  );
  await dialog.getByRole('checkbox', { name: /I checked the allergies/ }).click();
  await dialog.getByRole('button', { name: 'Record dose' }).click();
  await expect(n.getByText('Dose recorded', { exact: true })).toBeVisible();
  const paraDoses = para.getByRole('list', { name: 'Doses of Paracetamol, newest first' });
  await expect(paraDoses).toContainText('Given');
  await expect(paraDoses).toContainText('1 g');

  await paraDoses.getByRole('button', { name: /^Void dose of Paracetamol/ }).click();
  const confirmVoid = n.getByRole('alertdialog');
  await confirmVoid.getByLabel('Reason').fill('Recorded against the wrong patient');
  await confirmVoid.getByRole('button', { name: 'Void dose' }).click();
  await expect(n.getByText('Dose voided')).toBeVisible();
  await expect(paraDoses.getByRole('listitem')).toHaveCount(1);
  await expect(paraDoses).toContainText('Voided');
  await expect(paraDoses.getByRole('button', { name: /^Void dose/ })).toHaveCount(0);
  await expectNoSeriousA11yViolations(n);
  await n.context().close();
});
