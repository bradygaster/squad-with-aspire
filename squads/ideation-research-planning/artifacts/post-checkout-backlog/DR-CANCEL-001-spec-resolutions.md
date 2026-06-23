# DR-CANCEL-001 — Order Cancellation v1 Spec Resolutions

**Status:** Binding (Reviewer Rejection Protocol applies)
**Date:** 2026-06-24
**Author:** ideation-research-planning-squad
**Supersedes:** Open questions in `NEXT-VERTICAL-order-cancellation.md` (commit `0b93bf8`)
**Blocks:** WI-CANCEL-1 dispatch
**Related:** DR-REFUNDS-001 (pattern reuse for asymmetric rate-limit + frozen-reason enum)

> *"The art of being wise is the art of knowing what to overlook." — William James*

---

## Scope

Resolves the 4 open questions filed with `NEXT-VERTICAL-order-cancellation.md` before WI-CANCEL-1 can be dispatched to application-development-squad. Deviations from these resolutions require DR-CANCEL-002.

---

## R1 — Cancellation window: **60 minutes from `order.confirmedAt`**

**Question:** How long is the user-initiated cancel window?

**Decision:** **60 minutes**, anchored to `order.confirmedAt` (same anchor as refunds DR-REFUNDS-001/R1).

**Rejected alternatives:**
- *CS p95 fulfillment lag (~4h):* too generous — invites cancel-after-pick (inventory rework, fulfillment race).
- *15min:* too tight — fails users who confirm on mobile then immediately notice address typo on the confirmation page (the most common cancel reason per support tickets).
- *Until first fulfillment event:* fulfillment events arrive async and unpredictably; window must be deterministic and shown to the user up-front.

**Rationale:** 60min matches the inventory hold TTL × 4 (15min × 4 = soft buffer), is well under the p50 first-pick time (~2h per ops), and gives the confirmation page a meaningful "Cancel order" CTA lifetime. Anchor is `confirmedAt` (not `createdAt` or `paidAt`) for the same reason as refunds: immune to webhook lag, deterministic at write time.

**Eligibility check:** `now - order.confirmedAt < 60min` AND `order.state == Confirmed` AND no `Fulfillment.Picked` event recorded. Failure returns 409 with `reason: window_expired` or `already_fulfilled` per R2.

---

## R2 — Cancel-after-cancel-and-reorder: **default behavior is empty cart, no auto-rebuild**

**Question:** If user cancels then wants to re-order, do we rebuild the cart?

**Decision:** **No auto-rebuild in v1.** Successful cancellation returns the user to the order-history page. Cart remains empty (or whatever state it was in pre-cancel — we do not touch it).

**Rejected alternatives:**
- *Auto-restore cart from canceled order line items:* requires re-pricing (FX, promo expiry, inventory recheck), re-validating saved address, and re-running tax calc. Cross-cuts SPM v1, refunds, and pricing. Out of scope.
- *"Re-order" CTA on canceled-order detail page:* deferred to v2 — requires the work above plus design for "some items unavailable" states. Tracked as out-of-scope item in the vertical spec.

**Rationale:** v1 ships cancel-only. Re-order is a separate vertical with its own pricing/inventory/promo re-validation surface. Forcing the user to start fresh is honest about what we can guarantee.

---

## R3 — Email subscription confirmation: **out of scope for v1**

**Question:** Should cancellation send a confirmation email and/or unsubscribe from order-status notifications?

**Decision:** **No new email work in v1.** Cancellation triggers the existing `OrderStateChanged` event which the (already-shipped) notification service consumes — that service decides email behavior. Cancellation does NOT change subscription preferences.

**Rejected alternatives:**
- *Dedicated cancellation email template:* notification service owns email templates; threading a new template through that service is out of scope for the cancellation vertical and would block on notification-squad capacity.
- *Auto-unsubscribe from order-status emails on cancel:* user may still want shipping-rejection / refund-status updates from the linked refund flow. Aggressive unsubscribe is hostile.

**Rationale:** Cancellation is a state transition. Email is a downstream concern owned by notification infrastructure. Keep the seam clean; do not bundle.

**Follow-up:** If post-launch metrics show user confusion ("did my cancel work?"), file a v1.1 issue against notification-squad for a dedicated template — not against cancellation vertical.

---

## R4 — Provider variance (Stripe vs Adyen): **abstract behind `IPaymentProviderCancelClient`**

**Question:** Stripe uses `payment_intent.cancel` (when uncaptured) or refund (when captured); Adyen uses `/cancels` (uncaptured) or `/refunds` (captured). How do we model this?

**Decision:** Introduce **`IPaymentProviderCancelClient`** with a single `CancelOrRefundAsync(orderId, providerChargeId, ct)` method. Provider implementations (`StripeCancelClient`, `AdyenCancelClient`) internally decide cancel-vs-refund based on charge state queried from the provider. Our state machine only knows `CancelRequested → CancelAccepted → Canceled`; refund-as-implementation is invisible to us.

**Webhook handling:** Both providers fire a single `cancel.accepted` mapped event regardless of underlying mechanism. The webhook adapter (already in place for refunds) normalizes provider events to our internal event vocabulary. Inventory release happens on `cancel.accepted` exactly once.

**Provider charge ID NEVER serialized to clients:** SEC-CANCEL-001 (already in vertical spec) covers this — same pattern as DR-REFUNDS-001/R3.

**Rejected alternatives:**
- *Expose `cancelType: cancel|refund` in our API:* leaks provider implementation, breaks abstraction, and would require frontend branching.
- *Always issue a refund (skip cancel path):* loses the cheaper/faster cancel codepath when charge is uncaptured (Stripe charges no fee on cancel; refunds may incur a fee on some processors). Cost-relevant at scale.

**Rationale:** Provider abstraction is a Tier-1 concern. The state machine, audit log, and client API stay provider-agnostic. Provider-specific branching lives in two small adapter classes with their own test fixtures.

**Test seam:** `FakePaymentProviderCancelClient` (mirrors `FakePaymentProvider` from refunds) — QA can simulate cancel-success, cancel-rejected-charge-captured-must-refund, cancel-rejected-already-settled, and provider-timeout.

---

## Binding effects on WI-CANCEL-1..7

1. **WI-CANCEL-1 (app-dev backend):** Implements R1 (60min window check), R2 (no cart mutation post-cancel), R4 (`IPaymentProviderCancelClient` + `FakePaymentProviderCancelClient` seam). Eligibility 409 enum unchanged from vertical spec: `{not_confirmed | already_canceled | already_refunded | window_expired | already_fulfilled}`.
2. **WI-CANCEL-3 (UX):** Cancel CTA hidden when `now - confirmedAt >= 60min`. Post-cancel destination is order-history (R2). No "re-order" CTA.
3. **WI-CANCEL-5 (security):** R4 adds SEC-CANCEL-001 grep target (provider charge IDs `ch_`, `pi_`, `re_` never in `dist/`).
4. **WI-CANCEL-6 (infra):** No notification-service changes (R3). Existing `OrderStateChanged` event flow reused.
5. **WI-CANCEL-7 (CI):** Add grep gate `GATE-CANCEL-06`: `grep -r 'cancelType' src/` returns 0 matches (R4 — implementation detail must not leak).

---

## Out-of-scope (filed for v2+, not blocking v1)

- Partial cancel / line-item cancel
- CS-initiated cancel
- Returns flow (post-fulfillment)
- Reason capture (free-text or enum dropdown on cancel modal)
- Goodwill credit on cancel
- Subscription cancellation (separate vertical entirely)
- Cancel-during-3DS-challenge
- Auto-rebuild cart on cancel-then-reorder (R2 defers)

---

## Status

**Binding.** WI-CANCEL-1 may be dispatched to application-development-squad once:
1. SPM v1 ships to 100% (per backlog order: refunds → SPM → cancel).
2. Refunds v1 reaches 100% rollout.
3. This DR is committed to `tamir/squad-fixes`.

Deviation from R1–R4 requires DR-CANCEL-002. Reviewer Rejection Protocol applies — original author of any deviating PR is locked out of revision per `squad.agent.md`.

> *"In matters of style, swim with the current; in matters of principle, stand like a rock." — Thomas Jefferson*
