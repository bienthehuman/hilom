import { defineConfig, devices } from '@playwright/test';

// Their own ports and database, so a run never touches development, end-to-end or showcase runs.
const webPort = 4500;
const apiPort = 5380;

/**
 * Runs the Phase 4 milestone script (e2e/milestones/phase-4.spec.ts) against the seeded demo hospital, the
 * way a visitor actually reaches it: the login page's "Try the demo" buttons. Not part of `pnpm e2e` — the
 * demo seed takes a minute or two and the other specs expect a clean, non-demo database.
 *
 * The API runs in demo mode against its own database, `hilom_milestone`, which it drops and seeds again on
 * startup (Tiny scale, enough nurses and physicians for every scripted role), so a run always starts from
 * the same fictional hospital. `reuseExistingServer: true` means a server already running on these ports is
 * reused rather than reseeded, which is what you want while iterating on the spec.
 */
export default defineConfig({
  testDir: './e2e',
  testMatch: /milestones\/phase-4\.spec\.ts/,
  workers: 1,
  retries: 1,
  reporter: 'list',
  timeout: 300_000,
  expect: { timeout: 15_000 },
  use: {
    baseURL: `http://localhost:${webPort}`,
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'demo', use: { ...devices['Desktop Chrome'], colorScheme: 'light' } }],
  webServer: [
    {
      // Built first: demo settings in the environment break the build-time OpenAPI step, so they go to the run only.
      command:
        `dotnet build ../api/src/Hilom.Api -v q && ` +
        `env HILOM_DB_NAME=hilom_milestone HILOM_DB_RESET_ON_START=true Demo__Enabled=true Demo__Scale=Tiny ` +
        `dotnet run --no-build --project ../api/src/Hilom.Api --launch-profile http -- --urls http://localhost:${apiPort}`,
      url: `http://localhost:${apiPort}/health/ready`,
      reuseExistingServer: true,
      // Seeding the Tiny hospital takes under a minute, but the build beforehand can take longer.
      timeout: 300_000,
      stdout: 'ignore',
    },
    {
      command: `pnpm exec ng serve --port ${webPort} --proxy-config src/proxy.milestone.conf.json`,
      url: `http://localhost:${webPort}`,
      reuseExistingServer: true,
      timeout: 120_000,
    },
  ],
});
