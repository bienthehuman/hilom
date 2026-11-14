import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { expect, test, type Browser, type Page } from '@playwright/test';
import { GIFEncoder, applyPalette, quantize } from 'gifenc';
import { PNG } from 'pngjs';

/**
 * The README's screenshots and tour GIF, taken from the seeded demo hospital (playwright.showcase.config.ts).
 * Every sign-in goes through the "Try the demo" buttons on the sign-in pages, as a visitor would.
 */

const OUT = resolve(__dirname, '../../docs/images');
mkdirSync(OUT, { recursive: true });

test.describe.configure({ mode: 'serial' });

type Scheme = 'light' | 'dark';

async function signInAsDemo(
  browser: Browser,
  role: string,
  options: { scheme?: Scheme; viewport?: { width: number; height: number }; portal?: boolean } = {},
): Promise<Page> {
  const context = await browser.newContext({
    colorScheme: options.scheme ?? 'light',
    viewport: options.viewport ?? { width: 1280, height: 800 },
  });
  const page = await context.newPage();
  await page.goto(options.portal ? '/portal/login' : '/login');
  await page.getByRole('button', { name: new RegExp(`^${role}`) }).click();
  await expect(page).not.toHaveURL(/login/);
  await settle(page);
  return page;
}

/** Waits for data to load and animations to finish, so a shot never shows skeletons or a half-open dialog. */
async function settle(page: Page): Promise<void> {
  await page.waitForLoadState('networkidle');
  await expect(page.locator('[aria-busy="true"]')).toHaveCount(0);
  await page.waitForTimeout(400);
}

async function shot(page: Page, name: string): Promise<void> {
  await settle(page);
  await page.screenshot({ path: `${OUT}/${name}.png` });
}

const nav = (page: Page, name: string) =>
  page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name, exact: true });

/** The first patient on the census board whose chart this user may open. */
const firstChart = (page: Page) => page.locator('main a[href*="/clinical/encounters/"]').first();

/** Frames for the tour GIF: a screenshot plus how long it stays up. */
class Tour {
  private readonly frames: { png: Buffer; delay: number }[] = [];

  async frame(page: Page, delay = 1800): Promise<void> {
    await settle(page);
    this.frames.push({ png: await page.screenshot(), delay });
  }

  write(path: string): void {
    const gif = GIFEncoder();
    for (const { png, delay } of this.frames) {
      const { data, width, height } = PNG.sync.read(png);
      const palette = quantize(data, 256);
      gif.writeFrame(applyPalette(data, palette), width, height, { palette, delay });
    }
    gif.finish();
    writeFileSync(path, gif.bytes());
  }
}

test('physician dashboard', async ({ browser }) => {
  const page = await signInAsDemo(browser, 'Physician');
  await shot(page, 'dashboard-physician');
});

test('ward census and a patient chart, light and dark', async ({ browser }) => {
  for (const scheme of ['light', 'dark'] as const) {
    const page = await signInAsDemo(browser, 'Staff Nurse', { scheme });
    await nav(page, 'Ward census').click();
    await shot(page, `census-${scheme}`);
    if (scheme === 'light') {
      await firstChart(page).click();
      await expect(page).toHaveURL(/encounters\//);
      await page.getByRole('tab', { name: 'Vitals', exact: true }).click();
      await shot(page, 'encounter-vitals');
    }
    await page.context().close();
  }
});

test('duty roster', async ({ browser }) => {
  const page = await signInAsDemo(browser, 'Head Nurse');
  await nav(page, 'Duty roster').click();
  await shot(page, 'roster');
});

test('role editor with scoped permissions, and the audit log', async ({ browser }) => {
  const page = await signInAsDemo(browser, 'System Administrator');
  await nav(page, 'Roles').click();
  await page.getByRole('link', { name: /^Staff Nurse/ }).click();
  await expect(page).toHaveURL(/admin\/roles\/[0-9a-f-]+$/);
  // The clinical grants, where the ward scope shows: a nurse reaches the patients on her wards only.
  await settle(page);
  await page
    .locator('main')
    .getByText('Clinical', { exact: true })
    .evaluate((el) => el.scrollIntoView({ block: 'start' }));
  // Clear of the sticky top bar.
  await page.mouse.wheel(0, -72);
  await shot(page, 'role-editor');
  // Notice and critical events: transfers, discharges, role changes, refused access. Not the capture's own sign-ins.
  await page.goto('/admin/audit?severity=notice');
  await shot(page, 'audit-log');
});

test('patient portal on a phone', async ({ browser }) => {
  const page = await signInAsDemo(browser, 'Patient Portal User', {
    portal: true,
    viewport: { width: 390, height: 844 },
  });
  // The portal opens on Appointments; records are under Patients.
  await page
    .getByRole('navigation', { name: 'Portal' })
    .getByRole('link', { name: 'Patients' })
    .click();
  await page
    .getByRole('link', { name: /records/i })
    .first()
    .click();
  await shot(page, 'portal-records');
});

test('tour GIF: a nurse signs in, opens the census and charts a patient', async ({ browser }) => {
  const tour = new Tour();
  const context = await browser.newContext({ viewport: { width: 1024, height: 640 } });
  const page = await context.newPage();

  await page.goto('/login');
  await expect(page.getByRole('heading', { name: 'Try the demo' })).toBeVisible();
  await tour.frame(page, 2200);
  await page.getByRole('button', { name: /^Staff Nurse/ }).click();
  await expect(page).not.toHaveURL(/login/);
  await tour.frame(page, 2600);
  await nav(page, 'Ward census').click();
  await tour.frame(page, 2600);
  await firstChart(page).click();
  await tour.frame(page, 2600);
  for (const tab of ['Vitals', 'MAR']) {
    await page.getByRole('tab', { name: tab, exact: true }).click();
    await tour.frame(page, 2800);
  }

  tour.write(`${OUT}/tour.gif`);
});
