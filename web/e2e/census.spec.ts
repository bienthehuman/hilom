import { expect, test } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { apiPost, createStaff, hospitalDay, signInAs } from './support/staff';

test('a ward nurse lands on their ward board, sees who needs attention and opens the stay', async ({
  page,
  browser,
}) => {
  test.setTimeout(90_000);
  const tag = `${test.info().project.name}${Date.now()}${Math.floor(Math.random() * 1e4)}`;
  const wardCode =
    `C${Date.now().toString(36).slice(-5)}${Math.floor(Math.random() * 100)}`.toUpperCase();

  // ---- Admin sets up a doctor, a ward nurse, a ward with three beds and an admitted patient ----
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
  const beds: { id: string }[] = [];
  for (const label of ['Bed 1', 'Bed 2', 'Bed 10']) {
    beds.push(await apiPost(page, `/api/facility/wards/${ward.id}/beds`, { label }));
  }
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
  await apiPost(page, `/api/patients/${patient.id}/allergies`, {
    allergen: 'Penicillin',
    category: 'drug',
    severity: 'severe',
    reaction: 'Anaphylaxis',
  });
  const stay = await apiPost<{ encounterId: string }>(page, '/api/clinical/admissions', {
    patientId: patient.id,
    attendingPhysicianId: doctor.employeeId,
    bedId: beds[1]!.id,
    admittingDiagnosis: 'Community-acquired pneumonia',
    reason: 'Needs IV antibiotics and oxygen',
  });

  // ---- The nurse signs in and lands on their ward board ----
  const nursePage = await signInAs(browser, nurse);
  const board = nursePage.getByRole('region', { name: new RegExp(`^${wardCode}`) });
  await expect(board.getByRole('heading', { level: 2 })).toContainText('1 of 3 beds occupied');
  // Tiles are the board's bed list; each bed is one ui-bed-tile.
  const tiles = board.locator('ui-bed-tile');
  await expect(tiles).toHaveCount(3);
  const tile = tiles.filter({ hasText: `DelaCruz${tag}, Juan` });
  await expect(tile).toContainText('Bed 2');
  await expect(tile).toContainText(`Dr. Jose Santos${tag}`);
  await expect(tile).toContainText('Allergy: Penicillin');
  await expect(tile).not.toContainText('Critical');
  await expect(tiles.filter({ hasText: 'Bed 10' })).toContainText('Free');
  await expectNoSeriousA11yViolations(nursePage);

  // ---- A critical reading shows up on the next refresh ----
  await apiPost(nursePage, `/api/clinical/encounters/${stay.encounterId}/vital-signs`, {
    heartRate: 140,
    oxygenSaturation: 86,
  });
  await nursePage.getByRole('button', { name: 'Refresh' }).click();
  await expect(tile).toContainText('Critical: Heart rate, SpO₂');

  // The same board is in the sidebar, and the tile opens the stay.
  await nursePage
    .getByRole('navigation', { name: 'Main' })
    .getByRole('link', { name: 'Ward census' })
    .click();
  await expect(nursePage.getByRole('heading', { name: 'Ward census', level: 1 })).toBeVisible();
  await nursePage.getByRole('link', { name: `DelaCruz${tag}, Juan` }).click();
  await expect(
    nursePage.getByRole('heading', { name: `DelaCruz${tag}, Juan`, level: 1 }),
  ).toBeVisible();
  await expect(nursePage.getByText(`${wardCode} · Bed 2`).first()).toBeVisible();
  await nursePage.context().close();
});
