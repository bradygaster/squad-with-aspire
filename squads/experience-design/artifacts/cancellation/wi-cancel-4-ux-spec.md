# WI-CANCEL-4 — Cancel Order UX Spec (v1)

**Status:** Design-frozen (pending SPM v1 100% before frontend work begins)
**Owner:** experience-design-squad (Iris UX Lead, Vela Visual/Interaction, Orin a11y, Cass IA)
**Date:** 2026-06-24
**Squad:** experience-design
**Branch:** tamir/squad-fixes
**Consumes contracts:** DR-CANCEL-001, DR-CANCEL-002, DR-CANCEL-003 (rate-cap refund-on-reject), `CancelErrorEnvelope.Codes`
**Mirrors patterns from:** `refunds/wi-refund-3-ux-spec.md`, `focus-and-live-region-policy.md`, WI-REFUND-7 RefundModal

---

## 1. Scope

**In scope (v1):**
- Cancel-order CTA on confirmed-order surfaces (confirmation page, order-detail page).
- Modal-based confirm flow, destructive-action guard.
- 4 UI states: `idle | confirming | pending | terminal-success | terminal-error`.
- Server-driven visibility via `order.eligibleActions` (no client clock checks).
- Polling via `usePollingResource` (5s interval / 60s cap / 12-poll cap — same generalized hook as refunds).
- Provider-rejection terminal-state UX (per DR-CANCEL-003 R4': order returns to `Confirmed`, refund CTA becomes immediately available).
- WCAG 2.2 AA incl. new SCs 2.4.11 (focus not obscured), 2.5.8 (target size 24×24), 3.2.6 (consistent help).

**Out of scope (v2-punted):**
1. Partial cancellation (single traveler / single leg).
2. Cancellation-fee preview before confirm.
3. Reason-required dropdown (provider analytics ask).
4. Bulk cancel across multiple orders.
5. Schedule-cancel-at-future-time.
6. In-app messaging if cancel is rejected (email-only v1).
7. Localization / RTL (deferred with refunds).
8. Cancel-on-behalf-of (CSR tool surface).
9. Animated state transitions beyond fade (reduced-motion baseline).
10. Inline "are you sure?" without modal on mobile (modal is universal in v1).

---

## 2. Entry points

| Surface | CTA placement | Visibility rule |
|---|---|---|
| `/checkout/confirmation/:orderId` | Below order summary, secondary button | `order.eligibleActions.includes("cancel")` |
| `/account/orders/:orderId` (post-canary) | Order header, secondary destructive button | Same |
| Email "Manage order" deep-link | Lands on confirmation page → CTA visible if eligible | Same |

**No CTA when not eligible.** Absence (not disabled state) per refunds v1b pattern. Disabled buttons leak intent + create false-affordance.

---

## 3. Component inventory

### 3.1 `CancelOrderButton`
- Secondary button, neutral color (NOT destructive red — destructive is for the modal Confirm only).
- Label: **"Cancel order"** (sentence case, no ellipsis — modal is not a settings dialog).
- `data-testid="cancel-trigger-button"` (mirrors `refund-trigger-button`).
- Minimum target 44×44 (above 24×24 floor per SC 2.5.8).
- Renders only when `eligibleActions.includes("cancel")` — never disabled placeholder.

### 3.2 `CancelOrderModal`
- Trigger: click on `CancelOrderButton`.
- Variant: destructive-confirm dialog.
- **Heading (h2):** "Cancel this order?"
- **Body copy:**
  > "We'll request cancellation with the provider. If accepted, your booking is canceled and we'll start a refund. This can take up to 24 hours to confirm."
- **Buttons (in DOM order — Cancel FIRST, Confirm LAST):**
  - **Cancel** — primary visual weight, `autofocus`, `data-testid="cancel-modal-cancel-button"`, closes modal, restores focus to trigger.
  - **Confirm cancellation** — destructive (red), `data-testid="cancel-modal-confirm-button"`. Submits POST.
- **Dismiss paths (all = Cancel):**
  - `Esc` key
  - Browser Back button (pushState/popstate, same as RefundModal)
  - Click outside modal (overlay click)
  - Cancel button
- **Focus trap** during open; **focus restored** to trigger on close.
- `background = inert` (NOT `aria-hidden` — inert prevents focus AND removes from a11y tree without breaking AT navigation).
- `data-testid="cancel-modal"` on dialog root.

### 3.3 `CancelStatusInline`
- Inline status block, replaces the trigger button area after submit.
- **5 states:**

| State | Visual | Live region | Focus |
|---|---|---|---|
| `idle` | Trigger button visible | — | — |
| `confirming` | Modal open | — | Modal Cancel (autofocus) |
| `pending` | Inline status: "Cancellation requested. Confirming with provider…" + spinner | polite | NO focus move (live region only) |
| `terminal-success` | Inline status: "Order canceled. A refund is being processed." Refund CTA appears below. | polite | NO focus move (live region only) — per focus policy §3 happy-path rule |
| `terminal-error` | Inline status: "Cancellation failed: {message}." Retry button. | assertive (role=alert) mirror + h1-equivalent focus target | Focus moves to status heading |

- `data-testid="cancel-status-inline"` on wrapper.
- `data-testid="poll-state"` with `data-state="pending|terminal-success|terminal-error|idle"` (per QA enforcement bundle).
- `data-testid="live-region-status"` (polite).
- `data-testid="live-region-error"` (role="alert" sr-only mirror in terminal-error).

---

## 4. State machine (UI side)

```
idle ──(click trigger)──▶ confirming
confirming ──(Cancel/Esc/Back/outside)──▶ idle [focus → trigger]
confirming ──(Confirm)──▶ pending [POST /orders/:id/cancel]
pending ──(202 + poll: cancel.accepted)──▶ terminal-success
pending ──(202 + poll: cancel.rejected_by_provider OR DR-003 R4')──▶ terminal-error [+ refund CTA becomes available]
pending ──(poll cap 60s/12)──▶ reconciliation_delayed (treated as pending visually + advisory copy)
pending ──(immediate 4xx)──▶ terminal-error
```

**Mapped error codes** (from `CancelErrorEnvelope.Codes` — never construct strings locally):

| Code | User-facing copy | Retry? |
|---|---|---|
| `ORDER_NOT_CANCELLABLE` + `reason:"already_canceled"` | "This order is already canceled." | No |
| `ORDER_NOT_CANCELLABLE` + `reason:"already_refunded"` | "This order has already been refunded." | No |
| `ORDER_NOT_CANCELLABLE` + `reason:"window_expired"` | "The cancellation window for this order has passed." | No |
| `ORDER_NOT_CANCELLABLE` + `reason:"fulfillment_in_progress"` | "We can't cancel right now — your booking is being fulfilled. Try again in a few minutes." | Yes (after delay) |
| `REQUEST_IN_FLIGHT` + `operation:"refund"` | "A refund is already in progress for this order. Cancel isn't available." | No |
| `REQUEST_IN_FLIGHT` + `operation:"cancel"` | "Cancellation is already in progress." | No (auto-poll resumes) |
| `RATE_LIMITED` | "Too many requests. Please try again in a moment." | Yes |
| `cancel.rejected_by_provider` (terminal via poll, DR-003) | "The provider couldn't cancel this booking. You can request a refund instead." | No — refund CTA shown |
| **Unmapped code** | Generic: "Cancellation failed. Please try again or contact support." | Yes + telemetry: `cancel.failure_reason_unmapped` |

**Telemetry hook for unmapped codes:** mirrors `refund.failure_reason_unmapped`. App-dev never adds codes silently; new codes require a DR.

---

## 5. Accessibility contract

Inherits `squads/experience-design/artifacts/focus-and-live-region-policy.md` in full. Cancel-specific clarifications:

1. **Cancel button (modal) is the safe default.** Confirm button is destructive — `autofocus` lands on Cancel per refunds v1b pattern. **Never autofocus a destructive action.**
2. **Esc + Back = Cancel.** Both restore focus to trigger. No navigation. (Forbidden-patterns spec FP-04 — focus trap without restore — fails CI if regressed.)
3. **One polite live region + one assertive (role=alert) max** per page. Cancel inline status reuses the page's existing live regions when on confirmation page (don't add duplicates). Standalone surfaces (account/orders) add their own.
4. **`aria-busy` on inline status** during pending; cleared on terminal. FP-07 forbids stuck `aria-busy`.
5. **No focus move on pending → terminal-success.** Live region only. (Refunds parity.)
6. **Focus moves to status heading on terminal-error.** Role=alert sr-only mirror provides assertive announcement. (Same dedup pattern as ConfirmationPage / RefundModal — see chat history 2026-06-23 22:43.)
7. **Reduced-motion:** spinner respects `prefers-reduced-motion: reduce` — switches to pulsing dot or static skeleton.
8. **Forced-colors:** all state colors fall back to system colors; rely on text + iconography, not color-only.
9. **Target size:** all interactive elements ≥ 24×24 CSS px (SC 2.5.8). Trigger button and modal buttons ≥ 44×44.
10. **Heading order:** modal h2 does NOT compete with page h1 (FP-08 guard). Modal heading is dialog-scoped.

---

## 6. QA test-hook contract (frozen for WI-CANCEL-4)

Per the binding note in `qa-testhook-patches.md` and QA's `focus-live-region-enforcement` bundle, the cancel surface MUST emit:

| testid | Element | State |
|---|---|---|
| `cancel-trigger-button` | Cancel CTA | All |
| `cancel-modal` | Dialog root | `confirming` |
| `cancel-modal-cancel-button` | Cancel button in modal | `confirming` |
| `cancel-modal-confirm-button` | Confirm button in modal | `confirming` |
| `cancel-status-inline` | Inline status wrapper | `pending|terminal-*|reconciliation_delayed` |
| `cancel-status-heading` | Status h-element (focus target on terminal-error) | `terminal-error` |
| `cancel-retry-button` | Retry button | `terminal-error` (retryable codes only) |
| `cancel-error-message` | Inner error-copy span | `terminal-error` |
| `poll-state` (with `data-state`) | sr-only span mirroring state machine | `pending|terminal-success|terminal-error|reconciliation_delayed|idle` |
| `live-region-status` | Polite live region | All (page-level, reused) |
| `live-region-error` | role=alert sr-only mirror | `terminal-error` only |

**Once frontend ships:** uncomment the `cancel-modal` page entry in QA's `pagesUnderTest` (per `focus-live-region.spec.ts` and `forbidden-patterns.spec.ts`). The 10-pattern enumeration guard and decision-matrix tests apply automatically.

---

## 7. IA / routing (Cass)

- **No new routes.** Cancel is a modal in-place + inline status — mirrors refunds v1.
- **URL state:** `?cancel=open` is **NOT** added on modal open. Modal is ephemeral session UI; deep-linking into "modal open" is a v2 ask (and complicates Back-button = Cancel semantics).
- **Browser Back during pending poll:** does NOT cancel the in-flight POST. The request is server-accepted (202); leaving the page does not retract it. Returning to the order shows the current state via fresh fetch.
- **Email deep-link "Cancel my order":** lands on confirmation page with `?action=cancel` query param. Page reads param, opens modal IF `eligibleActions.includes("cancel")`. Param is consumed (removed via `replaceState`) so refresh doesn't reopen.
- **Refund-after-rejection path:** when cancel terminal-error is specifically `cancel.rejected_by_provider`, the refund CTA appears inline below the error message. Server confirms `eligibleActions` updates via the same poll response — frontend does NOT assume eligibility client-side.

---

## 8. Visual / tokens (Vela)

| Token | Value | Use |
|---|---|---|
| `--cancel-trigger-bg` | `transparent` | Cancel button (neutral) |
| `--cancel-trigger-border` | `var(--color-neutral-400)` | Cancel button border |
| `--cancel-confirm-bg` | `var(--color-error-600)` = `#DC2626` | Destructive Confirm button |
| `--cancel-confirm-bg-hover` | `var(--color-error-700)` | Hover |
| `--cancel-status-pending-bg` | `var(--color-info-50)` | Pending banner |
| `--cancel-status-success-bg` | `var(--color-success-50)` | Success banner |
| `--cancel-status-error-bg` | `var(--color-error-50)` | Error banner |
| `--modal-overlay` | `rgba(0,0,0,0.5)` | Overlay (forced-colors override: `Canvas` with `opacity:0.7`) |

Reuses existing checkout/refunds design tokens. No new primitives.

---

## 9. Telemetry events

| Event | When | Properties |
|---|---|---|
| `cancel.cta_viewed` | Trigger button rendered | `orderId`, `surface` (confirmation\|account) |
| `cancel.modal_opened` | Modal shown | `orderId` |
| `cancel.modal_dismissed` | Esc/Back/outside/Cancel | `orderId`, `method` (esc\|back\|overlay\|button) |
| `cancel.submitted` | POST initiated | `orderId` |
| `cancel.succeeded` | terminal-success | `orderId`, `pollCount`, `elapsedMs` |
| `cancel.failed` | terminal-error | `orderId`, `errorCode`, `reason?` |
| `cancel.failure_reason_unmapped` | Unknown code received | `orderId`, `errorCode`, `reason?` |
| `cancel.reconciliation_delayed` | Poll cap hit | `orderId` |
| `cancel.refund_offered` | Refund CTA shown after `cancel.rejected_by_provider` | `orderId` |

All events go through `window.telemetry.track` via the same `safeTrack` wrapper used in WI-REFUND-7 (interceptable for tests).

---

## 10. Frontend implementation notes (for WI-CANCEL-7, blocked until SPM v1)

- **Hook reuse:** consume `usePollingResource` directly. New hook contract is frozen by WI-REFUND-7.
  - `interval: 5000`, `capMs: 60000`, `maxPolls: 12`
  - `terminalStates: ["canceled", "cancel_rejected", "failed"]`
  - `selectState`: extract from `order.cancelStatus` (app-dev contract — confirm exact field name in WI-CANCEL-1 backend).
- **State machine** lives in `CancelOrderModal` consumer; hook stays data-only.
- **`poll-state` testid** derived in JSX consumer (4 lines, copy-paste from RefundModal pattern — see `qa-testhook-patches.md`).
- **Bundle budget:** ≤ 4KB gz (matches refund modal). Reuse hook + reuse focus-trap utility — no new deps.
- **Feature flag:** `cancel_v1_enabled` gates trigger-button render only, not endpoint. Server is source of truth for `eligibleActions`.
- **Never render** raw provider IDs (`pi_xxx`, `ch_xxx`, `re_xxx`), `providerReason`, or internal `cancelType` in DOM. GATE-CANCEL-06 + GATE-CANCEL-07 (review-deployment) enforce this in preprod grep.

---

## 11. Acceptance criteria

- [ ] CTA visibility driven by `order.eligibleActions` only — zero client clock logic.
- [ ] Modal: Cancel autofocus, focus trap, Esc + Back + overlay = Cancel, focus restored to trigger.
- [ ] All 11 testids from §6 present.
- [ ] All 9 mapped error codes from §4 produce correct copy + retry affordance.
- [ ] Unmapped codes fire `cancel.failure_reason_unmapped` telemetry.
- [ ] `cancel.rejected_by_provider` terminal state shows refund CTA inline (server-driven eligibility).
- [ ] Polling: 5s interval, 60s/12-poll cap → reconciliation_delayed.
- [ ] No focus move on pending → terminal-success (live region only).
- [ ] Focus moves to status heading on terminal-error (sr-only role=alert mirror for assertive announce).
- [ ] axe-CI clean (0 violations).
- [ ] QA bundle `focus-live-region.spec.ts` `cancel-modal` page entry uncommented and green.
- [ ] WCAG 2.2 AA verified incl. SC 2.4.11, 2.5.8, 3.2.6.
- [ ] Bundle ≤ 4KB gz.

---

## 12. Open contract questions for app-dev (block WI-CANCEL-7)

1. **Exact field name on order entity for cancel status** — `order.cancelStatus`? `order.cancellation.state`? Frontend `selectState` callback signature depends on this.
2. **Polling endpoint** — is it `GET /checkout/orders/:orderId/status` (same endpoint as confirmation) returning a richer payload, or new `GET /orders/:orderId/cancel/status`? Prefer the former — one endpoint = one hook instance per page.
3. **Refund-after-rejection eligibility surfacing** — does `eligibleActions` update in the same poll response that delivers `cancel_rejected`, or does the frontend need a separate refresh? Spec assumes same-response (atomic).

Will route these via decisions inbox once SPM v1 lands and app-dev picks up WI-CANCEL-1 backend.

---

## Squad sign-off

- **Iris (UX Lead):** Approved. Mirrors refunds v1 patterns exactly — no novel UX, low review burden. *"Constraints are the soul of design." — Charles Eames*
- **Vela (Visual/Interaction):** Tokens reuse existing. Destructive-confirm pattern proven in refunds. *"God is in the details." — Mies van der Rohe*
- **Orin (a11y):** WCAG 2.2 AA contract is non-negotiable. Focus policy compliance enforced by QA bundle. *"Accessibility allows us to tap into everyone's potential." — Debra Ruh*
- **Cass (IA):** No new routes, no URL state for ephemeral modal — keeps Back-button semantics clean. *"The details are not the details. They make the design." — Charles Eames*
- **Pell (User Research):** No-research-debt — cancel patterns are well-trodden; we'd burn cycles to re-validate. Reuses refund mental model users already learned. *"Listen to your users, but don't let them design the product." — Jakob Nielsen*
