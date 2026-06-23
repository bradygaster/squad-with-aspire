// LP-006 E2E suite for "remember last viewed page".
// Env-gated on LP_E2E_BASE_URL — auto-skips when app isn't running, so this
// can land ahead of LP-002/LP-003 implementation work.

import { test, expect, type Page } from '@playwright/test';

const BASE = process.env.LP_E2E_BASE_URL;
test.skip(!BASE, 'LP_E2E_BASE_URL not set; skipping LP-006 E2E suite.');

const STORAGE_KEY = 'ta.nav.lastPage.v1';
const OPT_OUT_KEY = 'ta.nav.lastPage.optOut.v1';

async function setStorage(page: Page, kv: Record<string, string>) {
  await page.addInitScript((entries) => {
    for (const [k, v] of Object.entries(entries as Record<string, string>)) {
      window.localStorage.setItem(k, v);
    }
  }, kv);
}

async function readStorage(page: Page, key: string): Promise<string | null> {
  return await page.evaluate((k) => window.localStorage.getItem(k), key);
}

test.describe('LP-006 S1: deep restore after close/reopen', () => {
  test('flights search with query restores after reopen', async ({ browser }) => {
    const ctx = await browser.newContext();
    const page1 = await ctx.newPage();
    await page1.goto(`${BASE!}/flights/search?from=TLV&to=JFK`);
    await page1.waitForLoadState('networkidle');
    await page1.waitForTimeout(500);
    expect(await readStorage(page1, STORAGE_KEY)).toBe('/flights/search?from=TLV&to=JFK');
    await page1.close();

    const page2 = await ctx.newPage();
    await page2.goto(`${BASE!}/`);
    await expect(page2).toHaveURL(/\/flights\/search\?from=TLV&to=JFK$/);
    await ctx.close();
  });
});

test.describe('LP-006 S2: auth-gated route does NOT restore after logout', () => {
  test.fixme(true, 'requires LP-003 auth-gate hook + app-dev session fixtures');
  test('logout clears stored auth-gated route', async ({ browser }) => {
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    await page.goto(`${BASE!}/account/settings`);
    await page.getByRole('button', { name: /log ?out/i }).click();
    const reopen = await ctx.newPage();
    await reopen.goto(`${BASE!}/`);
    await expect(reopen).toHaveURL(new RegExp(`^${BASE!}/?$`));
  });
});

test.describe('LP-006 S3: deny-list /login is never restored', () => {
  test('visiting /login does not populate storage', async ({ browser }) => {
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    await page.goto(`${BASE!}/login`);
    await page.waitForTimeout(500);
    expect(await readStorage(page, STORAGE_KEY)).toBeNull();
    await ctx.close();
  });
});

test.describe('LP-006 S4: token-bearing URL is never restored', () => {
  test('OAuth callback with ?code is dropped', async ({ browser }) => {
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    await page.goto(`${BASE!}/oauth/callback?code=xyz123`);
    await page.waitForTimeout(500);
    expect(await readStorage(page, STORAGE_KEY)).toBeNull();
    await ctx.close();
  });
});

test.describe('LP-006 S5: external deep-link does not restore', () => {
  test('history.length===1 from external entry → no restore performed', async ({ browser }) => {
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    await setStorage(page, { [STORAGE_KEY]: '/flights/search?from=TLV' });
    await page.goto(`${BASE!}/vacations/123`);
    await page.waitForLoadState('networkidle');
    await expect(page).toHaveURL(/\/vacations\/123$/);
    await ctx.close();
  });
});

test.describe('LP-006 S6: opt-out toggle clears existing value', () => {
  test.fixme(true, 'requires LP-002 settings UI selector');
  test('toggling opt-out OFF wipes storage and skips future writes', async ({ browser }) => {
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    await page.goto(`${BASE!}/flights/search`);
    await page.waitForTimeout(500);
    expect(await readStorage(page, STORAGE_KEY)).toBe('/flights/search');

    await page.goto(`${BASE!}/account/preferences`);
    await page.getByLabel(/remember last page/i).uncheck();

    expect(await readStorage(page, STORAGE_KEY)).toBeNull();
    expect(await readStorage(page, OPT_OUT_KEY)).toBe('1');

    const reopen = await ctx.newPage();
    await reopen.goto(`${BASE!}/`);
    await expect(reopen).toHaveURL(new RegExp(`^${BASE!}/?$`));
    await ctx.close();
  });
});

test.describe('LP-006 S7: stored route 404 → toast and stay on /', () => {
  test('navigate to deleted route shows role=status toast', async ({ browser }) => {
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    await setStorage(page, { [STORAGE_KEY]: '/vacations/999999999' });
    await page.goto(`${BASE!}/`);
    const toast = page.getByRole('status').filter({ hasText: /couldn'?t reopen|page not available/i });
    await expect(toast).toBeVisible({ timeout: 5000 });
    await expect(page).toHaveURL(new RegExp(`^${BASE!}/?$`));
    await ctx.close();
  });
});

test.describe('LP-006 S8: no flash-of-wrong-page', () => {
  test('first paint is / skeleton; restore happens after hydration', async ({ browser }) => {
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    await setStorage(page, { [STORAGE_KEY]: '/flights/search?from=TLV&to=JFK' });

    const navs: string[] = [];
    page.on('framenavigated', (frame) => {
      if (frame === page.mainFrame()) {
        const u = new URL(frame.url());
        navs.push(u.pathname + u.search);
      }
    });

    await page.goto(`${BASE!}/`);
    await page.waitForURL(/\/flights\/search/);
    expect(navs[0]).toBe('/');
    expect(navs[navs.length - 1]).toContain('/flights/search');
    await ctx.close();
  });
});
