/**
 * DM-004 §3 E2E: persistence, system tracking, FOUC.
 *
 * Skipped until DM-002 ships a Playwright config + dev server. The specs are
 * authored verbatim against the spec so they go red-until-green.
 */
import { test, expect } from '@playwright/test';

const BASE = process.env.DM_E2E_BASE_URL ?? '';
test.skip(!BASE, 'DM_E2E_BASE_URL not set — skipping until DM-002 dev server is wired');

test.describe('DM-004 §3 persistence', () => {
  test('dark survives reload', async ({ page }) => {
    await page.goto(BASE);
    await page.getByRole('radio', { name: /dark/i }).check();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  });

  test('system tracks OS preference change without reload', async ({ page, context }) => {
    await context.emulateMedia({ colorScheme: 'light' });
    await page.goto(BASE);
    await page.getByRole('radio', { name: /system/i }).check();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
    await context.emulateMedia({ colorScheme: 'dark' });
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  });

  test('corrupted localStorage falls back to system', async ({ page }) => {
    await page.addInitScript(() => {
      try { localStorage.setItem('ta.theme', 'garbage'); } catch { /* ignore */ }
    });
    await page.goto(BASE);
    // resolved must be light or dark, never "garbage" or "system" as data-theme
    const theme = await page.locator('html').getAttribute('data-theme');
    expect(['light', 'dark']).toContain(theme);
  });

  test('localStorage disabled does not crash', async ({ page }) => {
    await page.addInitScript(() => {
      Object.defineProperty(window, 'localStorage', {
        get() { throw new Error('SecurityError'); },
      });
    });
    const errors: string[] = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.goto(BASE);
    await expect(page.locator('html')).toHaveAttribute('data-theme', /light|dark/);
    expect(errors).toEqual([]);
  });
});

test.describe('DM-004 §4 no-FOUC', () => {
  test('stored=dark: html[data-theme=dark] set before first paint with dark bg', async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem('ta.theme', 'dark'));
    // Intercept first response and assert inline <script> sets data-theme synchronously.
    const responsePromise = page.waitForResponse(r => r.url().startsWith(BASE) && r.request().resourceType() === 'document');
    await page.goto(BASE);
    const resp = await responsePromise;
    const html = await resp.text();
    // Strong contract: a synchronous <script> in <head> must set data-theme before render.
    expect(html, 'index.html must inline a pre-paint theme script').toMatch(
      /<script[^>]*>[^<]*document\.documentElement[^<]*setAttribute\(\s*['"]data-theme['"]/
    );
    // And the resulting attribute is correct on the very first DOM snapshot.
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  });
});
