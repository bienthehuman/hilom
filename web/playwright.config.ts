import { defineConfig, devices } from '@playwright/test';

import { ADMIN_STORAGE_STATE } from './e2e/support/accounts';

// Their own ports, so end-to-end runs never touch the servers (or the database) you develop against.
const webPort = 4300;
const apiPort = 5180;

/**
 * End-to-end tests against the real API and database. Every page visited is also checked with axe
 * (docs/DESIGN_SYSTEM.md §8). Locally, start the database first: `docker compose up -d`.
 *
 * Playwright starts its own API (port 5180) against its own database, `hilom_e2e`, which the API drops
 * and recreates on startup, so every run starts clean and leaves the development database alone. The
 * dev server (port 4300) proxies to that API. Mail still goes to the shared Mailpit.
 */
export default defineConfig({
  testDir: './e2e',
  // Needs the seeded demo hospital and its logins (playwright.milestone.config.ts), not this clean database.
  testIgnore: /milestones\/phase-4\.spec\.ts/,
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
      // Development mode applies migrations and seeds the first System Administrator. Always a new process,
      // so the database is always reset (CI's explicit connection string points at its own fresh database).
      command: `dotnet run --project ../api/src/Hilom.Api --launch-profile http -- --urls http://localhost:${apiPort}`,
      url: `http://localhost:${apiPort}/health/ready`,
      env: {
        HILOM_DB_NAME: 'hilom_e2e',
        HILOM_DB_RESET_ON_START: 'true',
        // Links in emails (staff invites) open this run's dev server.
        Email__AppUrl: `http://localhost:${webPort}`,
      },
      reuseExistingServer: false,
      timeout: 180_000,
      stdout: 'ignore',
    },
    {
      command: `pnpm exec ng serve --port ${webPort} --proxy-config src/proxy.e2e.conf.json`,
      url: `http://localhost:${webPort}`,
      reuseExistingServer: !process.env['CI'],
      timeout: 120_000,
    },
  ],
});
