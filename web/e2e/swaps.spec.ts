import { expect, test } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { apiPost, createStaff, hospitalDay, mondayOf, signInAs } from './support/staff';

const longDay = (date: string) =>
  new Intl.DateTimeFormat('en-PH', {
    weekday: 'long',
    month: 'long',
    day: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(`${date}T00:00:00Z`));

test('two nurses swap shifts and the head nurse approves', async ({ page, browser }) => {
  test.setTimeout(90_000);
  const tag = `${test.info().project.name[0]}${Date.now().toString(36)}`.toUpperCase();
  await page.goto('/');

  // A ward, two shifts, three nurses on the ward, and two rostered shifts.
  const ward = await apiPost(page, '/api/facility/wards', {
    code: `SW-${tag}`,
    name: 'Swap ward',
    wardType: 'general',
  });
  const am = await apiPost(page, '/api/workforce/shifts', {
    code: `A${tag}`.slice(0, 10),
    name: 'Morning',
    startsAt: '06:00:00',
    endsAt: '14:00:00',
    color: 'am',
  });
  const pm = await apiPost(page, '/api/workforce/shifts', {
    code: `P${tag}`.slice(0, 10),
    name: 'Afternoon',
    startsAt: '14:00:00',
    endsAt: '22:00:00',
    color: 'pm',
  });
  const ana = await createStaff(page, 'Ana', `Reyes${tag}`, 'Staff Nurse');
  const ben = await createStaff(page, 'Ben', `Cruz${tag}`, 'Staff Nurse');
  const cora = await createStaff(page, 'Cora', `Lim${tag}`, 'Head Nurse');
  for (const nurse of [ana, ben, cora]) {
    await apiPost(page, '/api/workforce/ward-assignments', {
      employeeId: nurse.employeeId,
      wardId: ward.id,
      startsOn: hospitalDay(-1),
    });
  }
  const anaDay = hospitalDay(2);
  const benDay = hospitalDay(3);
  await apiPost(page, '/api/workforce/roster/entries', {
    employeeId: ana.employeeId,
    wardId: ward.id,
    shiftId: am.id,
    dutyDate: anaDay,
  });
  await apiPost(page, '/api/workforce/roster/entries', {
    employeeId: ben.employeeId,
    wardId: ward.id,
    shiftId: pm.id,
    dutyDate: benDay,
  });
  const amCode = `A${tag}`.slice(0, 10);
  const pmCode = `P${tag}`.slice(0, 10);

  // Ana offers her morning for Ben's afternoon.
  const anaPage = await signInAs(browser, ana);
  await anaPage.goto('/workforce/swaps');
  await anaPage.getByRole('button', { name: 'Request a swap' }).click();
  const dialog = anaPage.getByRole('dialog');
  await dialog.getByRole('combobox', { name: /^Your shift/ }).click();
  await anaPage.getByRole('option', { name: new RegExp(`${longDay(anaDay)} · ${amCode}`) }).click();
  await dialog.getByRole('combobox', { name: /^Shift you'd take/ }).click();
  await anaPage
    .getByRole('option', { name: new RegExp(`Ben Cruz${tag} · ${longDay(benDay)} · ${pmCode}`) })
    .click();
  await expect(anaPage.getByRole('listbox')).toHaveCount(0);
  await expectNoSeriousA11yViolations(anaPage);
  await dialog.getByLabel('Message').fill('Family event');
  await dialog.getByRole('button', { name: 'Send request' }).click();
  await expect(anaPage.getByText('Swap requested')).toBeVisible();
  await expect(anaPage.getByRole('article').first()).toContainText('Awaiting colleague');

  // Ben accepts.
  const benPage = await signInAs(browser, ben);
  await benPage.goto('/workforce/swaps');
  const waiting = benPage.getByRole('region', { name: 'Waiting for your answer' });
  await expect(waiting.getByRole('article')).toContainText('Family event');
  await expectNoSeriousA11yViolations(benPage);
  await waiting.getByRole('button', { name: 'Accept' }).click();
  await expect(benPage.getByText('Swap accepted')).toBeVisible();

  // Cora, head nurse of the ward, approves.
  const coraPage = await signInAs(browser, cora);
  await coraPage.goto('/workforce/swaps/review');
  const card = coraPage.getByRole('article').filter({ hasText: `Ana Reyes${tag}` });
  await expect(card).toContainText('Awaiting approval');
  await expectNoSeriousA11yViolations(coraPage);
  await card.getByRole('button', { name: 'Approve' }).click();
  await expect(coraPage.getByText('Swap approved')).toBeVisible();

  // Ana's schedule now has Ben's afternoon, and not her morning.
  await anaPage.goto(`/workforce/my-schedule?week=${mondayOf(benDay)}`);
  await expect(anaPage.getByRole('listitem').filter({ hasText: longDay(benDay) })).toContainText(
    pmCode,
  );
  await anaPage.goto(`/workforce/my-schedule?week=${mondayOf(anaDay)}`);
  await expect(anaPage.getByRole('listitem').filter({ hasText: longDay(anaDay) })).toContainText(
    'Off',
  );
  await expectNoSeriousA11yViolations(anaPage);

  for (const p of [anaPage, benPage, coraPage]) {
    await p.context().close();
  }
});
