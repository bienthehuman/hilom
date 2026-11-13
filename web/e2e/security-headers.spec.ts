import { expect, test } from '@playwright/test';

// The app and the API both send security headers (docs/ARCHITECTURE.md, security headers). The app's policy
// allows its own scripts plus the inline theme script by hash; if that script changes, the hash in angular.json
// must change with it, and the "runs the theme script" test below catches a stale one.

test.describe('security headers', () => {
  test('the app page forbids framing and limits what it loads', async ({ request }) => {
    const response = await request.get('/');
    const headers = response.headers();

    expect(headers['content-security-policy']).toContain("frame-ancestors 'none'");
    expect(headers['content-security-policy']).toContain("object-src 'none'");
    expect(headers['x-frame-options']).toBe('DENY');
    expect(headers['referrer-policy']).toBe('strict-origin-when-cross-origin');
    expect(headers['permissions-policy']).toContain('camera=()');
    expect(headers['x-content-type-options']).toBe('nosniff');
    expect(headers['strict-transport-security']).toBeUndefined();
  });

  test('the API sends its own locked-down policy and no HSTS locally', async ({ request }) => {
    const response = await request.get('/api/access/me');
    const headers = response.headers();

    expect(headers['content-security-policy']).toBe(
      "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
    );
    expect(headers['x-frame-options']).toBe('DENY');
    expect(headers['strict-transport-security']).toBeUndefined();
  });
});

test.describe('content security policy in the browser', () => {
  // Signed out and in dark mode, so the theme script has something to do before the app starts.
  test.use({ storageState: { cookies: [], origins: [] }, colorScheme: 'dark' });

  test('runs the inline theme script without policy violations', async ({ page }) => {
    const violations: string[] = [];
    page.on('console', (message) => {
      if (/Content Security Policy/i.test(message.text())) {
        violations.push(message.text());
      }
    });

    await page.goto('/login');

    await expect(page.getByRole('heading', { level: 1, name: 'Sign in to Hilom' })).toBeVisible();
    await expect(page.locator('html')).toHaveClass(/\bdark\b/);
    expect(violations).toEqual([]);
  });
});
