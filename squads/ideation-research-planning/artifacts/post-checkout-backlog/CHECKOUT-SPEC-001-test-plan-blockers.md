# CHECKOUT-SPEC-001 — Resolutions for QA Checkout Flow Test Plan Blockers

**Status:** Binding under reviewer-rejection-protocol. Deviation requires CHECKOUT-SPEC-002.
**Date:** 2026-06-24
**Author:** ideation-research-planning-squad
**Consumes:** quality-testing-squad `files/checkout-flow-test-plan/README.md` (session 3ab6ab79), Q-CO-1, Q-CO-7, Q-CO-8
**Routes:** Q-CO-2/9 → experience-design-squad; Q-CO-3/4/5/6/10 → application-development-squad

---

## Context

Checkout is the **root vertical** all post-checkout work (confirmation page, order history, refunds, SPM, cancellation) presupposes. Refunds DR-REFUNDS-001 already anchors the 24h refund window on `order.confirmedAt` — that field must exist and be authoritative. Cancellation DR-CANCEL-001 anchors its 60min window on the same field. The cancel-v1 inventory release contract (`cancel.accepted` releases exactly once) requires that checkout has a defined **reservation** moment to release from.

This DR locks the checkout contract that those downstream verticals already depend on. It is **descriptive** for the parts those DRs implied and **prescriptive** for the parts QA is now formalizing.

---

## R1 — Canonical Checkout States (resolves Q-CO-1)

**Frozen state machine — 7 states, additive only:**

```
cart → shipping → payment → review → confirming → confirmed
                                                ↘ failed_retryable → review (one-shot back-edge)
                                                ↘ failed_terminal (terminal)
```

**State definitions:**

| State | Meaning | Owner of transition |
|---|---|---|
| `cart` | Items added, no checkout intent expressed | client (add/remove/qty) |
| `shipping` | Address captured + validated | client → POST `/api/checkout/shipping` |
| `payment` | Payment method selected (`paymentMethodId` or `providerToken`) | client → POST `/api/checkout/payment` |
| `review` | All inputs frozen, totals computed, ready for confirm | server (computed on entry) |
| `confirming` | Confirm POSTed, provider call in flight | server (irrevocable from client side) |
| `confirmed` | Provider accepted + order persisted + `order.confirmedAt` set | server (webhook OR sync response) |
| `failed_retryable` | Provider declined recoverable (3DS challenge, network timeout, soft-decline) | server, client may retry from `review` |
| `failed_terminal` | Provider declined unrecoverable (fraud, hard-decline, insufficient funds with no recourse) | server, terminal — new cart required |

**Acceptance:**

- QA's 7-state proposal is accepted **verbatim** (no rename, no split).
- `failed_retryable → review` is the **only** back-edge; `confirming` cannot revert (idempotency + provider state-of-record).
- `confirmed` is terminal at the checkout-vertical layer. Post-confirmation lifecycle (cancel, refund) is owned by their respective verticals and operates on the resulting `order`, not on the checkout state machine.
- `order.confirmedAt` is set **exactly when** the transition to `confirmed` commits. It is the same field DR-REFUNDS-001 R1 and DR-CANCEL-001 R1 anchor on.

**Frozen failure code enum on `failed_retryable`/`failed_terminal`:**

`failed_retryable.reason ∈ {three_ds_required | gateway_timeout | provider_unavailable | soft_decline}`
`failed_terminal.reason ∈ {hard_decline | fraud_block | insufficient_funds_terminal | provider_rejected_permanent}`

Mirrors cancel DR-CANCEL-004 pattern: any unmapped provider reason routes to `provider_unavailable` (retryable), NOT to a terminal state — fail-open to retry to avoid bricking on adapter gaps. Log `providerReason` to `checkout-audit` container (immutable, 7yr) but **never serialize to client**. GATE-CO-01 grep enforces.

---

## R2 — Authentication Requirement (resolves Q-CO-7)

**v1 = authenticated checkout only. Guest checkout is OUT OF SCOPE.**

**Rationale:**

1. **Architectural consistency.** Every post-checkout vertical (history, refunds, cancel, SPM) is `sub`-keyed. Refunds rate limit is `100/sub/24h`, cancel is `50/sub/24h`, history IDOR is `sub == order.userId`. A guest checkout produces an order with no `sub`, which means every post-checkout vertical needs a separate codepath (claim-by-email, magic-link, etc.) that we have not specified.
2. **IDOR posture.** Guest checkouts traditionally use long opaque order-claim tokens. That is a separate threat model (token-replay, email-enumeration via guest-order lookup, etc.) that security-hardening-squad has not modelled.
3. **Refund/cancel actor identity.** DR-CANCEL-002 R2 rate-limits `50/sub + 250/IP per 24h`. Guest checkout breaks the `sub` axis. Adding an IP-only ladder for guests inverts the asymmetry intentionally chosen in cancel v1.
4. **No revenue blocker.** The user-base is authenticated by the time they reach the cart (login required at app entry). Guest checkout would require a new auth-bypass landing flow that does not exist.

**v2 guest-checkout decision deferred to a future DR-CHECKOUT-GUEST-001.** Must include: claim-token threat model, rate-limit axis (IP cap + per-token cap), post-checkout vertical compatibility matrix (does guest get order history? refunds? cancel?), email-as-identity claims for refund actor verification.

**v1 contract:** unauthenticated request to any `/api/checkout/**` returns **401**, never 403 (consistent with IDOR posture: do not leak whether the resource exists pre-auth).

---

## R3 — Inventory Reservation Semantics (resolves Q-CO-8)

**v1 = reserve at `confirming` transition, release on terminal failure or cancel.accepted webhook.**

**Specifically:**

| Event | Inventory effect |
|---|---|
| Add to cart | **No reservation.** Cart is purely client-side intent until checkout begins. |
| `shipping` → `payment` → `review` transitions | **No reservation.** Stock displayed is best-effort, may show "X left" hint but never holds. |
| `review → confirming` transition | **Reserve.** Atomic decrement of `availableStock` on `inventory` container, partition `/sku`. Reservation is short-lived: 90s soft TTL. |
| `confirming → confirmed` | Reservation **converts to sale** (no further inventory mutation; reservation row stays for audit). |
| `confirming → failed_retryable` | Reservation **held** through retry window (max 90s from initial reserve). Client may retry once within window without re-reserving. Second retry expires the hold. |
| `confirming → failed_terminal` | Reservation **released immediately** (atomic increment). |
| `confirming` timeout (>90s no provider answer) | Reservation **released** by janitor. Order moves to `failed_retryable.reason=gateway_timeout`. Client retry re-reserves (may now fail with `out_of_stock`). |
| Post-confirm cancel via cancel.accepted webhook | Inventory release owned by **cancellation vertical** (DR-CANCEL-004), not checkout. Checkout has no obligation. |

**Rationale for reserve-at-confirm over reserve-at-add:**

1. **Cart abandonment is the dominant pattern.** Reserve-at-add holds inventory for every browse-and-leave session. Industry data puts cart abandonment at 60-80%. Reserve-at-add forces a TTL janitor handling 5-10x as many holds as actual sales.
2. **Race window minimization.** Reserve-at-add window = time from add to either confirm or TTL expiry (minutes to hours). Reserve-at-confirm window = time from confirm to provider response (~90s P99). Smaller window = smaller out-of-stock-for-other-shoppers blast radius.
3. **Simpler state.** Reserve-at-add requires reservation state on every cart-line. Reserve-at-confirm requires reservation state on the order. Order-level is one row per checkout attempt; cart-line-level is one row per item per browsing session.
4. **Aligns with cancel v1.** Cancel releases inventory on `cancel.accepted`. That contract makes sense only if the inventory was reserved as part of confirm. Reserve-at-add would mean cancel-after-confirm releases inventory that was reserved hours/days before confirm — confusing audit trail.

**Frozen 409 on confirm if reservation fails:** `CONFIRM_REJECTED` with `reason ∈ {out_of_stock | price_changed | shipping_unavailable}`. Same single-409-with-frozen-reason-enum pattern as refunds DR-REFUNDS-001 R2 and cancel DR-CANCEL-002 R1. Frontend renders per-reason copy; never string-matches.

**Reservation observability:**
- `_debug/inventory-reservation/{sku}` returns `{available, reserved, sold_last_hour}` (test-only, gated on `CHECKOUT_DEBUG=1`).
- `inventory-audit` container, immutable, 7yr retention, partition `/sku`, records every reserve/release/convert with `orderId`, `quantity`, `timestamp`, `reason`.

---

## Test Plan Implications for QA

QA's bundle 1 (enumeration guard + pages-under-test config) can author **today** against:

**For Q-CO-1 unblocking bundle 1 backend (enumeration guard):**
- `CheckoutState` enum = 7 values (cart, shipping, payment, review, confirming, confirmed, failed_retryable, failed_terminal)
- `CheckoutFailureReason.Retryable.All` = 4 strings (`three_ds_required | gateway_timeout | provider_unavailable | soft_decline`)
- `CheckoutFailureReason.Terminal.All` = 4 strings (`hard_decline | fraud_block | insufficient_funds_terminal | provider_rejected_permanent`)
- `CheckoutErrorEnvelope.Codes` includes `ReasonOutOfStock`, `ReasonPriceChanged`, `ReasonShippingUnavailable` for confirm-time 409
- Apply same 8-gate reflection pattern from cancel v1 DR-005 helper bundle: `Reasons.All` cardinality, `Reasons.ForEnum` totality sweep, const-parity reflection, ordinal-comparator drift gate.

**For Q-CO-7 unblocking bundle 2 (auth boundary):**
- Every `/api/checkout/**` endpoint: unauthenticated → 401.
- No guest-checkout codepath. Tests asserting absence of guest endpoints are valid.

**For Q-CO-8 unblocking bundle 4 (reservation race):**
- Single race test set: **two users → review → confirm last item simultaneously**. First commit wins, second gets 409 `CONFIRM_REJECTED{reason:out_of_stock}`. No add-to-cart race test.
- 90s reservation TTL test: confirm, kill client, wait 91s, assert inventory restored, assert order state = `failed_retryable.reason=gateway_timeout`.
- One-retry-without-re-reserve test: confirm → soft_decline → retry within 30s → no inventory state change observed.

**5 P0 gates owed (GATE-CO-01..05):**

1. **GATE-CO-01** — providerReason never serialized: `grep -rE 'providerReason|provider_reason' dist/` returns empty
2. **GATE-CO-02** — IDOR 401-not-403: unauth `/api/checkout/{orderId}/state` returns 401 with no body discriminator
3. **GATE-CO-03** — reserve-only-on-confirming: `_debug/inventory-reservation/{sku}` shows zero `reserved` count for orders in `cart|shipping|payment|review` states
4. **GATE-CO-04** — reservation TTL: orphaned `confirming` orders >90s release inventory (janitor proven)
5. **GATE-CO-05** — failure-reason enum closure: every wire failure code is in `Retryable.All ∪ Terminal.All` (8-gate reflection sweep)

---

## What's Routed Elsewhere (not resolved here)

**Q-CO-2 (UX state-transition copy) and Q-CO-9 (analytics events):** experience-design-squad owns. Without a UX spec, bundle 1 frontend (pages-under-test config for CheckoutPage) cannot author selectors. Expect a future EXP-CHECKOUT-001 spec mirroring confirmation-page + cancel-modal-unmount-lifecycle pattern.

**Q-CO-3 (3DS challenge flow), Q-CO-4 (idempotency key shape), Q-CO-5 (price recompute on review entry), Q-CO-6 (shipping rate provider call timing), Q-CO-10 (tax recompute trigger):** application-development-squad owns. These are backend contract questions that drive bundle 4 backend race tests. Without these, QA bundle 4 partially blocked (race tests can author today; 3DS happy-path cannot).

---

## Apply Order Impact

Checkout v1 is **already in production** (refunds, cancel, confirmation page all depend on it). This DR is **retrospectively binding** — the contract it describes is what the system already does. Where the running system diverges from this DR, application-development-squad owns reconciliation patches before QA bundle 1 gates can pass.

The cancel/refunds/SPM dispatch order is unchanged: refunds v1 100% → SPM v1 100% → cancel v1. Checkout test plan bundles ship in parallel with that pipeline, gated only on the answers above (Q-CO-1/7/8 unblocked **now**; Q-CO-2/3/4/5/6/9/10 unblock as exp-design and app-dev respond).

**Critical path:**
1. CHECKOUT-SPEC-001 (this DR — done)
2. App-dev reconciliation patches (any drift from R1/R2/R3 in current code)
3. QA bundle 1 backend (enumeration guard) — unblocked by R1
4. Exp-design EXP-CHECKOUT-001 (UX spec) — unblocks QA bundle 1 frontend
5. App-dev Q-CO-3/4/5/6/10 answers — unblocks QA bundle 4 race + happy-path

---

## Out of Scope (v1)

Explicitly v2+:
- Guest checkout (see R2)
- Reserve-at-add inventory model (see R3)
- Multi-currency / FX (single-currency v1)
- Subscription / recurring billing (one-shot only)
- Split payment (single payment method per order)
- Promo codes / discount stacking (no promo engine in v1)
- BNPL / Klarna / Affirm (Stripe + Adyen card only)
- Tax exemption certificates (auto-tax via provider only)
- Gift orders (shipping address = billing address tenant, no separate gift recipient)
- Save-for-later (cart is ephemeral, no cart persistence beyond session)

Each represents a future DR-CHECKOUT-* with its own threat model, state-machine impact, and rate-limit ladder.

---

## Binding Statement

Resolutions R1, R2, R3 are **binding** under reviewer-rejection-protocol. Deviation requires CHECKOUT-SPEC-002 amendment with explicit citation of which resolution is being changed and why. Frontend, backend, and QA must converge on the contract described here.

QA may begin authoring bundle 1 backend (enumeration guard) and bundle 4 race tests immediately. Bundle 1 frontend awaits EXP-CHECKOUT-001 from experience-design-squad.
