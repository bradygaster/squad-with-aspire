# Checkout Confirmation Page — A11y & Content Spec

**Squad:** experience-design
**Authors:** Iris (UX Lead), Orin (A11y), Cass (IA), Vela (Visual)
**Date:** 2026-06-23
**Status:** Final — ships with checkout vertical
**Scope:** `/checkout/confirmation/:orderId` only. Complements `checkout-design-spec.md`.
**Pairs with:** app-dev `CheckoutEndpoints.confirm.v2.cs` (15-min `/confirm` TTL), webhook-debug-endpoint bundle.

---

## 1. Why this spec exists

Confirmation is the **only step where the user has paid but the system may still be reconciling** (webhook → ledger). The checkout spec covered the happy-path layout; this doc closes three gaps QA and security flagged in adjacent threads:

1. The page is reachable via deep link (refresh, email link, browser back). State must be derivable from `orderId` alone — no reliance on client-held `checkoutSessionId`.
2. The 422 mismatch path (idempotency cap-key reuse) needs a user-facing presentation that doesn't leak whether another user's request collided.
3. Webhook lag (payment provider → our ledger) means `orderStatus` can legitimately be `pending_reconciliation` for up to ~30s after redirect. The page must handle this without making the user think payment failed.

---

## 2. Order-status state machine (UI contract)

The page renders one of five states, derived **server-side** from the order record at request time. Client polls only for `pending_reconciliation`.

| State | Trigger | User-visible label | Visual treatment | Actions offered |
|---|---|---|---|---|
| `confirmed` | Webhook applied, ledger updated | "Booking confirmed" | ✅ success #10B981, full itinerary | View itinerary, Add to calendar, Email receipt |
| `pending_reconciliation` | Payment authorized, webhook not yet applied (<30s typical) | "Finalizing your booking" | ⏳ neutral, skeleton for itinerary block | (none — poll every 3s, max 10 polls = 30s) |
| `reconciliation_delayed` | Still pending after 30s | "Still finalizing — we'll email you" | ⏳ neutral, email-capture if guest | Email me when ready, Contact support |
| `failed_post_auth` | Webhook reported failure after redirect | "Payment captured but booking failed" | ⚠️ warning, NOT error red | Auto-refund notice, Contact support, Reference: orderId |
| `not_found_or_forbidden` | `orderId` invalid OR belongs to another `sub` | "We can't find that order" | Neutral empty state | Go to bookings, Go home |

**Critical:** `not_found_or_forbidden` returns the **same** UI for both cases — never disclose "this order exists but isn't yours." This is the IDOR mitigation security-hardening flagged for confirmation routes.

---

## 3. Polling contract (for `pending_reconciliation`)

```
GET /checkout/orders/:orderId/status
Auth: existing session cookie OR signed magic-link token from confirmation email
Response: { status: "<one of the 5 states>", retryAfterSeconds: 3 }
```

- Client polls at `retryAfterSeconds` interval, honoring the server value (back-off-capable).
- Stop on any terminal state (`confirmed`, `failed_post_auth`, `reconciliation_delayed`).
- Hard cap: 10 polls. After cap, transition UI to `reconciliation_delayed` regardless of server reply.
- Polling uses `fetch` with `AbortController` tied to page unload + visibility-change (no polls while tab hidden).

---

## 4. Accessibility requirements (WCAG 2.2 AA)

### 4.1 Live region for state transitions
- Single `aria-live="polite"` `role="status"` region at top of `<main>`.
- Updated on every state transition with the visible label from §2.
- Do NOT use `aria-live="assertive"` — confirmation transitions are not interruptive.

### 4.2 Focus management
- On initial render: focus `<h1>` (programmatic focus, `tabindex="-1"`, no visible outline change beyond browser default).
- On state transition `pending_reconciliation → confirmed`: do NOT move focus. Update live region only. Moving focus mid-read is hostile to screen-reader users.
- On state transition to `failed_post_auth`: move focus to the support-contact CTA (this IS interruptive — user needs to act).

### 4.3 Skeleton states
- Skeleton blocks for itinerary use `aria-busy="true"` on the container, `aria-hidden="true"` on the shimmer divs.
- Screen readers announce "Loading itinerary" via visually-hidden text inside the container.

### 4.4 Order reference
- `orderId` is rendered with `<code>` and a copy-to-clipboard button.
- Copy button: `aria-label="Copy order reference {orderId}"`, success state announced via `aria-live="polite"`.
- Reference is also embedded in the `<title>` so browser history is meaningful: `Order {short-id} — Travel Assistant`.

### 4.5 Color & contrast
- `pending_reconciliation` and `reconciliation_delayed` use neutral grey #6B7280 on white (contrast 4.83:1 ✅).
- `failed_post_auth` uses warning #D97706 with bold weight, NOT error red — color-blind users still get the icon + label.
- No color-only signaling anywhere; every state has icon + label + treatment.

### 4.6 Reduced motion
- Skeleton shimmer respects `prefers-reduced-motion: reduce` → static grey blocks.
- Success checkmark animation respects same → static checkmark.

---

## 5. Information architecture

### 5.1 URL & history
- Canonical: `/checkout/confirmation/:orderId` (matches checkout-design-spec §6).
- Refresh-safe: server renders current state on every GET.
- Back button from confirmation: returns to bookings list (NOT to `/checkout/review` — that route is invalid post-confirmation and returns 410 Gone).
- Deep link from email: same URL, requires auth OR signed token query param `?t=<jwt>` (15-min TTL, single-use, audience-locked to orderId).

### 5.2 Indexability
- `<meta name="robots" content="noindex, nofollow">` — confirmation pages must never appear in search results.

### 5.3 Page title pattern
- `confirmed`: `Booking confirmed — Order {short-id}`
- `pending_reconciliation`: `Finalizing booking — Order {short-id}`
- `reconciliation_delayed`: `Booking pending — Order {short-id}`
- `failed_post_auth`: `Action needed — Order {short-id}`
- `not_found_or_forbidden`: `Order not found`

---

## 6. Content strings (final, no rewrites)

| Key | Copy |
|---|---|
| `confirmation.confirmed.h1` | Your booking is confirmed |
| `confirmation.confirmed.subtitle` | We've emailed your itinerary to {email}. |
| `confirmation.pending.h1` | Finalizing your booking |
| `confirmation.pending.body` | Your payment was authorized. We're confirming availability with the provider — this usually takes a few seconds. |
| `confirmation.delayed.h1` | We're still finalizing your booking |
| `confirmation.delayed.body` | This is taking longer than usual. Your payment is safe — we'll email you within 5 minutes once it's confirmed. |
| `confirmation.delayed.cta.email` | Email me when ready |
| `confirmation.failed.h1` | Payment captured — booking needs attention |
| `confirmation.failed.body` | Your card was charged but we couldn't complete the booking. Our team has been notified and will refund or rebook within 24 hours. Reference: {orderId} |
| `confirmation.failed.cta.support` | Contact support |
| `confirmation.notfound.h1` | We can't find that order |
| `confirmation.notfound.body` | The link may have expired or belong to a different account. |
| `confirmation.notfound.cta.bookings` | Go to my bookings |

**Locale note:** All strings keyed for i18n; no string concatenation in views. `{email}`, `{orderId}`, `{short-id}` are interpolation tokens, not template literals.

---

## 7. Analytics events (additions to checkout-design-spec §9)

| Event | Properties | When |
|---|---|---|
| `confirmation_viewed` | `orderId`, `initialState`, `arrivedVia` (redirect/deeplink/refresh) | First render |
| `confirmation_state_resolved` | `orderId`, `finalState`, `pollCount`, `elapsedMs` | Terminal state reached |
| `confirmation_delayed_email_optin` | `orderId` | User clicks "Email me when ready" |
| `confirmation_support_clicked` | `orderId`, `state` | Support CTA clicked |

PII: never log `email` as event property — use `orderId` only. Email lives in the order record, retrievable by support tooling.

---

## 8. Handoffs

- **application-development:** `CheckoutEndpoints.confirm.v2.cs` already returns 200 with order body; needs companion `GET /checkout/orders/:orderId/status` endpoint per §3. Bennett — please confirm this exists in the wi6-redis-di-reconcile bundle, or treat as a follow-up work item post-canary.
- **quality-testing:** Add NVDA + VoiceOver scripts for the `pending_reconciliation → confirmed` transition (verify focus does NOT move, live region announces new label). Add axe rule for `aria-busy` on skeleton container.
- **security-hardening:** Confirm signed-token email-link contract (§5.1): JWT, 15-min TTL, single-use (server-side jti revocation), audience = `orderId`. If the contract differs, this spec defers to your decision.
- **review-deployment:** No deploy-gate dependency. Ships with the checkout canary.

---

— Iris (UX Lead) · Orin (A11y) · Cass (IA) · Vela (Visual)

> "The page after payment is the page users remember. Make every state honest." — Iris
> "Focus is not a notification system." — Orin
> "Deep links are a feature, not a bug. Plan for them." — Cass
> "Neutral is a color too." — Vela
