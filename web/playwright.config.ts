import { defineConfig, devices } from '@playwright/test';

import { ADMIN_STORAGE_STATE } from './e2e/support/accounts';

const webPort = 4200;
const apiPort = 5080;

/**
 * End-to-end tests against the real API and database. Every page visited is also checked with axe
 * (docs/DESIGN_SYSTEM.md §8). Locally, start the database first: `docker compose up -d`.
 * Both servers are started automatically unless they are already running.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  // Each worker runs a browser next to the API and dev server; more than a few overloads a laptop (or WSL).
  workers: process.env['CI'] ? undefined : 3,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 1 : 0,
  reporter: process.env['CI'] ? [['github'], ['html', { open: 'never' }]] : 'list',
  // CI starts the API and dev server cold; first loads compile lazy chunks.
  expect: { timeout: 10_000 },
  use: {
    baseURL: `http://localhost:${webPort}`,
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'setup', testMatch: /.*\.setup\.ts/ },
    {
      name: 'light',
      use: {
        ...devices['Desktop Chrome'],
        colorScheme: 'light',
        storageState: ADMIN_STORAGE_STATE,
      },
      dependencies: ['setup'],
    },
    {
      name: 'dark',
      use: { ...devices['Desktop Chrome'], colorScheme: 'dark', storageState: ADMIN_STORAGE_STATE },
      dependencies: ['setup'],
    },
  ],
  webServer: [
    {
      // Development mode applies migrations and seeds the first System Administrator.
      command: 'dotnet run --project ../api/src/Hilom.Api --launch-profile http',
      url: `http://localhost:${apiPort}/health/ready`,
      reuseExistingServer: !process.env['CI'],
      timeout: 180_000,
      stdout: 'ignore',
    },
    {
      command: `pnpm exec ng serve --port ${webPort}`,
      url: `http://localhost:${webPort}`,
      reuseExistingServer: !process.env['CI'],
      timeout: 120_000,
    },
  ],
});
