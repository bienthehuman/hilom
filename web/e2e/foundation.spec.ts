import { expect, test } from '@playwright/test';

import { expectNoSeriousA11yViolations } from './support/accessibility';

test.describe('foundation page', () => {
  test('renders the product name and tagline', async ({ page }) => {
    await page.goto('/');

    await expect(page.getByRole('heading', { level: 1, name: 'Hilom' })).toBeVisible();
    await expect(page.getByText('Hilom at Aruga')).toBeVisible();
  });

  test('follows the system color scheme', async ({ page }, testInfo) => {
    await page.goto('/');

    const isDark = await page.evaluate(() => document.documentElement.classList.contains('dark'));
    expect(isDark).toBe(testInfo.project.name === 'dark');
  });

  test('uses the Inter typeface', async ({ page }) => {
    await page.goto('/');

    const fontFamily = await page.evaluate(() => getComputedStyle(document.body).fontFamily);
    expect(fontFamily).toContain('Inter Variable');
  });

  test('has no serious accessibility violations', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    await expectNoSeriousA11yViolations(page);
  });
});
