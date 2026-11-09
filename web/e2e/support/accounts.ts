/** The development System Administrator, seeded from api/src/Hilom.Api/appsettings.Development.json. */
export const ADMIN = {
  email: process.env['E2E_ADMIN_EMAIL'] ?? 'admin@hilom.test',
  password: process.env['E2E_ADMIN_PASSWORD'] ?? 'Hilom-Dev-Admin-2026',
  displayName: 'System Administrator',
};

/** Signed-in browser state shared by tests (written by auth.setup.ts, git-ignored). */
export const ADMIN_STORAGE_STATE = 'e2e/.auth/admin.json';
