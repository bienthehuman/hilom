import { expect, test } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';
import { apiPost } from './support/staff';

test('front desk publishes a week of slots for a physician and closes one', async ({ page }) => {
  const tag = `${test.info().project.name}${Date.now()}`;
  const lastName = `Santos${tag}`;
  const specialization = `Cardiology ${tag}`;

  // A physician, made through the API the way HR would.
  await page.goto('/');
  const department = await apiPost(page, '/api/workforce/departments', { name: `Medicine ${tag}` });
  const position = await apiPost(page, '/api/workforce/positions', { name: `Consultant ${tag}` });
  const spec = await apiPost(page, '/api/workforce/specializations', { name: specialization });
  await apiPost(page, '/api/workforce/employees', {
    firstName: 'Jose',
    lastName,
    sex: 'male',
    birthDate: '1980-02-14',
    departmentId: department.id,
    positionId: position.id,
    professionalType: 'physician',
    licenseNumber: '0123456',
    employmentType: 'full_time',
    hiredOn: '2015-07-01',
    physician: { specializationId: spec.id, acceptsAppointments: true, consultationMinutes: 30 },
  });

  await page.goto('/scheduling/availability');
  await expect(page.getByRole('heading', { level: 1, name: 'Availability' })).toBeVisible();
  // With one physician there's no picker, just their name.
  const doctor = `Dr. Jose ${lastName} · ${specialization}`;
  const picker = page.getByRole('combobox', { name: 'Physician' });
  await expect(picker.or(page.getByText(doctor, { exact: true })).first()).toBeVisible();
  if (await picker.isVisible()) {
    await picker.click();
    await page.getByRole('option', { name: doctor }).click();
  }
  await page.getByRole('button', { name: 'Next week' }).click();
  await expectNoSeriousA11yViolations(page);

  // Next week, weekdays, 09:00–10:00 in the physician's 30-minute slots: 10 slots.
  await page.getByRole('button', { name: 'Publish slots' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByLabel('Minutes per slot')).toHaveValue('30');
  await dialog.locator('#publish-start').fill('09:00');
  await dialog.locator('#publish-end').fill('10:00');
  await expectNoSeriousA11yViolations(page);
  await dialog.getByRole('button', { name: 'Publish', exact: true }).click();
  await expect(page.getByText('Published 10 slots')).toBeVisible();

  const monday = page.getByRole('region', { name: /^Monday/ });
  await expect(monday.getByText('09:00–09:30')).toBeVisible();
  await expect(monday.getByText('09:30–10:00')).toBeVisible();
  await expect(page.getByRole('region', { name: /^Saturday/ })).toContainText('No slots');

  // Closing one withdraws it; publishing the same pattern again only fills that gap.
  await monday.getByRole('button', { name: /^Close slot .*09:00–09:30$/ }).click();
  await expect(page.getByText('Slot closed')).toBeVisible();
  await expect(monday.getByText('(closed)')).toBeAttached();

  await page.getByRole('button', { name: 'Publish slots' }).click();
  await dialog.locator('#publish-start').fill('09:00');
  await dialog.locator('#publish-end').fill('10:00');
  await dialog.getByRole('button', { name: 'Publish', exact: true }).click();
  await expect(page.getByText('Published 1 slot', { exact: true })).toBeVisible();
  await expect(page.getByText('9 skipped because they overlapped open slots.')).toBeVisible();
});
