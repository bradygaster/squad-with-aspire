# Checkout State-Copy Spec — Amendment for SEC-CHK-008

**Parent:** `checkout-state-copy-and-analytics-spec.md` (commit `31e42b1`)
**Status:** Design-frozen. Supersedes §3.1 `failed_terminal` row, §3.2 floor rule, §5.2 telemetry rule, §6 testid rules. Re-cut per security-hardening verdict SEC-CHK-008 R1–R6.
**Effective:** Immediately. QA bundle 7 (analytics) and bundle 6 (terminal-error-focus) MUST use these bytes.
**Date:** 2026-06-24

---

## What changed

Security-hardening's verdict tightened §3.2 from "floor" to "ceiling". The amendment makes all 4 `failed_terminal` reasons (`hard_decline`, `fraud_block`, `insufficient_funds_terminal`, `provider_rejected_permanent`) **byte-identical in every user-observable surface**.

Three concessions absorbed:

1. **Lost `insufficient_funds_terminal` differentiated copy** — users with genuinely insufficient funds now see the same generic message. UX regression accepted; security gain (close balance-probe oracle) outweighs.
2. **CTA routes to `/cart` not `/payment`** — retry on `/payment` for hard_decline (card "might work next time") vs `/cart` for fraud_block would be a routing oracle. Single destination across all 4.
3. **Secondary CTA single label** — no "Contact us" vs "Get help" vs "Why was this declined?" branching. One label, one href.

Retryable copy differentiation is **preserved** (3DS / gateway_timeout / soft_decline / provider_unavailable still differ — they're actionable, not a fraud-oracle surface).

---

## §3.1 — Frozen copy table (amended)

### `failed_terminal` — single row, all 4 reasons

| Surface | Required bytes (identical for hard_decline, fraud_block, insufficient_funds_terminal, provider_rejected_permanent) |
|---|---|
| Heading (`<h1>`) | `Your payment couldn't be processed` |
| Sub-copy (`<p>`) | `We weren't able to complete your order. Please review your payment details or try a different payment method.` |
| Primary CTA label | `Try again` |
| Primary CTA href | `/cart` |
| Secondary CTA label | `Get help` |
| Secondary CTA href | `/help/payments` |
| DOM `data-state` | `failed_terminal` |
| DOM `data-reason` | `declined_terminal` (constant string — never the enum value) |
| ARIA live-region (`role="alert"`) | Concatenation of heading + sub-copy, identical across the 4 reasons |
| Focus target on entry | `<h1 data-testid="checkout-status-heading">` (per focus-policy §3 — terminal-error moves focus) |

**No reason-specific sub-row exists.** Frontend MUST NOT branch copy, CTA label, CTA href, or live-region announcement on the underlying reason enum. The server response field `reason` is fixed to `"declined_terminal"` (per R2); frontend has nothing to branch on by construction.

### `failed_retryable` — UNCHANGED from parent spec

4 differentiated rows preserved (`gateway_timeout`, `provider_unavailable`, `soft_decline`, `three_ds_required`). Retryable surface is NOT a fraud-oracle attack surface — actionable information for the user is permitted. Every variant still contains the literal sentence `Your card was not charged.` (still flagged as Open Q1 to app-dev for `soft_decline`).

### `CONFIRM_REJECTED` — UNCHANGED from parent spec

3 differentiated rows preserved (`out_of_stock`, `price_changed`, `shipping_unavailable`). Not security-sensitive.

---

## §3.2 — Fraud-oracle prevention rule (re-cut)

**Old rule:** "The floor is identical bytes in copy. Server-side timing, HTTP status, headers — security-hardening's lane."

**New rule (binding, per SEC-CHK-008 R1+R2):** All 4 `failed_terminal` reasons MUST be indistinguishable in every surface observable to a non-privileged client:

1. **DOM bytes** (heading, sub-copy, CTA labels, CTA hrefs, `data-*` attrs, ARIA live-region text) — identical across the 4 reasons. Enforced by §3.1 single-row spec above.
2. **HTTP wire shape** (status code, headers, header order, header values, body envelope shape, body field values) — app-dev owns; SEC-CHK-008 R2 mandates. Frontend MUST NOT receive any field that varies by underlying reason.
3. **Response timing** — server-side equalization to ≥800ms floor + ±50ms jitter. App-dev owns; SEC-CHK-008 R3 mandates. Frontend has no role.
4. **Client telemetry** — `checkout_failed_terminal` emits `reason: "declined_terminal"` always (§5.2 amendment below).
5. **Server-rendered HTML attributes** consumed by 3p analytics SDKs (Segment, Amplitude, GA4) — emit `"declined_terminal"` only. App-dev owns.

**Permitted differentiation (full reason fidelity):**
- Server-side data warehouse exports gated to risk/security/finance roles only.
- Server-side structured logs gated to risk/security roles only.
- Order record stored server-side (never serialized to client envelope).

**Forbidden differentiation everywhere else.** This is the ceiling; tightening further (e.g., banning the `Get help` CTA entirely, or rendering `failed_terminal` and `failed_retryable` identically) is not currently mandated but is acceptable if a future threat-model update requires it.

---

## §5.2 — Telemetry rule (re-cut)

The `checkout_failed_terminal` event is unchanged in shape from parent spec — but the binding is now stronger:

```ts
safeTrack('checkout_failed_terminal', {
  state: 'failed_terminal',
  reason: 'declined_terminal',  // CONSTANT. Never composed from server response.
  attemptNumber: <int>,
  durationMs: <int>,
  checkoutSessionId: <opaque>,
});
```

**Frontend implementation rule:** The string `'declined_terminal'` MUST be a hardcoded literal in the call site. Frontend MUST NOT read `response.reason` and forward it — even though the server (per R2) will always send `"declined_terminal"`, defense-in-depth requires the literal at the call site so a server regression cannot leak via telemetry.

**Discipline gate (add to QA bundle 7):**

```bash
# Every checkout_failed_terminal call must use a literal "declined_terminal", never response field forwarding
grep -rE "safeTrack\(\s*['\"]checkout_failed_terminal['\"]" src/checkout/ -A 5 | grep -E "reason:\s*(response|payload|data|envelope|res)\."
# Must return empty.
```

**Forbidden in props (extends parent §5.3 PII list):** the underlying enum values `hard_decline`, `fraud_block`, `insufficient_funds_terminal`, `provider_rejected_permanent` MUST NOT appear in any client-side telemetry event under any property name, ever.

```bash
grep -rE "['\"](hard_decline|fraud_block|insufficient_funds_terminal|provider_rejected_permanent)['\"]" src/checkout/
# Must return empty (no allowlist — these strings have no client-side reason to exist).
```

---

## §6 — Testid contract (re-cut)

The 3 testids from parent spec are preserved. The `data-reason` rule is hardened:

| testid | `data-*` attribute | Allowed values |
|---|---|---|
| `checkout-status-heading` | `data-state` | `confirming`, `failed_retryable`, `failed_terminal`, `confirm_rejected` |
| `checkout-status-heading` | `data-reason` | For `failed_retryable`: `gateway_timeout`, `provider_unavailable`, `soft_decline`, `three_ds_required`, or `unmapped`. For `failed_terminal`: **`declined_terminal` ONLY** (literal constant — never the underlying enum). For `confirm_rejected`: `out_of_stock`, `price_changed`, `shipping_unavailable`, or `unmapped`. For `confirming`: attribute absent. |
| `checkout-retry-button` | — | Present only in `failed_retryable`. Absent in `failed_terminal`. (Primary CTA in `failed_terminal` is `checkout-return-to-cart-button` — new testid, see below.) |

**New testid required by R1 (CTA route to `/cart`):**

| testid | purpose |
|---|---|
| `checkout-return-to-cart-button` | Primary CTA in `failed_terminal`. `href="/cart"`. Label = `"Try again"`. Replaces any `checkout-retry-button` rendering in terminal states. |
| `checkout-get-help-link` | Secondary CTA in `failed_terminal` AND `failed_retryable`. `href="/help/payments"`. Label = `"Get help"`. Single label across both states (no "Contact us" / "Why was this declined?" branching). |

**QA discipline gates (add to bundle 7):**

```bash
# 1. failed_terminal must never expose underlying enum via data-reason
grep -rE 'data-reason=["\'"](hard_decline|fraud_block|insufficient_funds_terminal|provider_rejected_permanent)["\'"]' src/checkout/
# Must return empty.

# 2. failed_terminal must never render checkout-retry-button
grep -rB 3 -A 3 'data-testid="checkout-retry-button"' src/checkout/ | grep 'failed_terminal'
# Must return empty.

# 3. failed_terminal primary CTA must route to /cart, not /payment
grep -rB 3 -A 3 'checkout-return-to-cart-button' src/checkout/ | grep -E 'href=["\'"]/payment'
# Must return empty.

# 4. No conditional CTA labels across the 4 terminal reasons
grep -rE "(Contact us|Why was this declined|Get help)" src/checkout/ | wc -l
# Must equal exactly 1 (the single "Get help" rendering).
```

**QA test additions (bundle 6 — terminal-error-focus):**

```ts
test.describe('failed_terminal — fraud-oracle byte equality (SEC-CHK-008 R1)', () => {
  const TERMINAL_REASONS = ['hard_decline', 'fraud_block', 'insufficient_funds_terminal', 'provider_rejected_permanent'];

  test('all 4 terminal reasons render byte-identical DOM', async ({ page }) => {
    const snapshots = await Promise.all(TERMINAL_REASONS.map(async (reason) => {
      await page.goto(`/checkout/review?_force_reason=${reason}`);  // CHECKOUT_DEBUG=1 seam
      await page.click('[data-testid="checkout-review-place-order-button"]');
      await page.waitForSelector('[data-testid="checkout-status-heading"][data-state="failed_terminal"]');
      return {
        heading: await page.textContent('[data-testid="checkout-status-heading"]'),
        subCopy: await page.textContent('[data-testid="checkout-status-subcopy"]'),
        primaryCtaLabel: await page.textContent('[data-testid="checkout-return-to-cart-button"]'),
        primaryCtaHref: await page.getAttribute('[data-testid="checkout-return-to-cart-button"]', 'href'),
        secondaryCtaLabel: await page.textContent('[data-testid="checkout-get-help-link"]'),
        secondaryCtaHref: await page.getAttribute('[data-testid="checkout-get-help-link"]', 'href'),
        dataReason: await page.getAttribute('[data-testid="checkout-status-heading"]', 'data-reason'),
      };
    }));
    // All 4 snapshots must be byte-identical.
    for (let i = 1; i < snapshots.length; i++) {
      expect(snapshots[i]).toEqual(snapshots[0]);
    }
    // data-reason is coarsened.
    expect(snapshots[0].dataReason).toBe('declined_terminal');
  });

  test('checkout-retry-button is absent in failed_terminal', async ({ page }) => {
    await page.goto('/checkout/review?_force_reason=hard_decline');
    await page.click('[data-testid="checkout-review-place-order-button"]');
    await page.waitForSelector('[data-testid="checkout-status-heading"][data-state="failed_terminal"]');
    await expect(page.locator('[data-testid="checkout-retry-button"]')).toHaveCount(0);
  });
});
```

---

## What is NOT changed

- `failed_retryable` copy table (4 differentiated rows) — preserved verbatim.
- `CONFIRM_REJECTED` copy table (3 differentiated rows) — preserved verbatim.
- `confirming` state behavior — preserved verbatim.
- /confirmation page (post-success surface) — out of scope; no oracle.
- Bundle budget (~2.6KB gz across 5 pages) — unchanged.
- Per-reason `failed_retryable` analytics fidelity — preserved (not a fraud-oracle surface).
- Open Q1 (no-charge invariant per retryable reason) — still open with app-dev.
- Open Q2 (cart-diff contract) — still open with app-dev.
- Open Q3 (3DS surface decision) — still open with app-dev.

---

## Reconciliation owed by frontend (if checkout v1 already shipped)

If `RefundModal`-style differentiated terminal copy was already drafted for checkout (e.g., distinct "insufficient funds" sub-copy), it MUST be removed before bundle 7 ships. Grep audit:

```bash
grep -rE "(insufficient funds|fraud|declined for|different card)" src/checkout/ | grep -v 'declined_terminal' | grep -v test
# Should return empty after reconciliation. Any hit is a bytes-not-equal violation.
```

Frontend has not yet shipped checkout pages (only contracts at app-dev `c80c3e4`), so this is a pre-emptive constraint, not a retrospective patch.

---

## Sign-off

This amendment is binding under reviewer-rejection-protocol via SEC-CHK-008. Exp-design squad accepts the UX regression on `insufficient_funds_terminal` differentiation as justified by the fraud-oracle threat model.

Next action: QA bundle 6 (terminal-error-focus) and bundle 7 (analytics) author against these bytes. App-dev owns R2/R3/R4/R5 per SEC-CHK-008 separately.

— experience-design squad
