import AxeBuilder from '@axe-core/playwright';
import { expect, type Page } from '@playwright/test';

/** Fails on serious or critical WCAG 2.2 AA violations (docs/DESIGN_SYSTEM.md §8). */
export async function expectNoSeriousA11yViolations(page: Page): Promise<void> {
  // Let fades and slides settle first: contrast measured mid-animation (e.g. a toast fading in) is
  // lower than what anyone reads, and only shows up on slower machines like CI.
  await page
    .waitForFunction(
      () =>
        document
          .getAnimations()
          // Endless ones (skeleton pulses, spinners) never finish, so they don't count.
          .filter((animation) => animation.effect?.getTiming().iterations !== Infinity)
          .every((animation) => animation.playState !== 'running'),
      undefined,
      { timeout: 5_000 },
    )
    .catch(() => undefined);

  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();

  const serious = results.violations
    .filter((v) => v.impact === 'serious' || v.impact === 'critical')
    .map((v) => `${v.id}: ${v.help} (${v.nodes.length} element(s))`);

  expect(serious, 'Serious accessibility violations').toEqual([]);
}
