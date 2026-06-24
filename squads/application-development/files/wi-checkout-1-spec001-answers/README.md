# WI-CHECKOUT-1 — Answers to CHECKOUT-SPEC-001 (ideation Item 1) + Reconciliation Status (Item 2)

**Author:** application-development-squad
**Base:** `c80c3e4` (wi-checkout-1-contracts, DR-CO-001..006)
**Re:** ideation CHECKOUT-SPEC-001 / commit `afd9319`, Q-CO-3/4/5/6/10 + R1/R2/R3/providerReason reconciliation
**Status:** Frozen. Bundle 4 unblocked. Diffs against ideation's proposals called out explicitly.

---

## Item 1 — 5 backend contract answers

### Q-CO-3 — 3DS challenge flow: **in-browser redirect** (Stripe `next_action.redirect_to_url` shape)

- v1 ships **redirect-out-and-return** only. NOT iframe-embedded native components.
- Wire: `POST /api/checkout/{id}/confirm` → `202 Accepted` + `Location: /api/checkout/{id}/status` (normal polling shape from DR-CO-005). When `PaymentConfirmOutcome.ActionRequired`, `GET /status` returns `{ state: "ActionRequired", actionRequired: { type: "redirect", url: "<provider-hosted URL>", returnUrl: "/checkout/{id}/review?resume=1" } }`.
- `CheckoutState.ActionRequired` (already in shipped enum) is the only state that exposes `actionRequired` payload. Client opens `actionRequired.url` via `window.location.assign` (full nav, not popup, not iframe).
- Provider returns to `returnUrl` with provider-appended query (`?payment_intent=…&payment_intent_client_secret=…` Stripe; `?redirectResult=…` Adyen). Server consumes the query, calls provider confirm-resume API, and the next `GET /status` poll returns terminal state.
- **Out of scope v1:** iframe-embedded Stripe Elements / Adyen Components, native Apple/Google Pay sheets. Captured as v2 punt in DR-CO-007.
- **Rationale:** single E2E shape for QA (navigation-out-and-return), works for both providers, no SDK in our bundle, and the resume path is symmetric for `GET /status` polling — `usePollingResource` 5s/60s/12-cap reuse from cancel v1 unchanged.

### Q-CO-4 — Idempotency-Key: **align to refunds shape (per-cart + JCS body hash, 15min TTL)**

**Amending DR-CO-005 (24h, per-session) → per-cart + JCS body hash, 15min TTL.** Ideation's proposal wins; refund consistency beats novelty.

- Header: `Idempotency-Key` required on `POST /api/checkout/{id}/confirm`. Missing → `400 IDEMPOTENCY_KEY_REQUIRED`.
- Scope: **per-cart** (`cartId` = checkout session id, stable across resume/refresh within session lifetime; rotates only on new cart creation, not on review-back-payment-back navigation).
- Cache key: `H(sub:checkout:{cartId}:{clientGeneratedKey})` + JCS-canonicalized body hash. **Same shape as refunds.**
- TTL: **15 minutes** (was 24h in DR-CO-005). Aligns with refunds; sufficient for retry-after-3DS-redirect path (provider redirects rarely exceed 5min).
- Same-key + different-body → `409 IDEMPOTENCY_KEY_CONFLICT` (existing CheckoutErrorEnvelope code, unchanged).
- In-flight (same key + same body, prior request still pending) → `409 REQUEST_IN_FLIGHT` with `retryAfterSeconds` (reuses CancelErrorEnvelope.RequestInFlight shape; already shipped).
- **cartId stability:** stable across resume/refresh/back-button within the session. Rotates ONLY on explicit cart re-creation (user empties cart and starts over, or 30-min session TTL elapses → DR-CO-008 punt). Retry semantics: same cartId + same Idempotency-Key + same body within 15min = idempotent replay returns cached result.

**Action:** Will ship `wi-checkout-1-contracts-v2/` amendment patching DR-CO-005 README + IdempotencyContract.cs constants (TTL_MINUTES=15, scope="per-cart") before checkout backend bundle. Non-blocking for QA bundle 1/4 (no test logic depends on TTL value — only on the 409 error envelope shape, which is unchanged).

### Q-CO-5 — Price recompute on `payment → review`: **server recompute on transition, cached 60 seconds, displayed price = cached price**

- On `payment → review` transition (`POST /api/checkout/{id}/transition`), server recomputes line totals + shipping + tax server-side, persists `reviewSnapshot{ total, taxTotal, shippingTotal, computedAt, ttlSeconds: 60 }` to checkout session state.
- Client displays `reviewSnapshot.total`. NOT recomputed on keystroke (there are no keystrokes on /review — review is a read-only confirm screen per spec §UX).
- On `review → confirming` (`POST /confirm`), the `ExpectedTotalMinorUnits` echo header (DR-CO-006) MUST equal `reviewSnapshot.total`. If `now - computedAt > ttlSeconds` (60s) OR upstream price changed (inventory/promotion/tax-jurisdiction shift), server forces re-review: `409 TAX_RECALCULATED` with `{ previousTotal: <reviewSnapshot>, newTotal: <fresh recompute>, changedFields: ["tax"|"shipping"|"line"|"promotion"] }`. Client re-renders /review with explicit re-acknowledge checkbox (UX spec §10).
- **60s is the divergence bound.** Honest trade-off between provider-side price drift and user-confusion-from-forced-re-review. Tuneable; QA's tax-recalc test bundle (Q-CO-10) can pin `ttlSeconds` via test-only header.

### Q-CO-6 — Shipping rate provider call: **at `shipping → payment` transition; rates held 10 minutes**

- Server calls carrier rate API on `POST /api/checkout/{id}/transition` from `shipping → payment` (address now committed).
- Response persists `shippingRates[]` + per-rate `quotedAt` + global `quotesValidUntil = quotedAt + 10min` to checkout session state.
- `/payment` screen displays the quoted rates from session state (no fresh carrier call on /payment render).
- On `payment → review` transition, server re-validates `now < quotesValidUntil`. If expired, recompute (carrier call), update session, return updated rates in the transition response → client re-renders /payment forcing user to re-confirm shipping option. If unchanged within 10min, no recompute.
- On `review → confirming`, the `reviewSnapshot` already locked the chosen shipping rate; no recompute needed unless `reviewSnapshot` itself expired (Q-CO-5 path).
- **10min** is the carrier-rate honor window (UPS/USPS typical SLA). Exceeds Q-CO-5 60s deliberately — shipping changes less often than tax/inventory.

### Q-CO-10 — Tax recompute: **on every transition INTO `review`, AND on item/qty change in cart**

- Triggers:
  1. `payment → review` transition (Q-CO-5 path): tax recomputed as part of `reviewSnapshot`.
  2. Cart line mutation (`POST /api/cart/{id}/items` add/remove/qty change) when state ∈ {Cart, Shipping}: tax recomputed in cart response. **No tax shown in Cart state — but stored for fast `shipping → payment` transition.**
  3. Address change (`POST /api/checkout/{id}/address`) on `Shipping` state: tax recomputed (tax jurisdiction may have changed).
- **NOT triggered on `/review` render or `/review` refresh** — that's the cached `reviewSnapshot` window (60s). Refresh within 60s = same `reviewSnapshot`, no recompute, no provider call.
- TTL: tax inherits Q-CO-5 60s `reviewSnapshot` lifetime; no separate tax TTL.
- **Same answer as DR-CO-006 (already shipped).** Confirming with explicit trigger list now.

---

## Item 2 — Reconciliation Status (R1/R2/R3/providerReason)

**TL;DR — there is nothing to reconcile.** Checkout backend implementation has not shipped. Only contracts (c80c3e4) + mapper interfaces have shipped. The checkout backend bundle (`wi-checkout-1-backend/`) is queued behind refunds v1 → SPM v1 → cancel v1 in our dispatch order. There is no running checkout code in `main` or any deployed environment to diverge from CHECKOUT-SPEC-001.

Verified by inspection:

| Item | Status |
|---|---|
| R1 — 7-state enum + 4+4 failure reasons wire strings | **Contracts MATCH SPEC-001.** `CheckoutState.cs` ships 9 states (SPEC-001's 7 + `ActionRequired` for 3DS/SCA per DR-CO-002 + `Confirming` polling state). All wire strings PascalCase per project convention; `Reasons.ForEnum` projection generates snake_case for envelope `.reason` field. No production code exists yet to diverge. |
| R2 — `/api/checkout/**` unauth = 401 not 403 | **No checkout endpoints implemented yet.** Will be wired correctly in `wi-checkout-1-backend/` bundle. SPEC-001 R2 captured as backend-bundle acceptance criterion. |
| R3 — Inventory reservation at `review → confirming` (NOT at add-to-cart) | **No reservation code exists.** SPEC-001 R3 captured as backend-bundle acceptance criterion. Will ship `_debug/inventory-reservation/{sku}` test seam (test-only, `CHECKOUT_DEBUG=1`-gated) per ideation's note, same pattern as cancel `_debug/cancel-count/{orderId}`. |
| `providerReason` redaction (GATE-CO-01 `dist/` grep) | **No backend serialization code exists.** Will ship with backend bundle; mirrors GATE-CANCEL-07 (cancel) — `providerReason` captured to `checkout-audit` container only, never serialized to client envelope. CheckoutErrorEnvelope (shipped) has no `providerReason` field — discipline enforced at contract layer. |

**Action when checkout backend bundle ships:** All 4 reconciliation items become bundle acceptance gates, not retrospective patches. QA's bundle 1 enumeration guard + GATE-CO-01..03 grep gates run as part of backend bundle CI, not as separate patches.

---

## Dispatch order unchanged

refunds v1 → SPM v1 → cancel v1 (`wi-cancel-1-backend/`) → checkout v1 (`wi-checkout-1-backend/`).

Contract surface for WI-CHECKOUT-1 frozen across DR-CO-001..006 + this answers bundle. Next app-dev artifact will be the per-cart/15min idempotency amendment (DR-CO-005 v2) — non-blocking for QA bundle 1/4.

App-dev checkout queue: empty.
