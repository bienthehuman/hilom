import { expect, test, type Locator, type Page } from '@playwright/test';

import { expectNoSeriousA11yViolations } from '../support/accessibility';

/**
 * Phase 4 demo script (docs/BUILD_PLAN.md §"Phase 4: Polish + ship"): "a visitor runs the app locally, picks
 * a role from the login page, and can try every part of the system." Run against the seeded demo hospital
 * (`pnpm exec playwright test -c playwright.milestone.config.ts`), not the plain `pnpm e2e` database, since
 * it needs the demo logins docs/adr/0009-demo-data-through-the-domain.md describes.
 *
 * One visitor, one browser tab: for each demo role in turn, they open the sign-in page, pick that role (the
 * "Try the demo" buttons from DemoAccountCatalog), try the parts of the system README.md's "Try it" table
 * promises for that role, and sign out before picking the next one. Every page they land on is checked with
 * axe. The portal account is tried last, from its own sign-in page.
 *
 * Fixed facts this relies on, all pinned by the seeder rather than randomised (api/src/Hilom.DemoData):
 * - Ward "W3" is always the medical ward the demo nurses work on (DemoCatalog.Wards).
 * - The demo Physician, Staff Nurse and Head Nurse are always on W3 (StaffGenerator).
 * - Every demo account's display name (StaffGenerator, PatientGenerator).
 * - The featured family, Antonio Mercado and his daughter Liza, are always linked with booking and
 *   records access (PatientGenerator.CreateFeaturedFamilyAsync).
 */

const DISPLAY_NAME = {
  physician: 'Dr. Elena Santos',
  staffNurse: 'Maria Reyes',
  headNurse: 'Ramon Dizon',
  chiefNurse: 'Teresita Villanueva',
  frontDesk: 'Joy Bautista',
  hr: 'Lourdes Navarro',
  admin: 'Alex Rivera',
  portal: 'Liza Mercado',
} as const;

const WARD3 = 'W3';

const sidebarLink = (page: Page, name: string) =>
  page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name, exact: true });

async function choose(page: Page, label: string | RegExp, option: string): Promise<void> {
  await page.getByRole('combobox', { name: label }).click();
  await page.getByRole('option', { name: option, exact: true }).click();
  await expect(page.getByRole('listbox')).toHaveCount(0);
}

/** From a sign-in page (staff or portal), picks the demo role's one-click login. */
async function demoSignIn(page: Page, role: string): Promise<void> {
  await expect(page.getByRole('heading', { name: 'Try the demo' })).toBeVisible();
  await expectNoSeriousA11yViolations(page);
  await page.getByRole('button', { name: new RegExp(`^${role}\\b`) }).click();
}

/** Signs a staff member out through the header menu, back to the sign-in page. */
async function signOutStaff(page: Page, displayName: string): Promise<void> {
  await page.getByRole('button', { name: displayName }).click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/login/);
}

test('Phase 4: a visitor picks every demo role from the login page and tries their part of the system', async ({
  page,
}) => {
  test.setTimeout(600_000);

  // ---- Sign-in page, signed out ----
  await page.goto('/login');
  await expect(page.getByRole('heading', { level: 1, name: 'Sign in to Hilom' })).toBeVisible();
  await expectNoSeriousA11yViolations(page);

  // ==== Physician: Dr. Elena Santos ====
  await demoSignIn(page, 'Physician');
  await expect(page.getByRole('heading', { level: 1 })).toContainText(DISPLAY_NAME.physician);
  await expect(page.getByRole('region', { name: "Today's appointments" })).toBeVisible();
  await expect(page.getByRole('region', { name: 'My inpatients' })).toBeVisible();
  await expectNoSeriousA11yViolations(page);

  await sidebarLink(page, 'Encounters').click();
  await expect(page.getByRole('heading', { level: 1, name: 'Encounters' })).toBeVisible();
  await expect(page.getByRole('table', { name: 'Encounters' })).toBeVisible();
  await expectNoSeriousA11yViolations(page);

  // Publishes a fresh week, far enough out that the seed's own publishing (14 days ahead) can't collide.
  await sidebarLink(page, 'Availability').click();
  await expect(page.getByRole('heading', { level: 1, name: 'Availability' })).toBeVisible();
  await expectNoSeriousA11yViolations(page);
  for (let i = 0; i < 3; i++) {
    await page.getByRole('button', { name: 'Next week' }).click();
  }
  await page.getByRole('button', { name: 'Publish slots' }).click();
  const publish = page.getByRole('dialog');
  await publish.locator('#publish-start').fill('09:00');
  await publish.locator('#publish-end').fill('10:00');
  await expectNoSeriousA11yViolations(page);
  await publish.getByRole('button', { name: 'Publish', exact: true }).click();
  await expect(publish).toHaveCount(0);
  await expect(page.getByText(/^\d+ open slots this week\.$/)).toBeVisible();
  await expectNoSeriousA11yViolations(page);

  await signOutStaff(page, DISPLAY_NAME.physician);

  // ==== Staff Nurse: Maria Reyes, Ward 3 ====
  await demoSignIn(page, 'Staff Nurse');
  await expect(page.getByRole('heading', { level: 1 })).toContainText(DISPLAY_NAME.staffNurse);
  const myWards = page.getByRole('region', { name: 'My wards' });
  await expect(myWards).toContainText(WARD3);
  await expect(page.getByRole('region', { name: 'Swap inbox' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'My shifts' })).toBeVisible();
  await expectNoSeriousA11yViolations(page);

  await myWards.getByRole('link', { name: 'Open ward census' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Ward census' })).toBeVisible();
  const board = page.getByRole('region', { name: new RegExp(`^${WARD3}`) });
  await expectNoSeriousA11yViolations(page);

  // Charts whoever's actually occupying a bed right now, if anyone is: the census is seeded, not scripted.
  const occupiedTile = board.locator('ui-bed-tile').filter({ hasNotText: 'Free' }).first();
  if (await occupiedTile.count()) {
    await occupiedTile.getByRole('link').click();
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expectNoSeriousA11yViolations(page);
    await page.getByRole('tab', { name: 'Vitals' }).click();
    await page
      .getByRole('tabpanel', { name: 'Vitals' })
      .getByRole('button', { name: 'Record vital signs' })
      .click();
    const vitals = page.getByRole('dialog');
    await vitals.getByLabel('Temperature (°C)').fill('37.2');
    await vitals.getByLabel('Heart rate (bpm)').fill('82');
    await vitals.getByLabel('SpO₂ (%)').fill('97');
    await expectNoSeriousA11yViolations(page);
    await vitals.getByRole('button', { name: 'Save vital signs' }).click();
    await expect(page.getByText('Vital signs recorded')).toBeVisible();
    await expectNoSeriousA11yViolations(page);
    await page.goBack();
  }

  await sidebarLink(page, 'Shift swaps').click();
  await expect(page.getByRole('heading', { level: 1, name: 'Shift swaps' })).toBeVisible();
  await expectNoSeriousA11yViolations(page);
  const waitingOnMe = page.getByRole('region', { name: 'Waiting for your answer' });
  const swapCard = waitingOnMe.getByRole('article').first();
  if (await swapCard.count()) {
    await swapCard.getByRole('button', { name: 'Accept' }).click();
    await expect(page.getByText('Swap accepted')).toBeVisible();
    await expectNoSeriousA11yViolations(page);
  }

  await signOutStaff(page, DISPLAY_NAME.staffNurse);

  // ==== Head Nurse: Ramon Dizon, Ward 3 ====
  await demoSignIn(page, 'Head Nurse');
  await expect(page.getByRole('heading', { level: 1 })).toContainText(DISPLAY_NAME.headNurse);
  await expectNoSeriousA11yViolations(page);

  await sidebarLink(page, 'Duty roster').click();
  await expect(page.getByRole('heading', { level: 1, name: 'Duty roster' })).toBeVisible();
  // Only his own ward is in scope: either no picker, or a picker offering just W3.
  const wardPicker = page.getByRole('combobox', { name: /^Ward/ });
  if (await wardPicker.isVisible()) {
    await expect(wardPicker).toContainText(WARD3);
  }
  await expectNoSeriousA11yViolations(page);

  await sidebarLink(page, 'Swap approvals').click();
  await expect(page.getByRole('heading', { level: 1, name: 'Swap approvals' })).toBeVisible();
  await expectNoSeriousA11yViolations(page);
  const approvalCard = page.getByRole('article').first();
  if (await approvalCard.count()) {
    await approvalCard.getByRole('button', { name: 'Approve' }).click();
    await expect(page.getByText('Swap approved')).toBeVisible();
    await expectNoSeriousA11yViolations(page);
  }

  await signOutStaff(page, DISPLAY_NAME.headNurse);

  // ==== Chief Nurse: Teresita Villanueva ====
  await demoSignIn(page, 'Chief Nurse');
  await expect(page.getByRole('heading', { level: 1 })).toContainText(DISPLAY_NAME.chiefNurse);
  await expect(page.getByRole('region', { name: 'Ward occupancy' })).toContainText(WARD3);
  await expect(page.getByRole('region', { name: 'Staffing today' })).toBeVisible();
  await expectNoSeriousA11yViolations(page);

  await sidebarLink(page, 'Employees').click();
  await expect(page.getByRole('heading', { level: 1, name: 'Employees' })).toBeVisible();
  await page.getByLabel('Search', { exact: true }).fill('Reyes');
  await expectNoSeriousA11yViolations(page);

  // The demo's own name pool can coincidentally repeat "Reyes, Maria" for an unrelated random hire; the
  // one actually assigned to Ward 3 is the one the tour means, so look that up rather than guess by order.
  const wards = (await (await page.request.get('/api/facility/wards?q=W3')).json()) as {
    items: { id: string; code: string }[];
  };
  const ward3Id = wards.items.find((w) => w.code === WARD3)!.id;
  const assignments = (await (
    await page.request.get(`/api/workforce/ward-assignments?wardId=${ward3Id}&pageSize=100`)
  ).json()) as { items: { employee: { id: string } }[] };
  const ward3Employees = new Set(assignments.items.map((a) => a.employee.id));
  const candidates = page.getByRole('link', { name: /^Reyes, Maria\b/ });
  let maria: Locator | null = null;
  for (let i = 0; i < (await candidates.count()); i++) {
    const href = await candidates.nth(i).getAttribute('href');
    if (href && ward3Employees.has(href.split('/').pop()!)) {
      maria = candidates.nth(i);
      break;
    }
  }
  expect(maria, 'the Ward 3 Maria Reyes, among the employees named that').not.toBeNull();
  await maria!.click();
  await expect(
    page.getByRole('heading', { level: 1, name: DISPLAY_NAME.staffNurse }),
  ).toBeVisible();
  await expect(page.getByRole('region', { name: 'Ward assignment' })).toContainText(WARD3);
  await expectNoSeriousA11yViolations(page);

  await signOutStaff(page, DISPLAY_NAME.chiefNurse);

  // ==== Front Desk: Joy Bautista ====
  await demoSignIn(page, 'Front Desk');
  await expect(page.getByRole('heading', { level: 1 })).toContainText(DISPLAY_NAME.frontDesk);
  await expect(page.getByRole('region', { name: 'Day list' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Pending link requests' })).toBeVisible();
  await expectNoSeriousA11yViolations(page);

  await sidebarLink(page, 'Link requests').click();
  await expect(page.getByRole('heading', { level: 1, name: 'Link requests' })).toBeVisible();
  await expectNoSeriousA11yViolations(page);

  const tag = `Milestone${Date.now().toString(36)}${Math.floor(Math.random() * 1e4)}`;
  const walkInLastName = `DelaCruz${tag}`;
  await sidebarLink(page, 'Patients').click();
  await expect(page.getByRole('heading', { level: 1, name: 'Patients' })).toBeVisible();
  await page.getByRole('link', { name: 'Register patient' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Register patient' })).toBeVisible();
  await page.getByLabel('First name').fill('Juan');
  await page.getByLabel('Last name').fill(walkInLastName);
  await page.getByLabel('Date of birth').fill('1958-03-21');
  await choose(page, /^Sex/, 'Male');
  await choose(page, /^Civil status/, 'Married');
  await page.getByLabel('Home address').fill('12 Mabini St., Marikina City');
  await page.locator('#contact-number').fill('0917 555 0199');
  await page.locator('#emergency-name').fill('Maria Dela Cruz');
  await page.getByLabel('Relationship').fill('Daughter');
  await page.locator('#emergency-number').fill('0918 555 0123');
  await expectNoSeriousA11yViolations(page);
  await page.getByRole('button', { name: 'Register patient' }).click();
  await expect(
    page.getByRole('heading', { level: 1, name: `${walkInLastName}, Juan` }),
  ).toBeVisible();
  await expectNoSeriousA11yViolations(page);

  await signOutStaff(page, DISPLAY_NAME.frontDesk);

  // ==== HR Officer: Lourdes Navarro ====
  await demoSignIn(page, 'HR Officer');
  await expect(page.getByRole('heading', { level: 1 })).toContainText(DISPLAY_NAME.hr);
  await expectNoSeriousA11yViolations(page);

  const hireLastName = `Fernandez${tag}`;
  const hireEmail = `ana.${tag.toLowerCase()}@hilom.test`;
  await sidebarLink(page, 'Employees').click();
  await page.getByRole('link', { name: 'New employee' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'New employee' })).toBeVisible();
  await page.getByLabel('First name').fill('Ana');
  await page.getByLabel('Last name').fill(hireLastName);
  await choose(page, /^Sex/, 'Female');
  await page.getByLabel('Date of birth').fill('1995-04-12');
  await choose(page, /^Department/, 'Nursing Administration');
  await choose(page, /^Position/, 'Staff Nurse');
  await choose(page, /^Professional type/, 'Nurse');
  await page.getByLabel('License number').fill('0456789');
  await page.getByLabel('Hired on').fill('2024-06-01');
  await expectNoSeriousA11yViolations(page);
  await page.getByRole('button', { name: 'Add employee' }).click();
  await expect(page.getByRole('heading', { level: 1, name: `Ana ${hireLastName}` })).toBeVisible();
  // The "Employee added" toast pauses while hovered, and the pointer is still where "Add employee" was.
  await page.mouse.move(0, 0);
  await expect(page.locator('[data-sonner-toast]')).toHaveCount(0);
  await expectNoSeriousA11yViolations(page);

  const access = page.getByRole('region', { name: 'Sign-in access' });
  await access.getByRole('button', { name: 'Send invite' }).click();
  const inviteDialog = page.getByRole('dialog');
  await inviteDialog.getByLabel('Email').fill(hireEmail);
  await inviteDialog.getByRole('checkbox', { name: /^Staff Nurse/ }).click();
  await expectNoSeriousA11yViolations(page);
  await inviteDialog.getByRole('button', { name: 'Send invite' }).click();
  await expect(access).toContainText('Waiting');
  await expectNoSeriousA11yViolations(page);

  await signOutStaff(page, DISPLAY_NAME.hr);

  // ==== System Administrator: Alex Rivera ====
  await demoSignIn(page, 'System Administrator');
  await expect(page.getByRole('heading', { level: 1 })).toContainText(DISPLAY_NAME.admin);
  await expect(page.getByRole('region', { name: 'Critical audit events' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Users' })).toBeVisible();
  await expectNoSeriousA11yViolations(page);

  await sidebarLink(page, 'Roles').click();
  await expect(page.getByRole('heading', { level: 1, name: 'Roles' })).toBeVisible();
  await expectNoSeriousA11yViolations(page);
  await page.getByRole('link', { name: /^Staff Nurse\b/ }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Staff Nurse' })).toBeVisible();
  await expectNoSeriousA11yViolations(page);

  await sidebarLink(page, 'Audit log').click();
  await expect(page.getByRole('heading', { level: 1, name: 'Audit log' })).toBeVisible();
  await expectNoSeriousA11yViolations(page);
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export CSV' }).click();
  const file = await download;
  expect(file.suggestedFilename()).toMatch(/^audit-log-\d{4}-\d{2}-\d{2}\.csv$/);
  await page.getByRole('button', { name: 'Check integrity' }).click();
  await expect(page.getByRole('heading', { name: 'The log is intact' })).toBeVisible();
  await expectNoSeriousA11yViolations(page);

  await signOutStaff(page, DISPLAY_NAME.admin);

  // ==== Patient Portal User: Liza Mercado, from the portal's own sign-in page ====
  await page.goto('/portal/login');
  await expect(
    page.getByRole('heading', { level: 1, name: 'Sign in to the patient portal' }),
  ).toBeVisible();
  await expectNoSeriousA11yViolations(page);
  await demoSignIn(page, 'Patient Portal User');
  await expect(page).toHaveURL(/\/portal\/patients$/);
  const linked = page.getByRole('list', { name: 'Linked patients' });
  await expect(linked).toContainText('Mercado');
  await expectNoSeriousA11yViolations(page);

  await linked
    .getByRole('listitem')
    .filter({ hasText: 'Mercado' })
    .getByRole('link', { name: 'View records' })
    .click();
  await expect(page.getByRole('heading', { level: 1, name: 'Records' })).toBeVisible();
  await expectNoSeriousA11yViolations(page);

  const portalNav = page.getByRole('navigation', { name: 'Portal' });
  await portalNav.getByRole('link', { name: 'Appointments' }).click();
  await expectNoSeriousA11yViolations(page);

  await portalNav.getByRole('link', { name: 'Account' }).click();
  await expect(page.getByText('portal@demo.hilom.test')).toBeVisible();
  await expectNoSeriousA11yViolations(page);
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/portal\/login$/);
});
