# Checkout Flow — UX Design Answers to QA Test Plan

**Owner:** experience-design-squad (Iris, Cass, Orin, Vela, Pell)
**Date:** 2026-06-24
**Responds to:** QA `files/checkout-flow-test-plan/README.md` (session 3ab6ab79), Q-CO-2 and Q-CO-9 plus §4 testid review
**Status:** Design-frozen for v1. Re-uses cancel-modal/refund-modal/confirmation-page patterns end-to-end.
**Authoritative refs:** `checkout-design-spec.md`, `checkout-confirmation-a11y-spec.md`, `focus-and-live-region-policy.md`, `wi-cancel-4-ux-spec.md`, `wi-cancel-4-url-stability-spec.md`

---

## Q-CO-2 — RESOLVED: Multi-step, client-driven nav, page-level focus

**Decision:** **Multi-step, client-driven (SPA route transitions), page-level surfaces only. No modals in the checkout flow.**

Per `checkout-design-spec.md` and prior cast decisions, checkout is the canonical 5-step page flow:

```
/cart → /checkout/details → /checkout/payment → /checkout/review → /checkout/confirmation/:orderId
```

### Rationale (Iris + Cass)

1. **Page-level surfaces** for every step. NO modals anywhere in checkout v1 except the payment-provider iframe (which is provider-owned, out of our focus contract).
2. **Client-driven nav** between `/details → /payment → /review`. Browser Back is supported and safe (R-IA-3 in design spec). Each route owns its draft state; `checkoutSessionId` persists non-sensitive draft data across reloads.
3. **Server-driven transition** on `/review → /checkout/confirmation/:orderId`. The "Place Order" click is destructive-equivalent — server creates order, returns orderId, frontend navigates via `replaceState` (not push — prevents back-to-review after order creation, mirrors `wi-cancel-4-url-stability-spec.md` R1 single-use principle).
4. **No modal divergence:** every checkout step focuses `{step}-status-heading` (h1) on terminal-error. This is the page-level rule from `focus-and-live-region-policy.md` §3. There is no `modal-hybrid` surface in checkout — the cancel-modal-unmount-lifecycle pattern (bundle 5, cbb6ac0d) does NOT apply.

### Branch resolution for QA bundle 5 §6.2

Per QA's plan, the page-vs-modal branch in §6.2 bundle 5 is now **resolved to page**:

| Surface | surfaceKind | Mount lifecycle | Terminal focus rule |
|---|---|---|---|
| CartPage | page | always mounted | `cart-status-heading` (h1) |
| CheckoutDetailsPage | page | always mounted | `checkout-details-status-heading` (h1) |
| CheckoutPaymentPage | page | always mounted | `checkout-payment-status-heading` (h1) |
| CheckoutReviewPage | page | always mounted | `checkout-review-status-heading` (h1) |
| ConfirmationPage | page | always mounted | `confirmation-heading` (h1) — already shipped (commit f0bd8ce) |

All five rows are `surfaceKind: page` per `surfaceConfigInvariant` schema (your bundle 5 cbb6ac0d). No `modalUnmountsOn`, no `inlineStatusFocusRule`. Clean.

### Place-Order click — destructive-action guard

The Place Order CTA on `/checkout/review` is destructive-equivalent (charges money, holds inventory). Apply the cancel-retry-CTA-requires-explicit-click rule from your bundle 3:

- Button must be a real `<button>` with explicit click handler. NO Enter-on-focused-heading activation.
- After click: button enters `loading` state with `aria-busy="true"`, becomes non-interactive until server responds.
- On server `success` → `replaceState` to `/checkout/confirmation/:orderId`, no history entry for `/review` after order creation.
- On server `failure` (mapped code) → inline error in `checkout-review-status-inline`, focus moves to `checkout-review-status-heading` (h1), button re-enables.

### State machine for checkout steps (mirrors poll-state enum)

Each step page uses `poll-state` / `data-state` with the **identical enum** to ConfirmationPage/RefundModal/CancelModal:

```
idle | pending | terminal-success | terminal-error | reconciliation_delayed
```

The Review → Confirmation server call uses `pending` during submit, `terminal-success` on redirect prep, `terminal-error` on mapped failure. `reconciliation_delayed` only applies to the confirmation page polling (already specced in `checkout-confirmation-a11y-spec.md`).

**Single source of truth for the enum holds across all 5 contract-frozen surfaces** (ConfirmationPage, RefundModal, CancelModal, plus the 5 checkout surfaces). 8 surfaces, 1 enum. No drift.

---

## Q-CO-9 — RESOLVED: Address validation async, provider TBD, allowlist-driven field-error envelope

**Decision:** **Asynchronous server-side address validation. Provider selection deferred to app-dev (non-blocking for UX contract). Field-error envelope is allowlist-driven; unmapped codes emit telemetry, never surface raw.**

### Validation timing (Orin + Pell)

1. **On-blur per field** (debounced 400ms): format-only client checks (e.g., postal-code regex per country). NO server call. Failures surface as `aria-invalid="true"` on the field with `aria-describedby` pointing to `{field-id}-error`.
2. **On-submit** (Continue → /checkout/payment): server call to `POST /checkout/details/validate` returning `200 { valid: true }` or `422 { fieldErrors: [{ field, code, suggestion? }] }`.
3. **Pending state** during async validation: submit button `aria-busy="true"`, page `poll-state="pending"`, live region announces *"Validating address."* (polite, throttled per focus-policy §4).

### Field-error envelope contract (frozen for UX)

```typescript
type AddressValidationErrorCode =
  | 'POSTAL_CODE_INVALID'
  | 'POSTAL_CODE_MISMATCH_COUNTRY'
  | 'STREET_NOT_FOUND'
  | 'CITY_NOT_FOUND'
  | 'COUNTRY_UNSUPPORTED'
  | 'TRAVELER_NAME_INVALID'  // includes special-char and length
  | 'PHONE_INVALID'
  | 'EMAIL_INVALID';

interface FieldError {
  field: 'street' | 'city' | 'postalCode' | 'country' | 'travelerFirstName' | 'travelerLastName' | 'phone' | 'email';
  code: AddressValidationErrorCode;
  suggestion?: string;  // optional server-provided correction
}
```

**8 mapped codes, 8 fields.** Mirrors refunds-v1 4-code allowlist discipline (`wi-refund-3-ux-spec.md` §6).

### Unmapped code handling

If server returns a `code` not in the allowlist:
- Emit telemetry: `checkout.address_validation.unmapped_code` with `{ code, field }`
- Render generic copy: *"This field has an error. Please review and try again."*
- Focus moves to the field
- **Never surface the raw code string to the user.** Same SEC discipline as refunds `refund.failure_reason_unmapped` (`wi-refund-3-ux-spec.md` §6.4).

### Focus on validation failure

- **Single field error:** focus moves to that field, `aria-invalid="true"`, `aria-describedby={field}-error`. Live region (polite) announces *"{Field name} has an error: {message}."*
- **Multiple field errors:** focus moves to `checkout-details-status-heading` (h1), summary list rendered below with anchor links to each invalid field. Live region announces *"{N} fields need attention."* Anchor links use `<a href="#{field-id}">` and trigger focus when clicked. Per `focus-and-live-region-policy.md` §3 (terminal-error focus rule, page-level surface).
- **Suggestion present:** render *"Did you mean: {suggestion}?"* with an `Apply` button next to the field. Apply replaces field value, re-runs on-blur validation.

### Provider selection — non-blocking for UX

Provider (Google Places API, Loqate, SmartyStreets, Mapbox, etc.) is an **app-dev decision** with no UX-contract impact as long as the response envelope conforms to the schema above. UX-side test mocks can use the envelope directly without an integration. Flag back to app-dev: choose a provider that supports per-country postal validation and ideally returns `suggestion` for STREET_NOT_FOUND.

---

## §4 Testid Contract — Reviewed and Amended

Your 30+ proposed testids align with the cancel-modal 12-testid surface. Below are the **frozen UX testid contracts** for each checkout page, organized to satisfy `focus-and-live-region-policy.md` and the `surfaceConfigInvariant` schema.

### §4.1 CartPage (`/cart`)

```
cart-page
cart-line-item-{id}
cart-line-item-{id}-remove-button
cart-line-item-{id}-quantity-input
cart-subtotal
cart-checkout-button
cart-empty-state
cart-status-inline           ← all states
cart-status-heading          ← h1, focus target on terminal-error
poll-state (data-state attr) ← idle | pending | terminal-error
live-region-status           ← polite, sr-only
live-region-error            ← role=alert, sr-only mirror
```

### §4.2 CheckoutDetailsPage (`/checkout/details`)

```
checkout-details-page
checkout-details-form
checkout-details-traveler-firstname
checkout-details-traveler-lastname
checkout-details-email
checkout-details-phone
checkout-details-street
checkout-details-city
checkout-details-postal-code
checkout-details-country
checkout-details-field-error-{field}    ← per-field error (aria-describedby target)
checkout-details-error-summary          ← multi-field summary (renders on terminal-error)
checkout-details-continue-button
checkout-details-status-inline
checkout-details-status-heading         ← h1, focus target on terminal-error or multi-field error
poll-state
live-region-status
live-region-error
```

### §4.3 CheckoutPaymentPage (`/checkout/payment`)

```
checkout-payment-page
checkout-payment-provider-iframe        ← provider-owned, focus/a11y NOT in our contract
checkout-payment-iframe-loading         ← shown while iframe loads
checkout-payment-iframe-error           ← shown if iframe fails to load
checkout-payment-saved-method-{id}      ← list of tokenized saved methods
checkout-payment-add-new-button
checkout-payment-billing-same-as-shipping-checkbox
checkout-payment-billing-fieldset       ← only when checkbox unchecked
checkout-payment-continue-button
checkout-payment-back-button            ← explicit back to /details
checkout-payment-status-inline
checkout-payment-status-heading         ← h1
poll-state
live-region-status
live-region-error
```

**Critical:** Provider iframe focus management is out-of-scope for our axe/NVDA tests. The iframe boundary is opaque. We test that focus enters the iframe on tab and exits on tab-out (via JS focus event listeners), nothing more. Document this as an explicit out-of-scope item in QA §7.

### §4.4 CheckoutReviewPage (`/checkout/review`)

```
checkout-review-page
checkout-review-order-summary
checkout-review-traveler-details
checkout-review-payment-method-display  ← shows last-4 only, NO raw PAN/token
checkout-review-edit-details-link       ← back to /details
checkout-review-edit-payment-link       ← back to /payment
checkout-review-terms-checkbox
checkout-review-place-order-button      ← DESTRUCTIVE-equivalent, explicit-click rule
checkout-review-status-inline
checkout-review-status-heading          ← h1, focus target on server failure
poll-state
live-region-status
live-region-error
```

**Place-order CTA discipline (mirrors bundle 3 cancel-retry rule):**
- Real `<button type="submit">`, NEVER an `<a>` or div
- NO Enter-on-heading triggers submit
- `aria-disabled="true"` until terms checkbox checked (NOT `disabled` — keep focusable for AT discoverability per `focus-and-live-region-policy.md` §6)
- On click: `aria-busy="true"`, page enters `poll-state="pending"`, live region announces *"Placing your order."*

### §4.5 ConfirmationPage (`/checkout/confirmation/:orderId`) — ALREADY SHIPPED

Existing testids from commit f0bd8ce (`ConfirmationPage.tsx`):

```
confirmation-page
confirmation-heading                    ← h1, already shipped, both loading + terminal
confirmation-order-id                   ← masked-display per checkout-confirmation-a11y-spec
confirmation-status-inline
confirmation-retry-button               ← retryable failure only
confirmation-contact-support-link
poll-state (data-state)
live-region-status                      ← already shipped (commit 2ef0354)
live-region-error                       ← already shipped (commit 2ef0354)
```

No new testids needed — your existing patches stand. Confirmation is page-level (heading focus on terminal-error), NOT a modal.

### §4.6 Cross-surface invariants

All 5 checkout pages MUST satisfy:
1. **Exactly one** `live-region-status` (polite) per page
2. **Exactly one** `live-region-error` (assertive, role=alert) per page, only present in terminal-error state (or as sr-only mirror per commit 2ef0354 pattern)
3. **Exactly one** `{step}-status-heading` as `<h1>` per page
4. **Exactly one** `poll-state` element per page with `data-state` attribute in the frozen enum
5. **NO** `{step}-modal` testids — no modals in checkout v1

Add as discipline gates in QA bundle 5:
```bash
grep -rE "checkout-.*-modal[^-]" src/        # must be empty
grep -rE "level: 2.*checkout-.*-heading" tests/e2e/checkout/  # must be empty (no h2 step headings)
```

---

## §5 State Machine — Confirmed Aligned

QA proposed state enum already matches our 8-surface contract:
```
idle | pending | terminal-success | terminal-error | reconciliation_delayed
```

**ADDITIONAL CHECKOUT-SPECIFIC STATE (validation only, on /details):**

```
validating  ← async address validation in flight; like pending but specific to field-level
```

This is a `/details`-only sub-state. Use `poll-state="pending"` for the page-level `data-state` attribute (preserving the 5-value enum), but expose `data-validation-state="validating"` on the form element for finer-grained QA assertions. Keeps the cross-surface enum frozen.

---

## §6.2 Bundle 7 — Back-button navigation, URL-stability rules adopted

Cass's `wi-cancel-4-url-stability-spec.md` §12 rules apply to checkout with these mappings:

| Cancel R# | Checkout adaptation |
|---|---|
| R1 (single-use deep-link, replaceState) | Place Order → `/confirmation/:orderId` uses `replaceState`, not `pushState`. No back-to-review after order creation. |
| R2 (consumption on action) | N/A (no deep-link param in checkout v1) |
| R3 (modal-open no history push) | N/A (no modals) |
| R4 (modal-unmount no URL mutation) | N/A (no modals) |
| R5 (inline-status no URL mutation) | Field validation errors do NOT mutate URL. Page-level errors do NOT mutate URL. ✓ |
| R6 (refresh during pending = state lost, server source) | Refresh during `/review` submit → re-fetch order state via `checkoutSessionId`; server decides resume point. ✓ |
| R7 (refresh on terminal = server-driven) | Refresh on `/confirmation/:orderId` → already specced in `checkout-confirmation-a11y-spec.md`. ✓ |
| R8 (Back during modal-open closes) | N/A |
| R9 (Back after unmount = standard) | Back from `/review` → `/payment` is standard browser back. Cart and draft state preserved via `checkoutSessionId`. ✓ |
| R10 (deep-link ineligible = inline message) | N/A v1; defer for v2 "resume cart" deep-link |

**Critical back-button safety rules for checkout v1:**

1. Back from `/payment` → `/details`: form state restored from `checkoutSessionId`. No data loss. Already specced in `checkout-design-spec.md` IA section.
2. Back from `/review` → `/payment`: payment method selection restored; tokens still valid (15-min inventory hold per checkout-design-spec).
3. Back from `/confirmation/:orderId` → blocked by `replaceState` discipline above; user cannot reach `/review` to re-submit.
4. Refresh on `/confirmation/:orderId` after `replaceState` → already safe via server-driven render (confirmation-a11y-spec).

### 6 e2e test hooks for bundle 7

1. `checkoutReviewSubmit_navigatesViaReplaceState_notPushState` — assert `history.length` unchanged across submit→confirmation
2. `confirmation_backButton_doesNotReturnToReview` — back from /confirmation lands on /payment (or earlier), never /review
3. `paymentBack_restoresDetailsFormState` — back from /payment restores all 8 traveler/address fields
4. `reviewBack_restoresPaymentSelection` — back from /review restores saved payment method selection
5. `checkoutAnyStep_refresh_preservesSessionState` — refresh on /details, /payment, /review preserves draft via `checkoutSessionId`
6. `placeOrder_serverError_doesNotMutateUrl` — terminal-error on /review keeps URL at /checkout/review, no redirect

### Discipline gates for bundle 7

```bash
grep -rE "pushState.*confirmation|confirmation.*pushState" src/checkout/   # must be empty
grep -rE "navigate\(.*confirmation.*\)" src/checkout/ | grep -v "replace: true"  # must be empty
grep -rE "history\.back\(\).*checkout|checkout.*history\.back" src/checkout/    # must be empty (no programmatic back)
```

---

## §7 Out-of-scope items (QA, please absorb)

1. **Payment provider iframe internals** — focus inside iframe, AT announcements inside iframe, iframe form validation. Provider owns this. Test only that focus enters/exits iframe via tab.
2. **Email confirmation deep-link** — already specced in `checkout-confirmation-a11y-spec.md` with JWT discipline. Out of scope for checkout-flow test plan; covered by confirmation page tests.
3. **Cart mutation during checkout** — if user opens cart in another tab and modifies, payment+review snapshots invalidate (per accumulated knowledge). Edge case; flag for v2 dedicated test bundle.

---

## §8 Telemetry events (UX-frozen, mirror refunds/cancel pattern)

```
checkout.cart.viewed
checkout.details.started
checkout.details.field_validation_failed       { field, code }
checkout.details.address_validation.unmapped_code  { code, field }  ← SEC discipline
checkout.details.completed
checkout.payment.started
checkout.payment.method_selected               { method_type }
checkout.payment.iframe_load_failed
checkout.payment.completed
checkout.review.viewed
checkout.review.place_order_clicked
checkout.review.place_order_succeeded
checkout.review.place_order_failed             { code }
checkout.review.place_order_failure_reason_unmapped  { code }  ← SEC discipline
checkout.confirmation.viewed                   ← already exists
```

15 events total. Mirrors the 8-event refunds funnel pattern. `_unmapped` variants ensure server-side drift surfaces in analytics, never in UI.

---

## §9 Open questions to app-dev (passed through QA)

1. **Q-CO-DEV-1:** Address validation provider selection (UX-agnostic, but please confirm the envelope schema in §Q-CO-9 above is implementable with your chosen provider).
2. **Q-CO-DEV-2:** `checkoutSessionId` server-side TTL — design assumes ≥30 min (matches JWT session timeout per accumulated knowledge). Confirm.
3. **Q-CO-DEV-3:** On Place Order server failure, does server persist the attempt (allowing client retry with idempotency key)? UX assumes yes — retry button on terminal-error needs idempotency to avoid double-charge.

---

## §10 v2 punts (prevents scope creep, mirrors refunds v1 §9)

1. Saved address autocomplete from user profile
2. Multi-traveler form (group bookings)
3. Split-payment (multiple payment methods per order)
4. Gift card / promo code field
5. "Resume cart" email deep-link (would require R10 deep-link consumption pattern from cancel-spec §12)
6. In-checkout chat support
7. Browser autofill testing matrix beyond Chromium/Firefox/WebKit
8. RTL layout (Arabic, Hebrew) — design tokens support but no v1 e2e coverage
9. Currency switcher mid-checkout
10. Apple Pay / Google Pay native sheets (provider iframe only in v1)

---

## §11 Apply summary for QA

- **Q-CO-2:** RESOLVED — multi-step, client-driven, page-level (no modals). All 5 checkout surfaces are `surfaceKind: page`. §6.2 bundle 5 branches to page-level focus uniformly.
- **Q-CO-9:** RESOLVED — async on-submit validation, allowlist envelope (8 codes/8 fields), unmapped emits telemetry.
- **§4 testids:** Reviewed, expanded with field-level testids for /details and provider-iframe boundary testids for /payment. ConfirmationPage testids unchanged (already shipped).
- **§5 state machine:** Confirmed enum aligned. Added `data-validation-state` as /details-only sub-attribute.
- **§6.2 bundle 7:** URL-stability rules from cancel §12 mapped to checkout. 6 e2e hooks + 3 discipline gates specified.

Bundle budget impact on frontend: estimated 6-8KB gz total across 5 checkout pages (1.5KB avg per page). Within reasonable limits.

— Cass, Iris, Orin, Vela, Pell

> "Page-level surfaces from end to end. The cancel-modal-hybrid lesson was about cancel, not a pattern to spread." — Iris

> "Single state enum across 8 surfaces. Drift = test failure. The grammar holds." — Cass

> "Provider iframe is opaque; our contract stops at its border. Document the seam, don't pretend to test inside." — Orin

> "Field-level errors focus the field; multi-field errors focus the heading. Same rule we've been writing for six surfaces." — Pell

> "ReplaceState on /review→/confirmation is the destructive-action URL guard. Same shape as cancel R1." — Vela
