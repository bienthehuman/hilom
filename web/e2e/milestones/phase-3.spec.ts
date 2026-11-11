import { expect, test, type Browser, type Page } from '@playwright/test';

import { expectNoSeriousA11yViolations } from '../support/accessibility';
import { apiPost, createStaff, hospitalDay, signInAs } from '../support/staff';

/**
 * Phase 3 demo script (docs/ARCHITECTURE.md §4, steps 5–10), run end to end through the UI as each person:
 *
 * 5. The front desk checks the patient in. The Chief Nurse records vitals and Dr. Santos writes an assessment.
 * 6. Dr. Santos admits the patient to Ward 3, Bed 12.
 * 7. The Chief Nurse assigns Nurse Reyes to Ward 3. She sees the patient on the census, records vitals and
 *    gives the medication Dr. Santos ordered.
 * 8. Nurse Reyes requests a shift swap, the other nurse accepts and the Head Nurse of Ward 3 approves.
 * 9. The Head Nurse transfers the patient to Ward 5: Ward 3's nurses lose access, Ward 5's gain it.
 * 10. Dr. Santos discharges. The daughter reads the visit summary in the portal.
 *
 * Steps 1–4 are the Phase 2 milestone, so the hospital's setup (staff, wards, shifts, the patient, his
 * confirmed appointment today and his daughter's verified link) goes through the API.
 *
 * Who records vitals on a clinic visit: the Chief Nurse, who holds `clinical.vitals.record` at `all`. Ward
 * nurses reach only admitted patients (docs/BUILD_PLAN.md, backlog).
 */

const PASSWORD = 'Milestone-Pass-2026';
const SUMMARY = 'Pneumonia resolved on IV antibiotics. Home on oral amoxicillin for 5 days.';

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

const longDay = (date: string) =>
  new Intl.DateTimeFormat('en-PH', {
    weekday: 'long',
    month: 'long',
    day: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(`${date}T00:00:00Z`));

const sidebarLink = (page: Page, name: string) =>
  page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name, exact: true });

/** The daughter's portal account on a phone, linked to her father with the right to see records. */
async function linkedDaughter(browser: Browser, admin: Page, patientId: string): Promise<Page> {
  const context = await browser.newContext({
    storageState: { cookies: [], origins: [] },
    viewport: { width: 360, height: 740 },
    colorScheme: test.info().project.name === 'dark' ? 'dark' : 'light',
  });
  const portal = await context.newPage();
  const email = `maria.${Date.now().toString(36)}${Math.floor(Math.random() * 1e4)}@family.test`;
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
    relationship: 'representative',
    canBook: true,
    canViewRecords: true,
    version: link.version,
  });
  return portal;
}

test('Phase 3: from check-in to discharge across front desk, physician, chief, head and staff nurses', async ({
  page,
  browser,
}) => {
  test.setTimeout(300_000);
  // Check-in only works on the appointment's day, so the slot has to be later today.
  const start = hospitalTimeIn(5);
  const end = hospitalTimeIn(35);
  test.skip(!start || !end, 'Too close to midnight in hospital time to book a slot for today');

  const tag =
    `${test.info().project.name[0]}${Date.now().toString(36)}${Math.floor(Math.random() * 1e3)}`.toUpperCase();
  const father = `DelaCruz${tag}`;
  const ward3Code = `T${tag}`.slice(0, 8);
  const ward5Code = `U${tag}`.slice(0, 8);
  const today = hospitalDay();
  const weekday = new Intl.DateTimeFormat('en-US', { weekday: 'long', timeZone: 'UTC' })
    .format(new Date(`${today}T00:00:00Z`))
    .toLowerCase();

  // ---- Hospital setup (administrator, through the API) ----
  await page.goto('/');
  const specialization = await apiPost(page, '/api/workforce/specializations', {
    name: `Internal Medicine ${tag}`,
  });
  const santos = await createStaff(page, 'Jose', `Santos${tag}`, 'Physician', {
    professionalType: 'physician',
    licenseNumber: '0123456',
    physician: {
      specializationId: specialization.id,
      acceptsAppointments: true,
      consultationMinutes: 30,
    },
  });
  const clerk = await createStaff(page, 'Ana', `Lim${tag}`, 'Front Desk');
  const chief = await createStaff(page, 'Carla', `Mendoza${tag}`, 'Chief Nurse');
  const reyes = await createStaff(page, 'Rosa', `Reyes${tag}`, 'Staff Nurse');
  const ben = await createStaff(page, 'Ben', `Cruz${tag}`, 'Staff Nurse');
  const head3 = await createStaff(page, 'Cora', `Lim3${tag}`, 'Head Nurse');
  const nurse5 = await createStaff(page, 'Lea', `Garcia${tag}`, 'Staff Nurse');

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
  await apiPost(page, `/api/facility/wards/${ward3.id}/beds`, { label: 'Bed 12' });
  await apiPost(page, `/api/facility/wards/${ward5.id}/beds`, { label: 'Bed 5' });
  // Everyone except Nurse Reyes is already on their ward; the Chief Nurse assigns her in step 7.
  for (const [nurse, ward] of [
    [ben, ward3],
    [head3, ward3],
    [nurse5, ward5],
  ] as const) {
    await apiPost(page, '/api/workforce/ward-assignments', {
      employeeId: nurse.employeeId,
      wardId: ward.id,
      startsOn: hospitalDay(-1),
    });
  }
  const amCode = `A${tag}`.slice(0, 10);
  const pmCode = `P${tag}`.slice(0, 10);
  const am = await apiPost(page, '/api/workforce/shifts', {
    code: amCode,
    name: 'Morning',
    startsAt: '06:00:00',
    endsAt: '14:00:00',
    color: 'am',
  });
  const pm = await apiPost(page, '/api/workforce/shifts', {
    code: pmCode,
    name: 'Afternoon',
    startsAt: '14:00:00',
    endsAt: '22:00:00',
    color: 'pm',
  });
  const reyesDay = hospitalDay(2);
  const benDay = hospitalDay(3);
  await apiPost(page, '/api/workforce/roster/entries', {
    employeeId: ben.employeeId,
    wardId: ward3.id,
    shiftId: pm.id,
    dutyDate: benDay,
  });

  const patient = await apiPost<{ id: string }>(page, '/api/patients', {
    firstName: 'Juan',
    lastName: father,
    birthDate: '1958-03-21',
    sex: 'male',
    address: '12 Mabini St., Marikina City',
    contactNumber: '0917 555 0199',
    emergencyContactName: 'Maria Dela Cruz',
    emergencyContactRelation: 'Daughter',
    emergencyContactNumber: '0918 555 0123',
  });
  // Steps 3–4 (Phase 2): his daughter is linked, and his visit with Dr. Santos is booked and confirmed.
  await apiPost(page, '/api/scheduling/slots/publish', {
    physicianId: santos.employeeId,
    from: today,
    to: today,
    days: [weekday],
    startTime: start,
    endTime: end,
  });
  const slots = (await (
    await page.request.get(
      `/api/scheduling/booking/slots?from=${today}&days=1&physicianId=${santos.employeeId}`,
    )
  ).json()) as { id: string }[];
  const booked = await apiPost<{ id: string; version: number }>(
    page,
    '/api/scheduling/appointments',
    { slotId: slots[0]!.id, patientId: patient.id, reason: 'Cough and fever for a week' },
  );
  await apiPost(page, `/api/scheduling/appointments/${booked.id}/confirm`, {
    version: booked.version,
  });
  const daughter = await linkedDaughter(browser, page, patient.id);

  // ---- 5. The front desk checks him in ----
  const desk = await signInAs(browser, clerk);
  await expectNoSeriousA11yViolations(desk);
  await sidebarLink(desk, 'Appointments').click();
  const row = desk
    .getByRole('list', { name: /^Appointments, / })
    .getByRole('listitem')
    .filter({ hasText: father });
  await expect(row).toContainText('Confirmed');
  await expectNoSeriousA11yViolations(desk);
  await row.getByRole('button', { name: /^Check in / }).click();
  await expect(row).toContainText('Checked in');
  await expectNoSeriousA11yViolations(desk);
  await desk.context().close();

  // ... the Chief Nurse records his vitals ...
  const chiefPage = await signInAs(browser, chief);
  await expectNoSeriousA11yViolations(chiefPage);
  await sidebarLink(chiefPage, 'Encounters').click();
  const clinicVisits = chiefPage.getByRole('table', { name: 'Encounters' });
  await expect(clinicVisits).toContainText('Cough and fever for a week');
  await expectNoSeriousA11yViolations(chiefPage);
  await clinicVisits.getByRole('link', { name: new RegExp(`^${father}, Juan`) }).click();
  await expect(chiefPage.getByRole('heading', { name: `${father}, Juan`, level: 1 })).toBeVisible();
  await chiefPage.getByRole('tab', { name: 'Vitals' }).click();
  const chiefVitals = chiefPage.getByRole('tabpanel', { name: 'Vitals' });
  await chiefVitals.getByRole('button', { name: 'Record vital signs' }).click();
  const vitalsForm = chiefPage.getByRole('dialog');
  await vitalsForm.getByLabel('Temperature (°C)').fill('38.4');
  await vitalsForm.getByLabel('Heart rate (bpm)').fill('96');
  await vitalsForm.getByLabel('SpO₂ (%)').fill('93');
  await expectNoSeriousA11yViolations(chiefPage);
  await vitalsForm.getByRole('button', { name: 'Save vital signs' }).click();
  await expect(chiefPage.getByText('Vital signs recorded')).toBeVisible();
  await expect(chiefVitals.getByRole('table')).toContainText('38.4');
  await expectNoSeriousA11yViolations(chiefPage);
  await chiefPage.context().close();

  // ... and Dr. Santos writes his assessment.
  const dr = await signInAs(browser, santos);
  await expectNoSeriousA11yViolations(dr);
  await sidebarLink(dr, 'Encounters').click();
  const visits = dr.getByRole('table', { name: 'Encounters' });
  await expectNoSeriousA11yViolations(dr);
  await visits.getByRole('link', { name: new RegExp(`^${father}, Juan`) }).click();
  await expect(dr.getByRole('heading', { name: `${father}, Juan`, level: 1 })).toBeVisible();
  await expectNoSeriousA11yViolations(dr);
  await dr.getByRole('tab', { name: 'Vitals' }).click();
  await expect(dr.getByRole('tabpanel', { name: 'Vitals' }).getByRole('table')).toContainText(
    '38.4',
  );
  await dr.getByRole('tab', { name: 'Notes' }).click();
  const notes = dr.getByRole('tabpanel', { name: 'Notes' });
  await notes.getByRole('button', { name: 'Write note' }).click();
  const noteForm = dr.getByRole('dialog');
  await noteForm.getByRole('combobox', { name: /^Kind of note\b/ }).click();
  await dr.getByRole('option', { name: 'Assessment', exact: true }).click();
  await noteForm
    .getByRole('textbox', { name: /^Note\b/ })
    .fill('Fever, cough and low oxygen saturation. Likely pneumonia. Admit for IV antibiotics.');
  await expectNoSeriousA11yViolations(dr);
  await noteForm.getByRole('button', { name: 'Save note' }).click();
  await expect(dr.getByText('Note saved')).toBeVisible();
  await expectNoSeriousA11yViolations(dr);

  // ---- 6. He decides to admit: an inpatient encounter in Ward 3, Bed 12 ----
  await dr.getByRole('link', { name: 'Admit', exact: true }).click();
  await expect(dr.getByRole('heading', { name: 'Admit patient', level: 1 })).toBeVisible();
  await expect(dr.getByText('Admitted from their clinic visit; the two are linked.')).toBeVisible();
  await dr.getByLabel('Admitting diagnosis').fill('Community-acquired pneumonia');
  await dr.getByLabel('Reason for admission').fill('Needs IV antibiotics and oxygen');
  const beds = dr.getByRole('group', { name: new RegExp(`^${ward3Code}`) });
  await expect(beds).toContainText('1 free bed');
  await expectNoSeriousA11yViolations(dr);
  await beds.getByText('Bed 12', { exact: true }).click();
  await dr.getByRole('button', { name: 'Admit patient' }).click();
  await expect(dr.getByRole('heading', { name: 'Patient admitted' })).toBeVisible();
  await expect(dr.getByText(`${ward3Code} · Medical ${tag}, Bed 12`).first()).toBeVisible();
  await expectNoSeriousA11yViolations(dr);
  await dr.getByRole('link', { name: 'Open the stay' }).click();
  await expect(dr.getByText(`${ward3Code} · Bed 12`).first()).toBeVisible();
  const stayUrl = dr.url();

  // ---- 7. The Chief Nurse assigns Nurse Reyes to Ward 3 ----
  const chiefAgain = await signInAs(browser, chief);
  await chiefAgain.goto(`/workforce/employees/${reyes.employeeId}`);
  const wardCard = chiefAgain.getByRole('region', { name: 'Ward assignment' });
  await expectNoSeriousA11yViolations(chiefAgain);
  await wardCard.getByRole('button', { name: 'Assign to ward' }).click();
  const assign = chiefAgain.getByRole('dialog');
  await assign.getByRole('combobox', { name: /^Ward/ }).click();
  await chiefAgain.getByRole('option', { name: new RegExp(`^${ward3Code} · `) }).click();
  await expect(chiefAgain.getByRole('listbox')).toHaveCount(0);
  await expectNoSeriousA11yViolations(chiefAgain);
  await assign.getByRole('button', { name: 'Assign' }).click();
  await expect(chiefAgain.getByText('Assigned to ward')).toBeVisible();
  await expect(wardCard).toContainText(`On ${ward3Code} · Medical ${tag}`);
  await expectNoSeriousA11yViolations(chiefAgain);
  await chiefAgain.context().close();

  // Dr. Santos orders the medication.
  await dr.getByRole('tab', { name: 'Orders' }).click();
  const orders = dr.getByRole('tabpanel', { name: 'Orders' });
  await orders.getByRole('button', { name: 'New order' }).click();
  const orderForm = dr.getByRole('dialog');
  await orderForm.getByRole('combobox', { name: /^Kind of order\b/ }).click();
  await dr.getByRole('option', { name: 'Medication', exact: true }).click();
  await orderForm.getByRole('textbox', { name: /^Medication\b/ }).fill('Ceftriaxone');
  await orderForm.getByRole('textbox', { name: /^Dose\b/ }).fill('1 g');
  await orderForm.getByRole('textbox', { name: /^Route\b/ }).fill('IV');
  await orderForm.getByRole('textbox', { name: /^Frequency\b/ }).fill('every 24 hours');
  await orderForm.getByRole('textbox', { name: /^Instructions\b/ }).fill('Over 30 minutes.');
  await expectNoSeriousA11yViolations(dr);
  await orderForm.getByRole('button', { name: 'Save order' }).click();
  await expect(dr.getByText('Order saved')).toBeVisible();
  await expectNoSeriousA11yViolations(dr);

  // Nurse Reyes lands on her dashboard, opens the Ward 3 census, opens the stay, records vitals and gives the dose.
  const reyesPage = await signInAs(browser, reyes);
  await expect(reyesPage.getByRole('region', { name: 'My wards' })).toContainText(ward3Code);
  await expectNoSeriousA11yViolations(reyesPage);
  await reyesPage.getByRole('link', { name: 'Open ward census' }).click();
  const board = reyesPage.getByRole('region', { name: new RegExp(`^${ward3Code}`) });
  await expect(board.locator('ui-bed-tile').filter({ hasText: `${father}, Juan` })).toContainText(
    'Bed 12',
  );
  await expectNoSeriousA11yViolations(reyesPage);
  await reyesPage.getByRole('link', { name: `${father}, Juan` }).click();
  await expect(reyesPage.getByRole('heading', { name: `${father}, Juan`, level: 1 })).toBeVisible();
  await expectNoSeriousA11yViolations(reyesPage);

  await reyesPage.getByRole('tab', { name: 'Vitals' }).click();
  const reyesVitals = reyesPage.getByRole('tabpanel', { name: 'Vitals' });
  await reyesVitals.getByRole('button', { name: 'Record vital signs' }).click();
  const reyesForm = reyesPage.getByRole('dialog');
  await reyesForm.getByLabel('Temperature (°C)').fill('37.6');
  await reyesForm.getByLabel('Heart rate (bpm)').fill('88');
  await reyesForm.getByLabel('SpO₂ (%)').fill('96');
  await expectNoSeriousA11yViolations(reyesPage);
  await reyesForm.getByRole('button', { name: 'Save vital signs' }).click();
  await expect(reyesPage.getByText('Vital signs recorded')).toBeVisible();
  await expect(reyesVitals.getByRole('table').getByRole('row')).toHaveCount(2);
  await expectNoSeriousA11yViolations(reyesPage);

  await reyesPage.getByRole('tab', { name: 'MAR', exact: true }).click();
  const mar = reyesPage.getByRole('tabpanel', { name: 'MAR', exact: true });
  await expect(mar.getByRole('heading', { name: /^Ceftriaxone/ })).toBeVisible();
  await expectNoSeriousA11yViolations(reyesPage);
  await mar.getByRole('button', { name: 'Record dose of Ceftriaxone' }).click();
  const dose = reyesPage.getByRole('dialog');
  await expect(dose.getByRole('textbox', { name: /^Dose given\b/ })).toHaveValue('1 g');
  await expectNoSeriousA11yViolations(reyesPage);
  const allergyCheck = dose.getByRole('checkbox', { name: /I checked the allergies/ });
  if (await allergyCheck.isVisible()) {
    await allergyCheck.click();
  }
  await dose.getByRole('button', { name: 'Record dose' }).click();
  await expect(reyesPage.getByText('Dose recorded', { exact: true })).toBeVisible();
  await expect(mar.getByRole('list', { name: 'Doses of Ceftriaxone, newest first' })).toContainText(
    'Given',
  );
  await expectNoSeriousA11yViolations(reyesPage);

  // ---- 8. Nurse Reyes asks to swap shifts; Ben accepts; the Head Nurse approves ----
  await apiPost(page, '/api/workforce/roster/entries', {
    employeeId: reyes.employeeId,
    wardId: ward3.id,
    shiftId: am.id,
    dutyDate: reyesDay,
  });
  await reyesPage.goto('/workforce/swaps');
  await expectNoSeriousA11yViolations(reyesPage);
  await reyesPage.getByRole('button', { name: 'Request a swap' }).click();
  const swap = reyesPage.getByRole('dialog');
  await swap.getByRole('combobox', { name: /^Your shift/ }).click();
  await reyesPage
    .getByRole('option', { name: new RegExp(`${longDay(reyesDay)} · ${amCode}`) })
    .click();
  await swap.getByRole('combobox', { name: /^Shift you'd take/ }).click();
  await reyesPage
    .getByRole('option', {
      name: new RegExp(`Ben Cruz${tag} · ${longDay(benDay)} · ${pmCode}`),
    })
    .click();
  await expect(reyesPage.getByRole('listbox')).toHaveCount(0);
  await swap.getByLabel('Message').fill('Family event');
  await expectNoSeriousA11yViolations(reyesPage);
  await swap.getByRole('button', { name: 'Send request' }).click();
  await expect(reyesPage.getByText('Swap requested')).toBeVisible();
  await expect(reyesPage.getByRole('article').first()).toContainText('Awaiting colleague');
  await expectNoSeriousA11yViolations(reyesPage);

  const benPage = await signInAs(browser, ben);
  await benPage.goto('/workforce/swaps');
  const waiting = benPage.getByRole('region', { name: 'Waiting for your answer' });
  await expect(waiting.getByRole('article')).toContainText('Family event');
  await expectNoSeriousA11yViolations(benPage);
  await waiting.getByRole('button', { name: 'Accept' }).click();
  await expect(benPage.getByText('Swap accepted')).toBeVisible();
  await expectNoSeriousA11yViolations(benPage);
  await benPage.context().close();

  const headPage = await signInAs(browser, head3);
  await headPage.goto('/workforce/swaps/review');
  const card = headPage.getByRole('article').filter({ hasText: `Rosa Reyes${tag}` });
  await expect(card).toContainText('Awaiting approval');
  await expectNoSeriousA11yViolations(headPage);
  await card.getByRole('button', { name: 'Approve' }).click();
  await expect(headPage.getByText('Swap approved')).toBeVisible();
  await expectNoSeriousA11yViolations(headPage);

  // ---- 9. The Head Nurse transfers him to Ward 5; the nurses' access follows ----
  await headPage.goto(stayUrl);
  await expect(headPage.getByText(`${ward3Code} · Bed 12`).first()).toBeVisible();
  await expectNoSeriousA11yViolations(headPage);
  await headPage.getByRole('button', { name: 'Transfer' }).click();
  const transfer = headPage.getByRole('dialog', { name: 'Transfer patient' });
  const ward5Beds = transfer.getByRole('group', { name: new RegExp(`^${ward5Code}`) });
  await expect(ward5Beds).toContainText('1 free bed');
  await ward5Beds.getByText('Bed 5', { exact: true }).click();
  await transfer.getByLabel('Reason for transfer').fill('Needs cardiac monitoring');
  await expectNoSeriousA11yViolations(headPage);
  await transfer.getByRole('button', { name: 'Transfer patient' }).click();
  await expect(headPage).toHaveURL(/\/clinical\/census$/);
  await expect(headPage.getByText("They're no longer on your wards")).toBeVisible();
  await expectNoSeriousA11yViolations(headPage);
  await headPage.context().close();

  // Nurse Reyes, still on Ward 3, can't reach him any more.
  await reyesPage.goto('/clinical/census');
  await expect(reyesPage.getByText(father)).toHaveCount(0);
  await expectNoSeriousA11yViolations(reyesPage);
  await reyesPage.goto(stayUrl);
  await expect(reyesPage.getByRole('heading', { name: 'Encounter not found' })).toBeVisible();
  await expectNoSeriousA11yViolations(reyesPage);
  await reyesPage.context().close();

  // The Ward 5 nurse has him, with both beds in his history.
  const nurse5Page = await signInAs(browser, nurse5);
  await nurse5Page.getByRole('link', { name: 'Open ward census' }).click();
  await expect(nurse5Page.getByRole('link', { name: `${father}, Juan` })).toBeVisible();
  await expectNoSeriousA11yViolations(nurse5Page);
  await nurse5Page.getByRole('link', { name: `${father}, Juan` }).click();
  await expect(nurse5Page.getByText(`${ward5Code} · Bed 5`).first()).toBeVisible();
  const history = nurse5Page.getByRole('list', { name: 'Bed history' });
  await expect(history.getByRole('listitem')).toHaveCount(2);
  await expect(history).toContainText('Needs cardiac monitoring');
  await expectNoSeriousA11yViolations(nurse5Page);
  await nurse5Page.context().close();

  // ---- 10. Dr. Santos discharges him; his daughter reads the summary ----
  await dr.goto(stayUrl);
  await dr.getByRole('button', { name: 'Discharge' }).click();
  const discharge = dr.getByRole('dialog', { name: 'Discharge patient' });
  await discharge.getByRole('combobox', { name: /^Disposition/ }).click();
  await dr.getByRole('option', { name: 'Home' }).click();
  await discharge.getByLabel('Discharge summary').fill(SUMMARY);
  await expectNoSeriousA11yViolations(dr);
  await discharge.getByRole('button', { name: 'Discharge patient' }).click();
  await expect(discharge).toHaveCount(0);
  await expect(dr.getByText('Ward stay · Discharged')).toBeVisible();
  await expectNoSeriousA11yViolations(dr);
  await dr.context().close();

  await daughter.goto('/portal/patients');
  await expectNoSeriousA11yViolations(daughter);
  await daughter
    .getByRole('listitem')
    .filter({ hasText: father })
    .getByRole('link', { name: 'View records' })
    .click();
  await expect(daughter.getByRole('heading', { level: 1, name: 'Records' })).toBeVisible();
  const portalVisits = daughter.getByRole('list', { name: 'Visits' });
  await expect(portalVisits).toContainText('Ward stay');
  await expect(portalVisits).toContainText(SUMMARY);
  await expect(daughter.getByRole('list', { name: 'Vital signs' })).toContainText('38.4');
  await expectNoSeriousA11yViolations(daughter);
  await daughter.context().close();
});
