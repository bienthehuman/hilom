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
  }).formatToParts(new Date());
  const part = (type: 'hour' | 'minute') => Number(parts.find((p) => p.type === type)?.value);
  const total = Math.ceil((part('hour') * 60 + part('minute') + minutes) / 5) * 5;
  if (total >= 24 * 60) {
    return null;
  }
  return `${String(Math.floor(total / 60)).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`;
}

test('the front desk checks a patient in and the doctor works the visit', async ({
  page,
  browser,
}) => {
  // The whole visit across two roles: check-in, summary, vitals, voiding and notes.
  test.setTimeout(90_000);
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

  // ---- Vital signs: record two sets, see the flags, the trend and the table ----
  await dr.getByRole('tab', { name: 'Vitals' }).click();
  const vitals = dr.getByRole('tabpanel', { name: 'Vitals' });
  await expect(vitals.getByText('No vital signs yet')).toBeVisible();

  await vitals.getByRole('button', { name: 'Record vital signs' }).click();
  const form = dr.getByRole('dialog');
  await form.getByRole('button', { name: 'Save vital signs' }).click();
  await expect(form.getByText('Enter at least one measurement.')).toBeVisible();
  await form.getByLabel('Temperature (°C)').fill('38.6');
  await form.getByLabel('Systolic BP (mmHg)').fill('185');
  await form.getByLabel('Diastolic BP (mmHg)').fill('95');
  await form.getByLabel('Heart rate (bpm)').fill('88');
  await form.getByLabel('SpO₂ (%)').fill('97');
  await expectNoSeriousA11yViolations(dr);
  await form.getByRole('button', { name: 'Save vital signs' }).click();
  await expect(dr.getByText('Vital signs recorded')).toBeVisible();

  await vitals.getByRole('button', { name: 'Record vital signs' }).click();
  await form.getByLabel('Temperature (°C)').fill('37.2');
  await form.getByLabel('Heart rate (bpm)').fill('76');
  await form.getByRole('button', { name: 'Save vital signs' }).click();

  const readings = vitals.getByRole('table');
  await expect(readings.getByRole('row')).toHaveCount(3);
  // The first set: high temperature and critically high blood pressure, said in words too.
  const first = readings.getByRole('row').nth(2);
  await expect(first).toContainText('38.6');
  await expect(first).toContainText('(high)');
  await expect(first).toContainText('185/95');
  await expect(first).toContainText('(critically high)');
  await expect(vitals.getByRole('img', { name: /^Temperature trend, 2 readings/ })).toBeVisible();
  await vitals.getByRole('button', { name: 'Blood pressure' }).click();
  await expect(vitals.getByRole('img', { name: /^Blood pressure trend, 1 reading/ })).toBeVisible();
  await expectNoSeriousA11yViolations(dr);

  // ---- Void the newer set as entered in error: it stays, struck through, and leaves the trend ----
  await vitals.getByRole('button', { name: 'Temperature' }).click();
  await readings
    .getByRole('row')
    .nth(1)
    .getByRole('button', { name: /^Void reading from / })
    .click();
  const confirmVoid = dr.getByRole('alertdialog');
  await expect(confirmVoid.getByRole('heading', { name: 'Void this reading?' })).toBeVisible();
  await confirmVoid.getByRole('button', { name: 'Void reading' }).click();
  await expect(confirmVoid.getByText('Reason is required.')).toBeVisible();
  await confirmVoid.getByLabel('Reason').fill('Taken on the wrong patient');
  await expectNoSeriousA11yViolations(dr);
  await confirmVoid.getByRole('button', { name: 'Void reading' }).click();
  await expect(dr.getByText('Reading voided')).toBeVisible();
  await expect(readings.getByRole('row')).toHaveCount(3);
  await expect(readings.getByRole('row').nth(1)).toContainText('Voided');
  await expect(readings.getByRole('row').nth(1)).toContainText('Taken on the wrong patient');
  await expect(vitals.getByRole('img', { name: /^Temperature trend, 1 reading/ })).toBeVisible();

  // ---- Notes: write, edit within 24 hours, and void one written in error ----
  await dr.getByRole('tab', { name: 'Notes' }).click();
  const notes = dr.getByRole('tabpanel', { name: 'Notes' });
  await expect(notes.getByText('No notes yet')).toBeVisible();
  await expectNoSeriousA11yViolations(dr);

  await notes.getByRole('button', { name: 'Write note' }).click();
  const noteForm = dr.getByRole('dialog');
  await noteForm.getByRole('button', { name: 'Save note' }).click();
  await expect(noteForm.getByText('Note is required.', { exact: true })).toBeVisible();
  await noteForm.getByRole('combobox', { name: /^Kind of note\b/ }).click();
  await dr.getByRole('option', { name: 'Assessment', exact: true }).click();
  await noteForm
    .getByRole('textbox', { name: /^Note\b/ })
    .fill('Exertional chest pain for 2 weeks. Likely stable angina.');
  await expectNoSeriousA11yViolations(dr);
  await noteForm.getByRole('button', { name: 'Save note' }).click();
  await expect(dr.getByText('Note saved')).toBeVisible();
  const noteList = notes.getByRole('list', { name: 'Notes, newest first' });
  await expect(noteList.getByRole('listitem')).toHaveCount(1);

  await noteList.getByRole('button', { name: 'Edit assessment note' }).click();
  await noteForm
    .getByRole('textbox', { name: /^Note\b/ })
    .fill('Exertional chest pain for 3 weeks. Likely stable angina.');
  await noteForm.getByRole('button', { name: 'Save changes' }).click();
  await expect(dr.getByText('Note updated')).toBeVisible();
  await expect(noteList).toContainText('3 weeks');
  await expect(noteList).toContainText('edited');

  await notes.getByRole('button', { name: 'Write note' }).click();
  await noteForm.getByRole('combobox', { name: /^Kind of note\b/ }).click();
  await dr.getByRole('option', { name: 'Diet', exact: true }).click();
  await noteForm.getByRole('textbox', { name: /^Note\b/ }).fill('Low salt diet.');
  await noteForm.getByRole('button', { name: 'Save note' }).click();
  await expect(noteList.getByRole('listitem')).toHaveCount(2);
  await noteList.getByRole('button', { name: 'Void diet note' }).click();
  await confirmVoid.getByLabel('Reason').fill('Meant for another patient');
  await confirmVoid.getByRole('button', { name: 'Void note' }).click();
  await expect(dr.getByText('Note voided')).toBeVisible();
  const voidedNote = noteList.getByRole('listitem').filter({ hasText: 'Low salt diet.' });
  await expect(voidedNote).toContainText('Voided');
  await expect(voidedNote.getByRole('button', { name: 'Void diet note' })).toHaveCount(0);
  await expectNoSeriousA11yViolations(dr);
  // ---- Orders: a medication needs all its fields; discontinuing needs a reason ----
  await dr.getByRole('tab', { name: 'Orders' }).click();
  const orders = dr.getByRole('tabpanel', { name: 'Orders' });
  await expect(orders.getByText('No orders yet')).toBeVisible();
  await expectNoSeriousA11yViolations(dr);

  await orders.getByRole('button', { name: 'New order' }).click();
  const orderForm = dr.getByRole('dialog');
  await orderForm.getByRole('combobox', { name: /^Kind of order\b/ }).click();
  await dr.getByRole('option', { name: 'Medication', exact: true }).click();
  await orderForm.getByRole('textbox', { name: /^Medication\b/ }).fill('Isosorbide dinitrate');
  await orderForm.getByRole('textbox', { name: /^Dose\b/ }).fill('5 mg');
  await orderForm
    .getByRole('textbox', { name: /^Instructions\b/ })
    .fill('Under the tongue for chest pain.');
  await orderForm.getByRole('button', { name: 'Save order' }).click();
  await expect(orderForm.getByText('Route is required.', { exact: true })).toBeVisible();
  await expect(orderForm.getByText('Frequency is required.', { exact: true })).toBeVisible();
  await orderForm.getByRole('textbox', { name: /^Route\b/ }).fill('sublingual');
  await orderForm.getByRole('textbox', { name: /^Frequency\b/ }).fill('as needed');
  await expectNoSeriousA11yViolations(dr);
  await orderForm.getByRole('button', { name: 'Save order' }).click();
  await expect(dr.getByText('Order saved')).toBeVisible();
  const orderList = orders.getByRole('list', { name: 'Orders, newest first' });
  await expect(orderList.getByRole('listitem')).toHaveCount(1);
  await expect(orderList).toContainText('Isosorbide dinitrate');
  await expect(orderList).toContainText('sublingual');
  await expect(orderList).toContainText('Active');

  await orders.getByRole('button', { name: 'New order' }).click();
  await orderForm.getByRole('combobox', { name: /^Kind of order\b/ }).click();
  await dr.getByRole('option', { name: 'Laboratory', exact: true }).click();
  await expect(orderForm.getByRole('textbox', { name: /^Dose\b/ })).toHaveCount(0);
  await orderForm.getByRole('textbox', { name: /^Order\b/ }).fill('ECG and troponin now.');
  await orderForm.getByRole('button', { name: 'Save order' }).click();
  await expect(orderList.getByRole('listitem')).toHaveCount(2);

  await orderList.getByRole('button', { name: 'Discontinue Laboratory order' }).click();
  const confirmStop = dr.getByRole('alertdialog');
  await confirmStop.getByRole('button', { name: 'Discontinue' }).click();
  await expect(confirmStop.getByText(/reason/i).first()).toBeVisible();
  await confirmStop.getByLabel('Reason').fill('Done in the ED');
  await confirmStop.getByRole('button', { name: 'Discontinue' }).click();
  await expect(dr.getByText('Order discontinued')).toBeVisible();
  const stopped = orderList.getByRole('listitem').filter({ hasText: 'ECG and troponin now.' });
  await expect(stopped).toContainText('Discontinued');
  await expect(stopped).toContainText('Reason: Done in the ED');
  await expect(stopped.getByRole('button', { name: /^Discontinue/ })).toHaveCount(0);
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
