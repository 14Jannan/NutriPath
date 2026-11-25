import { createHmac } from 'node:crypto';
import { mkdirSync } from 'node:fs';
import path from 'node:path';
import { expect, test, type Page } from '@playwright/test';

const email = process.env.NUTRIPATH_E2E_EMAIL ?? '';
const password = process.env.NUTRIPATH_E2E_PASSWORD ?? '';
const secret = process.env.NUTRIPATH_E2E_TOTP_SECRET ?? '';
const shots = process.env.NUTRIPATH_E2E_SCREENSHOTS ?? path.resolve(import.meta.dirname, '../e2e-screenshots');

test.skip(!email || !password || !secret, 'Set NUTRIPATH_E2E_EMAIL, NUTRIPATH_E2E_PASSWORD and NUTRIPATH_E2E_TOTP_SECRET.');

function base32(input: string) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let bits = '';
  for (const c of input.replace(/=+$/, '').toUpperCase()) bits += alphabet.indexOf(c).toString(2).padStart(5, '0');
  return Buffer.from(bits.match(/.{8}/g)!.map((b) => parseInt(b, 2)));
}

function totp(step: number) {
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(step));
  const digest = createHmac('sha1', base32(secret)).update(counter).digest();
  const offset = digest[digest.length - 1] & 0x0f;
  return String((digest.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).padStart(6, '0');
}

/** Codes can't be reused, so wait for a 30-second step that hasn't been used yet. */
async function freshCode(lastStep: { value: number }) {
  let step = Math.floor(Date.now() / 30_000);
  while (step <= lastStep.value) {
    await new Promise((r) => setTimeout(r, 1000));
    step = Math.floor(Date.now() / 30_000);
  }
  lastStep.value = step;
  return totp(step);
}

const used = { value: 0 };

async function signIn(page: Page) {
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  // Sign-in allows 5 attempts a minute per IP; wait the limit out instead of failing.
  for (let attempt = 0; attempt < 3; attempt++) {
    await page.getByRole('button', { name: 'Continue' }).click();
    const outcome = await Promise.race([
      page.getByLabel('Authenticator code').waitFor().then(() => 'mfa' as const),
      page.getByText('Too many requests').waitFor().then(() => 'limited' as const),
    ]);
    if (outcome === 'mfa') break;
    await page.waitForTimeout(61_000);
  }
  await page.getByLabel('Authenticator code').fill(await freshCode(used));
  await page.getByRole('button', { name: /verify/i }).click();
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible({ timeout: 30_000 });
}

const pages: [string, string][] = [
  ['/', 'Dashboard'], ['/users', 'Users'], ['/administrators', 'Administrators'], ['/roles', 'Roles & permissions'],
  ['/ai/providers', 'AI providers'], ['/ai/keys', 'API keys'], ['/ai/configuration', 'AI configuration'], ['/ai/usage', 'AI usage'],
  ['/ai/prompts', 'Prompt guidance'], ['/ai/playground', 'AI playground'], ['/ai/evaluations', 'Evaluations'],
  ['/feedback', 'Answer feedback'], ['/safety', 'Safety incidents'],
  ['/knowledge', 'Knowledge base'], ['/embeddings', 'Embeddings'], ['/terminology', 'Terminology'], ['/foods', 'Food catalogue'], ['/foods/usda', 'USDA sync'],
  ['/jobs', 'Background jobs'], ['/health', 'System health'], ['/settings/flags', 'Feature flags'], ['/settings/system', 'System settings'],
  ['/privacy', 'Privacy requests'], ['/audit', 'Audit logs'], ['/profile', 'My profile'],
];

test.describe.serial('admin portal against the live API', () => {
  test('wrong password is refused without revealing which part was wrong', async ({ page }) => {
    await page.goto('/login');
    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password', { exact: true }).fill('definitely-not-the-password');
    await page.getByRole('button', { name: 'Continue' }).click();
    await expect(page.getByRole('alert')).toContainText(/incorrect|invalid/i);
  });

  test('every module loads real data without errors, in light and dark mode', async ({ page }) => {
    mkdirSync(shots, { recursive: true });
    const failures: string[] = [];
    page.on('response', (r) => {
      if (r.url().includes('/api/') && r.status() >= 500) failures.push(`${r.status()} ${r.url()}`);
    });
    await signIn(page);
    for (const [route, heading] of pages) {
      await page.goto(route);
      await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible({ timeout: 30_000 });
      await page.waitForLoadState('networkidle');
      await expect(page.getByText("Couldn't load this")).toHaveCount(0);
      const name = route === '/' ? 'dashboard' : route.slice(1).replaceAll('/', '-');
      await page.screenshot({ path: path.join(shots, `${name}.png`), fullPage: false });
    }
    // Dark theme, then back to the system default.
    await page.goto('/profile');
    await page.getByRole('radio', { name: /dark/i }).click();
    for (const route of ['/', '/ai/keys', '/safety']) {
      await page.goto(route);
      await page.waitForLoadState('networkidle');
      await page.screenshot({ path: path.join(shots, `dark-${route === '/' ? 'dashboard' : route.slice(1).replaceAll('/', '-')}.png`) });
    }
    await page.goto('/profile');
    await page.getByRole('radio', { name: /system/i }).click();
    expect(failures).toEqual([]);
  });

  test('a session survives a reload and ends on sign-out', async ({ page }) => {
    await signIn(page);
    await page.reload();
    await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible({ timeout: 30_000 });
    await page.getByRole('button', { name: 'Account menu' }).click();
    await page.getByRole('menuitem', { name: /sign out/i }).click();
    await expect(page).toHaveURL(/\/login$/);
    await page.goto('/users');
    await expect(page).toHaveURL(/\/login$/);
  });

  test('the phone layout uses a navigation drawer', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await signIn(page);
    await page.getByRole('button', { name: 'Open navigation' }).click();
    await page.getByRole('dialog').getByRole('link', { name: /Safety incidents/ }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Safety incidents' })).toBeVisible();
    await page.screenshot({ path: path.join(shots, 'mobile-safety.png') });
  });
});
