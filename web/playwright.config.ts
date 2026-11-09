import { defineConfig, devices } from '@playwright/test';

const port = 4200;

/**
 * End-to-end tests. Every page visited is also checked with axe for accessibility
 * (docs/DESIGN_SYSTEM.md §8). Starts the dev server unless one is already running.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 1 : 0,
  reporter: process.env['CI'] ? [['github'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: `http://localhost:${port}`,
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'light', use: { ...devices['Desktop Chrome'], colorScheme: 'light' } },
    { name: 'dark', use: { ...devices['Desktop Chrome'], colorScheme: 'dark' } },
  ],
  webServer: {
    command: `pnpm exec ng serve --port ${port}`,
    url: `http://localhost:${port}`,
    reuseExistingServer: !process.env['CI'],
    timeout: 120_000,
  },
});
