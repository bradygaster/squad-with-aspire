// E2E gate for remember-me on travel-assistant.
// Skipped until RM-002 (selectors) and RM-005 (storage decision) are locked.
// When ready, set:
//   RM_E2E_BASE_URL      -> travel-assistant web URL
//   RM_LOGIN_SELECTOR    -> form selector from RM-002 (e.g. 'form[data-testid="login"]')
//   RM_REMEMBER_SELECTOR -> checkbox selector from RM-002
//   RM_LONG_STORE        -> 'cookie' | 'localStorage' from RM-005

import { test, expect } from "@playwright/test";

const BASE_URL     = process.env.RM_E2E_BASE_URL;
const FORM_SEL     = process.env.RM_LOGIN_SELECTOR;
const REMEMBER_SEL = process.env.RM_REMEMBER_SELECTOR;
const LONG_STORE   = (process.env.RM_LONG_STORE ?? "") as "cookie" | "localStorage" | "";

const READY = Boolean(
  BASE_URL && FORM_SEL && REMEMBER_SEL && (LONG_STORE === "cookie" || LONG_STORE === "localStorage"),
);

test.describe("remember-me E2E (skipped until RM-002 + RM-005 lock)", () => {
  test.skip(!READY, "RM-002 selectors and/or RM-005 storage decision not yet locked");

  test("(a) checkbox default unchecked + state survives re-render", async ({ page }) => {
    await page.goto(`${BASE_URL}/login`);
    const cb = page.locator(`${FORM_SEL!} ${REMEMBER_SEL!}`);
    await expect(cb).not.toBeChecked();
    await cb.check();
    await page.locator(`${FORM_SEL!} input[name="email"]`).fill("a@b.test");
    await expect(cb).toBeChecked();
  });

  test("(b) unchecked -> sessionStorage, cleared on tab close", async ({ browser }) => {
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    await page.goto(`${BASE_URL}/login`);
    const sessionToken = await page.evaluate(() => sessionStorage.getItem("ta:auth:v1"));
    expect(sessionToken).toBeTruthy();
    const localToken = await page.evaluate(() => localStorage.getItem("ta:auth:v1"));
    expect(localToken).toBeNull();
    await ctx.close();
    const ctx2 = await browser.newContext();
    const page2 = await ctx2.newPage();
    await page2.goto(`${BASE_URL}/`);
    const after = await page2.evaluate(() => sessionStorage.getItem("ta:auth:v1"));
    expect(after).toBeNull();
  });

  test("(c) checked -> RM-005 store with explicit expiry", async ({ page, context }) => {
    await page.goto(`${BASE_URL}/login`);
    await page.locator(`${FORM_SEL!} ${REMEMBER_SEL!}`).check();
    if (LONG_STORE === "cookie") {
      const cookies = await context.cookies();
      const tok = cookies.find(c => c.name === "ta_auth");
      expect(tok).toBeTruthy();
      expect(tok!.httpOnly).toBe(true);
      expect(tok!.expires).toBeGreaterThan(Date.now() / 1000 + 60 * 60 * 24);
    } else {
      const v = await page.evaluate(() => localStorage.getItem("ta:auth:v1"));
      expect(v).toBeTruthy();
    }
  });

  test.fixme("(d) backend issues short vs long TTL based on flag");
  test.fixme("(e) /refresh preserves rememberMe across rotations");
  test.fixme("(f) logout revokes both sessionStorage and long-lived store");
  test.fixme("(g) password change invalidates all long-lived tokens server-side");
});
