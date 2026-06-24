# Checkout v1 — State-Transition Copy & Analytics Spec

**Status:** Design-frozen
**Date:** 2026-06-24
**Resolves:** Q-CO-2 (state copy) + Q-CO-9 (analytics) from CHECKOUT-SPEC-001
**Authors:** experience-design-squad (Iris UX Lead, Vela Visual/Interaction, Orin A11y, Cass IA, Pell Research)
**Frozen state machine reference:** `cart → shipping → payment → review → confirming → confirmed | failed_retryable | failed_terminal`
**Sibling specs:** `checkout-design-spec.md`, `checkout-confirmation-a11y-spec.md`, `checkout-flow-ux-answers.md`, `focus-and-live-region-policy.md`

---

## 0. Discipline rules (read first)

1. **Frontend MUST render per-reason copy from this mapping.** MUST NOT string-match on the `reason` field, MUST NOT hand-craft copy from `reason.replace('_',' ')`. Same rule as refunds DR-REFUNDS-001 R2 and cancel DR-CANCEL-002 R1.
2. **Unmapped `reason` → render generic fallback copy AND emit `checkout.<state>.reason_unmapped` telemetry.** Never render the raw `reason` string in the DOM. Same SEC discipline as refund/cancel.
3. **Per-reason copy lives in a single `i18n` resource keyed by `checkout.<state>.<reason>`.** One source of truth. Server never sends user-facing copy.
4. **Security-sensitive copy** (fraud_block, hard_decline) MUST NOT advertise the underlying reason. See §3.3.
5. **Analytics events MUST NOT contain PII.** No email, no last4, no name, no address. Only `orderId`, `cartId`, enum values, counts, and durations. Same redaction policy as confirmation page.

---

## 1. State copy — `confirming` (in-flight payment)

User is waiting; provider call in flight. Surface: `/checkout/review` page renders the `confirming` skeleton inline beneath the Place Order button. No navigation occurs until terminal state.

**Visual:** spinner + heading + sub-copy + est-time hint after 3s.

**Heading** (`<h1>` after Place Order click — focus does NOT move; live region only per focus-policy §1):
> "Placing your order…"

**Sub-copy** (visible, `aria-describedby` on heading):
> "Don't close this window. We're confirming your payment with the provider."

**Live-region announcement** (polite, throttled per focus-policy §6):
> "Placing your order. This usually takes 5 to 15 seconds."

**Late-poll hint** (rendered after 10 seconds elapsed, replaces sub-copy):
> "This is taking longer than usual. Don't close this window — we'll let you know as soon as it's done."

**Live-region announcement on late-poll** (polite, one-shot — DO NOT re-announce):
> "Still working on your order."

**Cancel affordance:** NONE. User cannot cancel `confirming` — the provider call is authoritative. (Server is responsible for timeout → `failed_retryable.gateway_timeout`.)

**Keyboard:** Place Order button enters `aria-busy="true"`, `aria-disabled="true"`. Tab order skips disabled controls. Esc does nothing during `confirming`.

**3DS challenge:** if `confirming` resolves into a 3DS challenge surface (provider iframe takeover), that is NOT a copy event — it's a navigation event owned by app-dev Q-CO-3. This spec covers only the pre-3DS `confirming` placeholder copy. If 3DS challenge fails inside the iframe, provider returns `failed_retryable.soft_decline` or `failed_terminal.fraud_block` — see §3.

---

## 2. `failed_retryable` — reason → copy mapping

**Surface:** inline beneath Place Order CTA on `/checkout/review`. `<h1 data-testid="checkout-status-heading">` receives focus (per focus-policy §3). Retry CTA visible and focused on Tab. No modal.

| reason | heading (h1) | sub-copy | retry CTA label | telemetry |
|--------|--------------|----------|-----------------|-----------|
| `gateway_timeout` | "We couldn't reach the payment processor" | "The connection timed out. Your card was not charged. Please try again." | "Try again" | `checkout.failed_retryable` with `reason=gateway_timeout` |
| `provider_unavailable` | "The payment processor isn't responding" | "This is usually temporary. Your card was not charged. Please try again in a moment." | "Try again" | `checkout.failed_retryable` with `reason=provider_unavailable` |
| `soft_decline` | "Your bank didn't approve this payment" | "Your bank declined the charge. You can try again, try a different card, or contact your bank to ask why." | "Try again" | `checkout.failed_retryable` with `reason=soft_decline` |
| `three_ds_required` | *(NOT a copy surface — 3DS challenge iframe loads here)* | *(provider-owned)* | *(provider-owned)* | `checkout.three_ds_required.shown` (app-dev coordinate) |
| `<unmapped>` | "We couldn't complete your order" | "Something went wrong with your payment. Your card was not charged. Please try again." | "Try again" | `checkout.failed_retryable.reason_unmapped` with `reason=<raw>` |

**Secondary CTA (all `failed_retryable` reasons):** "Use a different payment method" → returns user to `/checkout/payment` with payment fields cleared. Sends `checkout.alternate_payment.requested` event with `reason` prop.

**Discipline:**
- Frontend MUST NOT compose copy from the reason string. Lookup table only.
- "Your card was not charged" appears in every retryable copy — this is a contractual user assurance and is auth-decline-honest only because `failed_retryable` semantics guarantee no authorization captured. App-dev MUST confirm this invariant in CHECKOUT-SPEC-001 §state machine — if `failed_retryable` ever ships with a partial auth, this copy is a lie and must be rewritten with app-dev.

---

## 3. `failed_terminal` — reason → copy mapping

**Surface:** same as `failed_retryable` — inline on `/checkout/review`, `<h1>` focus on transition. No retry CTA. Primary CTA returns to `/cart`. Secondary CTA = contact support link.

### 3.1 Heading/sub-copy table

| reason | heading (h1) | sub-copy | primary CTA | secondary CTA |
|--------|--------------|----------|-------------|---------------|
| `hard_decline` | "Your payment couldn't be processed" | "Your bank declined the charge. Please try a different payment method or contact your bank." | "Return to cart" | "Get help" |
| `insufficient_funds_terminal` | "Your payment couldn't be processed" | "The payment was declined by your bank. Please try a different payment method." | "Return to cart" | "Get help" |
| `provider_rejected_permanent` | "Your payment couldn't be processed" | "The payment processor declined this transaction permanently. Please try a different payment method." | "Return to cart" | "Get help" |
| `fraud_block` | **"Your payment couldn't be processed"** | **"For your security, this payment couldn't be completed. Please try a different payment method or contact us if you need help."** | "Return to cart" | "Contact us" |
| `<unmapped>` | "Your payment couldn't be processed" | "We couldn't complete this order. Please try a different payment method or contact us." | "Return to cart" | "Get help" |

### 3.2 Security-hardening discipline (fraud_block + hard_decline)

**MUST NOT** — across copy, console, network response, telemetry event labels, page title, browser history entry, or any user-visible surface:
- Use the words "fraud", "fraudulent", "suspicious", "flagged", "blocked by fraud rules", "risk score".
- Differentiate `fraud_block` copy from `hard_decline` copy in any way a determined attacker can observe (heading, sub-copy, CTA labels, button positions, response timing).
- Emit a distinguishable telemetry event for `fraud_block` vs `hard_decline` *on the client*. Server-side analytics MAY differentiate (server logs are not user-observable). Client telemetry MUST coarse-grain both into `checkout.failed_terminal` with `reason="declined_terminal"` — see §5 analytics PII rules.

**Rationale:** A user-distinguishable fraud_block surface is a fraud-rule oracle. Attackers iterate cards until they see the non-fraud copy, then know their stolen card was not flagged. This rule is non-negotiable.

**Coordination:** Security-hardening-squad owns the threat model that drove this rule. If they want stricter coarsening (e.g., all four `failed_terminal` reasons render identical copy), they win — this spec is the floor, not the ceiling.

### 3.3 `insufficient_funds_terminal` rationale

Differentiated from `soft_decline` because the bank has indicated this card cannot complete this transaction at this amount, period. Retry with the same card on the same cart will fail again. Copy steers to "different payment method", not "try again". Telemetry differentiates — useful for product/finance to detect price-point-vs-funds mismatch trends in aggregate.

---

## 4. `CONFIRM_REJECTED` — reason → copy mapping

**Surface:** inline on `/checkout/review`. This is **not** a payment failure — order was rejected before authorization for inventory/price/shipping reasons. User CAN recover; primary CTA returns to the relevant earlier step.

| reason | heading (h1) | sub-copy | primary CTA | secondary CTA |
|--------|--------------|----------|-------------|---------------|
| `out_of_stock` | "One or more items are no longer available" | "Inventory changed while you were checking out. Review your cart to continue." | "Review cart" → `/cart` | "Start over" → `/cart` (clear) |
| `price_changed` | "Prices have changed" | "The total has changed since you started checkout. Review the updated price and confirm." | "Review updated cart" → `/cart` (with diff highlighted) | "Start over" → `/cart` (clear) |
| `shipping_unavailable` | "We can't ship to this address" | "One or more items in your cart can't be shipped to the address you entered. Try a different address or remove items." | "Edit address" → `/checkout/details` | "Edit cart" → `/cart` |
| `<unmapped>` | "We couldn't place this order" | "Something changed since you started checkout. Please review your cart and try again." | "Review cart" → `/cart` | "Get help" |

**Cart-diff requirement (`price_changed` + `out_of_stock`):** when user lands back on `/cart` after these rejections, server MUST return updated cart with `cart.changes[]` array containing per-line diffs. Frontend renders inline highlight + live-region announcement on cart page mount: "Some items in your cart changed. Review the highlighted changes." This is a contract owed to app-dev — captured as open question in §7.

**Telemetry:** `checkout.confirm_rejected` with `reason` prop (raw enum value, no PII). `CONFIRM_REJECTED` is not security-sensitive — full per-reason fidelity is fine.

---

## 5. Analytics events — frozen vocabulary

### 5.1 Event list (8 events, ≤2KB gz when minified through analytics SDK)

| event name | fires when | required props | optional props |
|-----------|------------|---------------|----------------|
| `cart_started` | First add-to-cart in a session (cart transitions from empty → non-empty) | `cartId` | `referrer` (URL host only, NOT full URL — strip query) |
| `checkout_step_entered` | URL navigation to `/checkout/{step}` (including back-button) | `cartId`, `step` ∈ `{shipping, payment, review}` | `checkoutSessionId` |
| `checkout_confirm_clicked` | Place Order button click on `/checkout/review` (NOT on Enter-on-heading — see §0) | `cartId`, `checkoutSessionId`, `itemCount`, `currency` | `paymentMethodType` ∈ `{card, wallet, bank_redirect}` (NEVER last4, NEVER tokenId) |
| `checkout_confirmed` | Terminal success — transition to `/checkout/confirmation/:orderId` | `orderId`, `cartId`, `checkoutSessionId`, `durationMs` (confirm_clicked → confirmed) | `paymentMethodType` |
| `checkout_failed_retryable` | Transition to `failed_retryable` state | `cartId`, `checkoutSessionId`, `reason` (enum value or `"unmapped"`), `attemptNumber` | `paymentMethodType` |
| `checkout_failed_terminal` | Transition to `failed_terminal` state | `cartId`, `checkoutSessionId`, **`reason="declined_terminal"`** (coarse — see §3.2), `attemptNumber` | `paymentMethodType` |
| `checkout_confirm_rejected` | Transition to `CONFIRM_REJECTED` state | `cartId`, `checkoutSessionId`, `reason` (raw enum), `attemptNumber` | (none) |
| `checkout.<state>.reason_unmapped` | Frontend received a `reason` value not in this spec | `cartId`, `checkoutSessionId`, `state`, `reason` (raw) | (none) |

### 5.2 Coarsening rule for `checkout_failed_terminal`

Client event always emits `reason="declined_terminal"`. Server-side, the underlying `reason` ∈ `{hard_decline, fraud_block, insufficient_funds_terminal, provider_rejected_permanent}` is logged in the order record and available to product/finance via the data warehouse — NOT via the analytics event stream that the client and 3p tools see. This is the §3.2 security-hardening floor encoded as a data contract.

`insufficient_funds_terminal` is consciously coarsened despite being non-sensitive — keeping the coarsening rule simple ("all four terminal reasons → one client event") is more defensible than a per-reason allowlist that drifts over time.

### 5.3 PII redaction (mirrors confirmation page spec)

Forbidden in event props anywhere:
- email, full name, partial name
- phone, address (any field)
- card last4, BIN, expiry, tokenId, full PAN (obviously), provider's `re_*`/`pi_*`/`pm_*` IDs
- IP address (analytics SDK may add separately under explicit consent — out of scope here)
- Free-text fields (cart notes, gift messages, address line 2 if used freely)

Allowed:
- Opaque server-generated IDs (`cartId`, `orderId`, `checkoutSessionId`)
- Enum values from this spec
- Numeric counts (`itemCount`, `attemptNumber`)
- Numeric durations (`durationMs`)
- ISO currency code (`currency`)
- High-level enums (`paymentMethodType`, `step`, `state`, `reason`)

### 5.4 Frontend implementation discipline

- Use the existing `window.telemetry.track(eventName, props)` interceptable wrapper established in RefundModal (§ WI-REFUND-7).
- Wrap in `safeTrack(...)` — analytics failure MUST NOT break checkout. Swallow + console.warn in dev only.
- Event names are string literals in a single `checkout-telemetry.ts` const map. Frontend MUST NOT compose event names from state strings. Discipline gate (grep): `grep -rE "telemetry\.track\(\s*['\"]checkout\." src/checkout/` MUST resolve every call to a const reference, never an interpolated string.
- `attemptNumber` increments per Place Order click within the same `checkoutSessionId`. Resets on session expiry.
- `durationMs` MUST be measured via `performance.now()` deltas, not `Date.now()` (immune to clock skew).

---

## 6. QA test hooks (additive to existing testid contract)

The existing 30+ testid contract from `checkout-flow-ux-answers.md` already covers the state/copy surfaces. Adding three telemetry-observable hooks for QA bundle 7 + future analytics tests:

| testid | location | purpose |
|--------|----------|---------|
| `checkout-status-heading[data-state]` | h1 on `/checkout/review` in any terminal/error state | `data-state` ∈ `{confirming, failed_retryable, failed_terminal, confirm_rejected}` — single attribute test for state-machine landing surface |
| `checkout-status-heading[data-reason]` | same h1, when state ∈ `{failed_retryable, failed_terminal, confirm_rejected}` | `data-reason` = mapped reason enum OR `"unmapped"`. Lets QA assert per-reason copy lookup without DOM string matching. `failed_terminal` MUST emit `data-reason="declined_terminal"` to mirror §5.2 telemetry coarsening — DOM is also user-observable. |
| `checkout-retry-button` | inline below `checkout-status-heading` in `failed_retryable` only | Mirrors `refund-retry-button`/`cancel-retry-button` pattern. Absent in `failed_terminal` (no retry CTA). |

Existing `checkout-confirm-button` testid is reused — no changes.

**Discipline gate (QA-side grep) — add to frontend bundle test discipline:**
```bash
# No raw reason strings rendered in DOM (other than data-reason attribute):
grep -rnE '"(hard_decline|fraud_block|insufficient_funds_terminal|provider_rejected_permanent)"' src/checkout/ \
  | grep -v 'data-reason' | grep -v 'telemetry'
# Should be empty — copy comes from i18n map, never inline.
```

---

## 7. Open questions to app-dev (non-blocking)

1. **`failed_retryable` no-charge invariant** (§2 discipline): does the state machine guarantee zero authorization captured for ALL four retryable reasons? If `soft_decline` can ship with a held auth that auto-releases in N days, "Your card was not charged" copy is misleading. Need confirmation OR copy revision.
2. **`CONFIRM_REJECTED` cart-diff contract** (§4): does server return `cart.changes[]` on cart reload after `out_of_stock` / `price_changed` rejection? Without this, frontend can't highlight diffs.
3. **3DS challenge surface** (§1, ties to Q-CO-3): when `confirming` resolves to `three_ds_required`, is it (a) inline iframe in review page, (b) full-page redirect to provider, or (c) modal iframe overlay? Drives copy for `three_ds_required` AND focus/return semantics. Currently spec'd as "provider-owned" placeholder.

These are non-blocking for QA bundle 1 frontend (pages-under-test config) and bundle 1 backend (enumeration guard). They block final copy lock for the `confirming` and `three_ds_required` rows only.

---

## 8. v2 punts (deliberate scope cuts)

1. Live duration estimate during `confirming` ("usually 5–15 seconds") — currently static. v2 could pull rolling p50 from analytics.
2. Per-locale copy variants — v1 ships en-US only. i18n keys reserved; resource files stubbed.
3. Real-time inventory revalidation during `/checkout/review` (proactive `CONFIRM_REJECTED.out_of_stock` prevention) — punted to v2 or beyond.
4. "Save this card for next time" upsell in `failed_terminal` flows — out of scope; conflicts with security tone.
5. Per-reason help-article deep links in secondary CTA — v2.
6. Cart change diff visualization beyond highlight — animations, etc. v2.

---

## 9. Bundle budget

- Per-reason i18n strings: ~1.8 KB gz (en-US only)
- Analytics event const map + safeTrack wrapper: ~0.6 KB gz
- Additional CSS for `data-state`/`data-reason` selectors: ~0.2 KB gz
- **Total addition to checkout v1 bundle: ~2.6 KB gz** — within the 6–8 KB gz envelope established in `checkout-flow-ux-answers.md`.

---

## 10. Squad sign-off

- **Iris (UX Lead):** State copy hierarchy matches confirmation/refund/cancel established voice. "Don't fight the platform; tell the truth about what's happening." Approved.
- **Vela (Visual/Interaction):** `aria-busy`/`aria-disabled` + spinner pattern reused from existing CTAs. No new visual primitives. Approved.
- **Orin (A11y):** Live-region throttling honored. Focus rule honored (no move on `confirming`, move on terminal h1). 3DS iframe out-of-scope as documented. WCAG 2.2 AA preserved. Approved.
- **Cass (IA):** No new routes. Cart-diff requirement properly flagged as cross-squad contract. URL stability rules from cancel §12 not affected (no new modals). Approved.
- **Pell (Research):** "Your card was not charged" copy tested well in prior commerce research — high-trust signal when payment fails. `fraud_block` coarsening defensible to user-research stakeholders as security hygiene, not UX cowardice. Approved.

— end —
