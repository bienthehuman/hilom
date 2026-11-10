import { expect, test } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { apiPost, createStaff, hospitalDay, signInAs } from './support/staff';

/** Hospital time now plus `minutes`, rounded up to 5 minutes, as HH:mm; null if it passes midnight. */
function hospitalTimeIn(minutes: number): string | null {
  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone: 'Asia/Manila',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  })
    .formatToParts(new Date())
    .reduce<Record<string, string>>((acc, p) => ({ ...acc, [p.type]: p.value }), {});
  const total = Math.ceil((Number(parts.hour) * 60 + Number(parts.minute) + minutes) / 5) * 5;
  if (total >= 24 * 60) {
    return null;
  }
  return `${String(Math.floor(total / 60)).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`;
}

test('the front desk checks a patient in and the doctor works the visit', async ({
  page,
  browser,
}) => {
  // Check-in only works on the appointment's day, so the slot has to be later today.
  const start = hospitalTimeIn(5);
  const end = hospitalTimeIn(35);
  test.skip(!start || !end, 'Too close to midnight in hospital time to book a slot for today');

  const tag = `${test.info().project.name}${Date.now()}${Math.floor(Math.random() * 1e4)}`;
  const today = hospitalDay();
  const weekday = new Intl.DateTimeFormat('en-US', { weekday: 'long', timeZone: 'UTC' })
    .format(new Date(`${today}T00:00:00Z`))
    .toLowerCase();

  // ---- Admin sets up a doctor with a slot later today, a front desk clerk and a booked patient ----
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
  await apiPost(page, '/api/scheduling/slots/publish', {
    physicianId: doctor.employeeId,
    from: today,
    to: today,
    days: [weekday],
    startTime: start,
    endTime: end,
  });
  const slots = (await (
    await page.request.get(
      `/api/scheduling/booking/slots?from=${today}&days=1&physicianId=${doctor.employeeId}`,
    )
  ).json()) as { id: string }[];
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
  const booked = await apiPost<{ id: string; version: number }>(
    page,
    '/api/scheduling/appointments',
    { slotId: slots[0]!.id, patientId: patient.id, reason: 'Chest pain when climbing stairs' },
  );
  await apiPost(page, `/api/scheduling/appointments/${booked.id}/confirm`, {
    version: booked.version,
  });

  // ---- The front desk checks the patient in from the day list ----
  const desk = await signInAs(browser, clerk);
  await desk
    .getByRole('navigation', { name: 'Main' })
    .getByRole('link', { name: 'Appointments' })
    .click();
  const row = desk
    .getByRole('list', { name: /^Appointments, / })
    .getByRole('listitem')
    .filter({ hasText: `DelaCruz${tag}` });
  await expect(row).toContainText('Confirmed');
  await expectNoSeriousA11yViolations(desk);
  await row.getByRole('button', { name: /^Check in / }).click();
  await expect(row).toContainText('Checked in');
  await expect(row.getByRole('button', { name: /^Check in / })).toHaveCount(0);
  // The front desk can't open clinical encounters.
  await expect(row.getByRole('link', { name: /^Open visit/ })).toHaveCount(0);
  await expect(
    desk.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Encounters' }),
  ).toHaveCount(0);
  await desk.context().close();

  // ---- The doctor finds the visit among their open encounters ----
  const dr = await signInAs(browser, doctor);
  await dr
    .getByRole('navigation', { name: 'Main' })
    .getByRole('link', { name: 'Encounters' })
    .click();
  await expect(dr.getByRole('heading', { name: 'Encounters', level: 1 })).toBeVisible();
  const encounters = dr.getByRole('table', { name: 'Encounters' });
  await expect(encounters).toContainText('Chest pain when climbing stairs');
  await expectNoSeriousA11yViolations(dr);
  await encounters.getByRole('link', { name: new RegExp(`^DelaCruz${tag}, Juan`) }).click();

  // ---- The encounter page: header with the severe allergy, summary, tabs ----
  await expect(dr.getByRole('heading', { name: `DelaCruz${tag}, Juan`, level: 1 })).toBeVisible();
  await expect(dr.getByTestId('severe-allergy-banner')).toContainText('Penicillin (Anaphylaxis)');
  await expect(dr.getByText('Clinic visit', { exact: true }).first()).toBeVisible();
  await expect(dr.getByText(`Dr. Jose Santos${tag}`)).toBeVisible();
  await expectNoSeriousA11yViolations(dr);

  await dr.getByRole('button', { name: /^Edit visit summary/ }).click();
  await dr.getByLabel('Diagnosis summary').fill('Stable angina. For ECG and lipid profile.');
  await dr.getByRole('button', { name: 'Save summary' }).click();
  await expect(dr.getByText('Visit summary saved')).toBeVisible();
  await expect(dr.getByText('Stable angina. For ECG and lipid profile.')).toBeVisible();

  await dr.getByRole('tab', { name: 'Vitals' }).click();
  await expect(
    dr.getByRole('tabpanel', { name: 'Vitals' }).getByText('Nothing recorded yet'),
  ).toBeVisible();
  await expectNoSeriousA11yViolations(dr);
  await dr.getByRole('tab', { name: 'Summary' }).click();

  await dr.getByRole('button', { name: 'Close encounter' }).click();
  await dr.getByRole('alertdialog').getByRole('button', { name: 'Close encounter' }).click();
  await expect(dr.getByText('Encounter closed')).toBeVisible();
  await expect(dr.getByRole('button', { name: 'Close encounter' })).toHaveCount(0);
  await expect(dr.getByRole('button', { name: /^Edit visit summary/ })).toHaveCount(0);

  // ---- The appointment is completed, and the patient's record lists the visit ----
  await dr.getByRole('link', { name: 'Patient record' }).click();
  const card = dr.getByRole('region', { name: 'Encounters' });
  await expect(card).toContainText('Clinic visit');
  await expect(card).toContainText('Closed');
  await expectNoSeriousA11yViolations(dr);

  await dr
    .getByRole('navigation', { name: 'Main' })
    .getByRole('link', { name: 'Appointments' })
    .click();
  const visit = dr
    .getByRole('list', { name: /^Appointments, / })
    .getByRole('listitem')
    .filter({ hasText: `DelaCruz${tag}` });
  await expect(visit).toContainText('Completed');
  await expect(visit.getByRole('link', { name: /^Open visit/ })).toBeVisible();
  await dr.context().close();
});
