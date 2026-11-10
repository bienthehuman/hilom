import { expect, test, type Page } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';

const unique = () =>
  `${test.info().project.name[0]}${Date.now().toString(36)}${Math.floor(Math.random() * 100)}`.toUpperCase();

async function apiPost(page: Page, path: string, data: object): Promise<{ id: string }> {
  const xsrf = (await page.context().cookies()).find((c) => c.name === 'XSRF-TOKEN')?.value ?? '';
  const response = await page.request.post(path, { data, headers: { 'X-XSRF-TOKEN': xsrf } });
  expect(response.status(), await response.text()).toBe(201);
  return (await response.json()) as { id: string };
}

/** YYYY-MM-DD in Manila, `offset` days from today. */
function hospitalDay(offset = 0): string {
  const now = new Date(Date.now() + offset * 86_400_000);
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Manila' }).format(now);
}

test('the chief nurse moves a nurse to another ward', async ({ page }) => {
  const id = unique();
  await page.goto('/');
  const ward3 = await apiPost(page, '/api/facility/wards', {
    code: `W3-${id}`,
    name: 'Medical',
    wardType: 'general',
  });
  await apiPost(page, '/api/facility/wards', {
    code: `W5-${id}`,
    name: 'Surgical',
    wardType: 'surgical',
  });
  const ward5Code = `W5-${id}`;
  const department = await apiPost(page, '/api/workforce/departments', { name: `Nursing ${id}` });
  const position = await apiPost(page, '/api/workforce/positions', { name: `Nurse ${id}` });
  const nurse = await apiPost(page, '/api/workforce/employees', {
    firstName: 'Ana',
    lastName: `Reyes${id}`,
    sex: 'female',
    birthDate: '1995-04-12',
    departmentId: department.id,
    positionId: position.id,
    professionalType: 'nurse',
    licenseNumber: '0456789',
    employmentType: 'full_time',
    hiredOn: '2020-06-01',
  });
  await apiPost(page, '/api/workforce/ward-assignments', {
    employeeId: nurse.id,
    wardId: ward3.id,
    startsOn: hospitalDay(-10),
  });

  await page.goto(`/workforce/employees/${nurse.id}`);
  const card = page.getByRole('region', { name: 'Ward assignment' });
  await expect(card).toContainText(`On W3-${id} · Medical`);
  await expectNoSeriousA11yViolations(page);

  // One ward at a time: assigning to Ward 5 while still on Ward 3 is refused with a clear reason.
  await card.getByRole('button', { name: 'Assign to ward' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('combobox', { name: /^Ward/ }).click();
  await page.getByRole('option', { name: `${ward5Code} · Surgical` }).click();
  // Let the options list finish closing before checking the dialog.
  await expect(page.getByRole('listbox')).toHaveCount(0);
  await expectNoSeriousA11yViolations(page);
  await dialog.getByRole('button', { name: 'Assign' }).click();
  await expect(dialog).toContainText(`already assigned to W3-${id}`);
  await dialog.getByRole('button', { name: 'Cancel' }).click();

  // End Ward 3 yesterday (the suggested date), then assign Ward 5 from today.
  await card.getByRole('button', { name: `End assignment to W3-${id}` }).click();
  await expect(page.getByLabel('Last day on the ward')).toHaveValue(hospitalDay(-1));
  await page.getByRole('button', { name: 'End assignment' }).click();
  await expect(page.getByText('Assignment ended')).toBeVisible();
  await expect(card).toContainText('Ended');

  await card.getByRole('button', { name: 'Assign to ward' }).click();
  await dialog.getByRole('combobox', { name: /^Ward/ }).click();
  await page.getByRole('option', { name: `${ward5Code} · Surgical` }).click();
  await dialog.getByLabel('Reason').fill('Rotation');
  await dialog.getByRole('button', { name: 'Assign' }).click();
  await expect(page.getByText('Assigned to ward')).toBeVisible();
  await expect(card).toContainText(`On ${ward5Code} · Surgical`);

  // The ward's page lists them as assigned today.
  await card.getByRole('link', { name: `${ward5Code} · Surgical` }).click();
  const staff = page.getByRole('region', { name: 'Assigned staff' });
  await expect(staff.getByRole('link', { name: `Reyes${id}, Ana` })).toBeVisible();
  await expectNoSeriousA11yViolations(page);
});
