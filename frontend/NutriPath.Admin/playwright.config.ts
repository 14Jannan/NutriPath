import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end tests against a running stack: API on :5092 and the portal dev server on :5173.
 * They sign in as a real administrator, so they need NUTRIPATH_E2E_EMAIL, NUTRIPATH_E2E_PASSWORD
 * and NUTRIPATH_E2E_TOTP_SECRET (see docs/admin-portal.md). Not part of CI.
 */
export default defineConfig({
  testDir: './e2e',
  timeout: 300_000,
  fullyParallel: false,
  workers: 1,
  reporter: [['list']],
  use: {
    baseURL: process.env.NUTRIPATH_E2E_URL ?? 'http://localhost:5173',
    trace: 'retain-on-failure',
    viewport: { width: 1440, height: 900 },
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } } }],
});
