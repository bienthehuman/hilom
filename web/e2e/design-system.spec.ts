import { expect, test } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';

test.describe('design system page', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/design');
    await expect(page.getByRole('heading', { level: 1, name: 'Design system' })).toBeVisible();
    await expect(page.getByRole('cell', { name: /MRN-/ }).first()).toBeVisible();
  });

  test('has no serious accessibility violations', async ({ page }) => {
    await expectNoSeriousA11yViolations(page);
  });

  test('has no serious accessibility violations with a dialog open', async ({ page }) => {
    await page.getByRole('button', { name: 'Dialog', exact: true }).click();
    await expect(page.getByRole('dialog')).toBeVisible();

    await expectNoSeriousA11yViolations(page);
  });

  test('shows form errors after submitting an empty form', async ({ page }) => {
    await page.getByRole('button', { name: 'Save patient' }).click();

    await expect(page.getByText('Full name is required.')).toBeVisible();
    await expect(page.getByText('Email is required.')).toBeVisible();
    await expect(page.getByText('Ward is required.')).toBeVisible();
  });

  test('sorts and pages the data table through the server', async ({ page }) => {
    const table = page.getByRole('table', { name: 'Admitted patients' });
    const patientHeader = table.getByRole('columnheader', { name: 'Patient' });

    await expect(patientHeader).toHaveAttribute('aria-sort', 'ascending');
    await patientHeader.getByRole('button').click();
    await expect(patientHeader).toHaveAttribute('aria-sort', 'descending');

    await page.getByRole('button', { name: 'Next page' }).click();
    await expect(page.getByText('Showing 11–20 of 57')).toBeVisible();
  });

  test('shows toasts from the lazily loaded toaster', async ({ page }) => {
    await page.getByRole('button', { name: 'Success toast' }).click();

    await expect(page.getByText('Appointment confirmed')).toBeVisible();
  });

  test('shows a retryable error when loading fails', async ({ page }) => {
    await page.getByLabel('Simulate error').click();
    await expect(page.getByText("Couldn't load admitted patients")).toBeVisible();

    await page.getByRole('button', { name: 'Try again' }).click();
    await expect(page.getByRole('cell', { name: /MRN-/ }).first()).toBeVisible();
  });
});

test.describe('theme toggle', () => {
  test('switches theme and remembers the choice', async ({ page }) => {
    await page.goto('/');

    await page.getByRole('button', { name: /^Theme:/ }).click();
    await page.getByRole('menuitemradio', { name: 'Dark' }).click();
    await expect(page.locator('html')).toHaveClass(/dark/);

    await page.reload();
    await expect(page.locator('html')).toHaveClass(/dark/);

    await page.getByRole('button', { name: 'Theme: Dark' }).click();
    await page.getByRole('menuitemradio', { name: 'Light' }).click();
    await expect(page.locator('html')).not.toHaveClass(/dark/);
  });
});

test.describe('interaction states', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/design');
    await expect(page.getByRole('heading', { level: 1, name: 'Design system' })).toBeVisible();
  });

  test('buttons show the pointer cursor', async ({ page }) => {
    const button = page.getByRole('button', { name: 'Save patient' });

    expect(await button.evaluate((el) => getComputedStyle(el).cursor)).toBe('pointer');
  });

  test('a focused input highlights its border without a halo', async ({ page }) => {
    const input = page.getByLabel('Full name');

    await input.focus();

    // A 1px ring flush against the border: reads as a 2px highlighted border, no spread or gap.
    // Polled because the ring animates in over ~150ms.
    const shadow = () => input.evaluate((el) => getComputedStyle(el).boxShadow);
    await expect.poll(shadow).toMatch(/0px 0px 0px 1px/);
    expect(await shadow()).not.toMatch(/0px 0px 0px 3px/);
  });
});
