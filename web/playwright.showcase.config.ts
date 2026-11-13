import { defineConfig, devices } from '@playwright/test';

// Their own ports and database, so a capture never touches development or end-to-end runs.
const webPort = 4400;
const apiPort = 5280;

/**
 * Captures the README screenshots and the tour GIF from the seeded demo hospital (docs/images/). Not a test
 * suite: run it by hand with `pnpm showcase` after a UI change worth showing. Locally, start the database
 * first: `docker compose up -d`.
 *
 * The API runs in demo mode against its own database, `hilom_showcase`, which it drops and seeds again on
 * startup (Small scale, the default seed), so every capture shows the same fictional hospital.
 */
export default defineConfig({
  testDir: './showcase',
  workers: 1,
  reporter: 'list',
  timeout: 60_000,
  expect: { timeout: 15_000 },
  use: {
    baseURL: `http://localhost:${webPort}`,
    ...devices['Desktop Chrome'],
    viewport: { width: 1280, height: 800 },
    colorScheme: 'light',
  },
  webServer: [
    {
      // Built first: demo settings in the environment break the build-time OpenAPI step, so they go to the run only.
      command:
        `dotnet build ../api/src/Hilom.Api -v q && ` +
        `env HILOM_DB_NAME=hilom_showcase HILOM_DB_RESET_ON_START=true Demo__Enabled=true Demo__Scale=Small ` +
        `dotnet run --no-build --project ../api/src/Hilom.Api --launch-profile http -- --urls http://localhost:${apiPort}`,
      url: `http://localhost:${apiPort}/health/ready`,
      reuseExistingServer: true,
      // Seeding the Small hospital takes a minute or two.
      timeout: 600_000,
      stdout: 'ignore',
    },
    {
      command: `pnpm exec ng serve --port ${webPort} --proxy-config src/proxy.showcase.conf.json`,
      url: `http://localhost:${webPort}`,
      reuseExistingServer: true,
      timeout: 120_000,
    },
  ],
});
