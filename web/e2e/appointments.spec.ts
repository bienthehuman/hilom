import { expect, test, type Page } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { apiPost, createStaff, signInAs } from './support/staff';

/** A Monday at least a week ahead. */
function nextMonday(): string {
  const day = new Date();
  day.setUTCDate(day.getUTCDate() + 7);
  while (day.getUTCDay() !== 1) {
    day.setUTCDate(day.getUTCDate() + 1);
  }
  return day.toISOString().slice(0, 10);
}

async function registerPatient(admin: Page, lastName: string): Promise<{ id: string }> {
  return apiPost(admin, '/api/patients', {
    firstName: 'Juan',
    lastName,
    birthDate: '1958-03-21',
    sex: 'male',
    address: '12 Mabini St., Marikina City',
    contactNumber: '0917 555 0199',
    emergencyContactName: 'Maria Dela Cruz',
    emergencyContactRelation: 'Daughter',
    emergencyContactNumber: '0918 555 0123',
  });
}

test('a doctor confirms and declines requests, and the front desk sees and cancels them', async ({
  page,
  browser,
}) => {
  const tag = `${test.info().project.name}${Date.now()}${Math.floor(Math.random() * 1e4)}`;
  const monday = nextMonday();

  // ---- A doctor with an account, two slots, and two patients booked into them ----
  await page.goto('/');
  const specialization = await apiPost(page, '/api/workforce/specializations', {
    name: `Cardiology ${tag}`,
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
  await apiPost(page, '/api/scheduling/slots/publish', {
    physicianId: doctor.employeeId,
    from: monday,
    to: monday,
    days: ['monday'],
    startTime: '09:00',
    endTime: '10:00',
  });
  const slots = (await (
    await page.request.get(
      `/api/scheduling/booking/slots?from=${monday}&days=1&physicianId=${doctor.employeeId}`,
    )
  ).json()) as { id: string }[];
  const first = await registerPatient(page, `Reyes${tag}`);
  const second = await registerPatient(page, `Cruz${tag}`);
  await apiPost(page, '/api/scheduling/appointments', {
    slotId: slots[0]!.id,
    patientId: first.id,
    reason: 'Chest pain when climbing stairs',
  });
  await apiPost(page, '/api/scheduling/appointments', {
    slotId: slots[1]!.id,
    patientId: second.id,
    reason: 'Blood pressure check',
  });

  // ---- The doctor works through their requests ----
  const dr = await signInAs(browser, doctor);
  await dr
    .getByRole('navigation', { name: 'Main' })
    .getByRole('link', { name: 'Requests' })
    .click();
  const requests = dr.getByRole('list', { name: 'Requests waiting' });
  await expect(requests.getByRole('listitem')).toHaveCount(2);
  await expect(requests).toContainText('"Chest pain when climbing stairs"');
  await expectNoSeriousA11yViolations(dr);

  await requests.getByRole('button', { name: new RegExp(`^Confirm Reyes${tag}`) }).click();
  await expect(dr.getByText('Appointment confirmed')).toBeVisible();
  await expect(requests.getByRole('listitem')).toHaveCount(1);

  await requests.getByRole('button', { name: new RegExp(`^Decline Cruz${tag}`) }).click();
  const dialog = dr.getByRole('dialog');
  await dialog.getByRole('button', { name: 'Decline request' }).click();
  await expect(dialog.getByText('Reason is required.')).toBeVisible();
  await dialog.getByLabel('Reason').fill('I am away that morning. Please book Thursday instead.');
  await expectNoSeriousA11yViolations(dr);
  await dialog.getByRole('button', { name: 'Decline request' }).click();
  await expect(dr.getByText('No requests waiting')).toBeVisible();
  await dr.context().close();

  // ---- The front desk's day list ----
  await page.goto('/scheduling/appointments');
  await page.getByLabel('Day', { exact: true }).fill(monday);
  await page.getByRole('combobox', { name: 'Doctor' }).click();
  await page.getByRole('option', { name: `Dr. Jose Santos${tag}` }).click();
  const day = page.getByRole('list', { name: /^Appointments, / });
  const confirmed = day.getByRole('listitem').filter({ hasText: `Reyes${tag}` });
  const declined = day.getByRole('listitem').filter({ hasText: `Cruz${tag}` });
  await expect(confirmed).toContainText('Confirmed');
  await expect(confirmed).toContainText('09:00–09:30');
  await expect(declined).toContainText('Declined');
  await expect(declined).toContainText('Please book Thursday instead.');
  await expectNoSeriousA11yViolations(page);

  await confirmed.getByRole('button', { name: /^Cancel / }).click();
  await page
    .getByRole('dialog')
    .getByLabel('Reason')
    .fill('Patient called to say he is admitted elsewhere');
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel appointment' }).click();
  await expect(confirmed).toContainText('Cancelled');
  await expect(confirmed).toContainText('admitted elsewhere');
});
