# Issue Substitute: Confirmation Page Vertical (Post-Checkout)

**Repo:** tamirdresher/travel-assistant
**Filed by:** ideation-research-planning-squad
**Date:** 2026-06-23
**Reason for file-not-issue:** EMU blocks `gh issue create` on tamirdresher/travel-assistant. This artifact is the canonical issue; squad messages carry the dispatch.

---

## Context

The checkout vertical (PAY) is fully shipped (10/10 canary gates, 16/16 AC, GO-LIVE-DECISION.md commit `25e9f18`). The first user-visible gap surfaced post-merge:

experience-design-squad shipped `squads/experience-design/artifacts/checkout-confirmation-a11y-spec.md` which defined 5 order states + polling contract + WCAG 2.2 AA rules for the confirmation page, but identified **two open handoffs** that are not yet implemented.

This artifact closes those handoffs as the next vertical slice.

---

## Work Items

### WI-CONFIRM-1 — Order status endpoint
**Owner:** application-development-squad
**Depends on:** none (checkout vertical already live)

**Spec:**
- `GET /api/checkout/orders/{orderId}/status`
- Returns: `{ orderId, state, paymentState, fulfillmentState, updatedAt, etag }`
- States (from a11y spec): `pending` | `confirmed` | `payment_failed` | `inventory_released` | `canceled`
- AuthZ: JWT `sub` claim MUST match order owner OR `scope=order:read:any` (support role). **IDOR-safe**: 404 (not 403) when sub mismatch — leak-free per security pattern already used in checkout.
- Caching: `ETag` + `Cache-Control: private, max-age=2` (matches a11y spec's 2s polling cadence).
- Idempotency: read-only, no Idempotency-Key required.

**Acceptance:**
- [ ] Endpoint returns all 5 states correctly from `OrdersRepository`
- [ ] 404 (not 403) when caller sub ≠ order.sub and no `order:read:any` scope
- [ ] ETag changes only when state OR paymentState OR fulfillmentState changes
- [ ] Unit test: `OrderStatusEndpointTests.cs` (5 state cases + IDOR + ETag stability)
- [ ] Contract test: matches a11y-spec.md §Polling Contract response shape exactly

---

### WI-CONFIRM-2 — NVDA + VoiceOver test scripts
**Owner:** quality-testing-squad
**Depends on:** WI-CONFIRM-1 (needs live endpoint for E2E)

**Spec:**
- Scripts under `tests/a11y/checkout-confirmation/` covering:
  - NVDA on Firefox Win11 — pending→confirmed live-region announcement
  - VoiceOver on Safari macOS — same transition
  - Focus management: focus does NOT jump on state change (a11y-spec §Focus Rules)
  - Skeleton → content transition: announced as "loading complete"
- Run manually + record results to `a11y-test-results.md` (no CI automation — NVDA/VoiceOver are not headless-friendly).

**Acceptance:**
- [ ] 4 manual test scripts written (NVDA-pending, NVDA-confirmed, VO-pending, VO-confirmed)
- [ ] WCAG 2.2 AA pass on all 5 order states
- [ ] Live-region (`role="status" aria-live="polite"`) announces state changes within 1 polling cycle
- [ ] Test report template in `tests/a11y/REPORT-TEMPLATE.md`

---

### WI-CONFIRM-3 — Frontend confirmation page
**Owner:** experience-design-squad → application-development-squad (handoff)

**Spec:**
- Implement `apps/web/src/checkout/ConfirmationPage.tsx` per `checkout-confirmation-a11y-spec.md`
- Polling: `useOrderStatus(orderId)` hook — 2s interval, exponential backoff on 5xx (2s→4s→8s, cap 30s), stop on terminal states (`confirmed`, `canceled`, `payment_failed`)
- Skeleton shown for first 200ms only (avoid flash on cached responses)
- 4 new analytics events emitted (per a11y spec): `confirmation_viewed`, `confirmation_state_changed`, `confirmation_polling_stopped`, `confirmation_error_shown`
- Out of scope (separate slice): email receipt, order detail expansion, reorder CTA

**Acceptance:**
- [ ] All 5 states render per a11y spec
- [ ] Polling stops on terminal states (verified via React Testing Library)
- [ ] Playwright E2E: `confirmation.spec.ts` — pending→confirmed happy path + 404 IDOR case
- [ ] Bundle size delta ≤ 8KB gzipped

---

## Out of scope (tracked, not filed)

- Email/SMS receipt delivery
- Order history page
- Reorder / re-buy flow
- Refund self-serve
- Order modification (address, items)

These are flagged in `backlog-v0.1-state.md` as separate verticals.

---

## Dispatch

Sent via `squad_send_message`:
- → application-development-squad: WI-CONFIRM-1 (endpoint)
- → quality-testing-squad: WI-CONFIRM-2 (a11y scripts, blocked on WI-CONFIRM-1)
- → experience-design-squad: WI-CONFIRM-3 owner (will sub-delegate frontend impl)

Merge order:
1. WI-CONFIRM-1 lands first (unblocks 2 and 3)
2. WI-CONFIRM-3 and WI-CONFIRM-2 land in parallel
3. No canary required — read-only endpoint + UI behind existing checkout feature flag

— ideation-research-planning-squad
