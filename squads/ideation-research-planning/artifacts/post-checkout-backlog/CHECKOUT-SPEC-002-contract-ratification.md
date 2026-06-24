# CHECKOUT-SPEC-002 — Contract Ratification (Q-CO-3/4/5/6/10)

**Status:** Binding under reviewer-rejection-protocol.
**Supersedes:** DR-CO-005 (idempotency TTL/scope only). All other DR-CO-001..006 unchanged.
**Filed:** 2026-06-24
**Trigger:** app-dev shipped `wi-checkout-1-spec001-answers/` (commit `bbc2faa`) answering CHECKOUT-SPEC-001 items 1 & 2.

---

## Resolutions (frozen)

### R1 — Q-CO-3 (3DS challenge flow): Ratified as **in-browser redirect**

- `CheckoutState.ActionRequired` payload: `actionRequired: { type: "redirect", url, returnUrl: "/checkout/{id}/review?resume=1" }`.
- Client performs full navigation via `window.location.assign(url)` — NO iframe, NO native sheets in v1.
- Provider redirects back to `returnUrl` with provider-appended query string; server consumes, calls provider resume API, next `GET /status` poll returns terminal state.
- **v1 single 3DS shape.** Adyen Components, Stripe Elements iframe, Apple/Google Pay native sheets all deferred to future DR-CHECKOUT-3DS-002.
- QA bundle 4 `3DS-resume-happy-path` authors against this single contract.

### R2 — Q-CO-4 (idempotency): **AMENDS DR-CO-005**

DR-CO-005 v1 (now superseded): per-session, 24h TTL.
**DR-CO-005 v2 (binding):** **per-cart scope, JCS body hash, 15min TTL.** Mirrors refunds + cancel idempotency exactly.

- Header: `Idempotency-Key` (required on `POST /confirm`, optional elsewhere).
- Key derivation: `H(sub:checkout:cartId:clientGeneratedKey)` + JCS canonicalization of body.
- `cartId` stability: stable across resume/refresh/back-button within session; rotates only on cart re-creation (new cart = new key space).
- TTL: 15 minutes.
- Error envelope codes unchanged: `IDEMPOTENCY_KEY_REQUIRED` (400), `IDEMPOTENCY_KEY_CONFLICT` (409 — same key, different body), `REQUEST_IN_FLIGHT` (409 — same key in flight, `retryAfterSeconds` per cancel R3 pattern).
- **Rationale:** Consistency with refunds (DR-REFUNDS-001) and cancel (DR-CANCEL-001) beats novelty. Single idempotency grammar across all 3 post-cart verticals.
- `IdempotencyContract.cs` shipped at `bbc2faa` with `TtlMinutes = 15`, `Scope = "per-cart"` constants. QA bundle 1 unaffected (envelope codes stable).

### R3 — Q-CO-5 (price recompute on review): Ratified as **server-recompute on payment→review, 60s snapshot TTL**

- Server recomputes total/tax/shipping on `payment→review` transition.
- Persists `reviewSnapshot { total, taxTotal, shippingTotal, computedAt, ttlSeconds: 60 }`.
- /review displays cached snapshot (read-only screen).
- `POST /confirm` requires `ExpectedTotalMinorUnits` echo == `reviewSnapshot.total`.
- Drift OR `now - computedAt > 60s` → **409 `TAX_RECALCULATED` { previousTotal, newTotal, changedFields: ["tax"|"shipping"|"line"|"promotion"] }** → forced re-review with explicit re-acknowledge (UX spec §10).
- **60s is the divergence bound.** Tuneable via test-only header for QA tax-recalc bundle.
- Frozen 409 reason `TAX_RECALCULATED` joins the `CheckoutErrorEnvelope.Codes.*` enum and is subject to GATE-CO-05 (enumeration closure).

### R4 — Q-CO-6 (shipping rate timing): Ratified as **carrier API on shipping→payment, 10min quote TTL**

- Carrier API called at `shipping→payment` transition.
- Quotes held 10 minutes: `quotesValidUntil = quotedAt + 10min`.
- Re-validated on `payment→review`; if expired, recompute + force user back to /payment to re-confirm shipping option.
- `review→confirming` reuses locked-in `reviewSnapshot` shipping rate (R3 snapshot owns the commit).
- **10min** = UPS/USPS carrier honor window SLA.

### R5 — Q-CO-10 (tax recompute triggers): Ratified — explicit trigger list

Tax recompute triggers (exhaustive):
1. Every `payment→review` transition.
2. Cart line mutation when `state ∈ {Cart, Shipping}`.
3. Address change while in `Shipping` state.

**Excluded** (no recompute):
- /review page render or refresh — cached `reviewSnapshot` window owns (R3).
- `confirming` state — irrevocable per DR-CO-003.
- Any state after `confirmed`.

No silent recalc on confirm. Drift detected at confirm → 409 `TAX_RECALCULATED` (R3) → forced re-review.

---

## Reconciliation status — confirmed: nothing owed

App-dev confirmed in `bbc2faa` README: no checkout backend code in production yet. Only contracts (`c80c3e4`) + mapper interfaces have shipped. `wi-checkout-1-backend/` queued behind refunds v1 → SPM v1 → cancel v1.

| SPEC-001 R-item | Status |
|---|---|
| R1 7-state enum + wire strings | Contracts MATCH spec (9-state superset adds `ActionRequired` for 3DS + `Confirming` polling — both additive, no spec violation). No prod code to diverge. |
| R2 401-not-403 unauth | Captured as backend-bundle acceptance criterion. |
| R3 reserve-at-confirm | Captured as backend-bundle acceptance criterion. `_debug/inventory-reservation/{sku}` seam owed (CHECKOUT_DEBUG=1-gated, mirrors `_debug/cancel-count/{orderId}`). |
| `providerReason` redaction | `CheckoutErrorEnvelope` (shipped) has NO `providerReason` field. Discipline enforced at contract layer. GATE-CO-01 `dist/` grep ships with backend bundle (mirrors GATE-CANCEL-07). |

All 4 R-items become **backend-bundle CI gates**, not retrospective patches. CHECKOUT-SPEC-001 R1/R2/R3 + this amendment frozen across DR-CO-001..006 (v2 on 005).

---

## Still open — DR-CO-007..009 (flagged by app-dev, non-blocking for current bundle stack)

| ID | Question | Owner | Blocks |
|---|---|---|---|
| DR-CO-007 | 3DS scope: EU/UK only vs all card BINs | ideation (this squad) | Backend bundle routing logic |
| DR-CO-008 | `checkoutSessionId` TTL (UX assumed ≥30 min; confirm or amend) | ideation + exp-design | Session-expiry test bundle (QA bundle 8) |
| DR-CO-009 | `CART_CHANGED` response shape: return current cart payload vs force-redirect | ideation + exp-design | Cart-changed-during-checkout edge case tests |

**Proposed direction (for next turn / next session):**
- DR-CO-007: scope to **all card BINs** v1. Per-BIN routing adds combinatorial test surface and provider-specific 3DS variance with no clear v1 win. Defer per-BIN to DR-CHECKOUT-3DS-002 if observed false-positive rate hurts conversion.
- DR-CO-008: **30min TTL** ratified (UX assumption). Refresh on activity in {Cart, Shipping, Payment}; **no refresh** in {Review, Confirming, ActionRequired} (irrevocable / polling states should not extend session via background activity).
- DR-CO-009: **Return current cart payload + 409 `CART_CHANGED { currentCart, changedFields }`.** Client owns the redirect-vs-inline-reconcile UX choice (route to exp-design). Same shape pattern as `TAX_RECALCULATED` (R3) and `CONFIRM_REJECTED` (SPEC-001 R3) — return new state, let client render the surface transition. NO server-side forced redirect.

Will file as DR-CO-007..009 next turn or when triggered by downstream blocker. Not blocking any bundle currently in flight.

---

## QA unblocked surface (post-ratification)

- **Bundle 1 backend (enumeration guard)** — unblocked at SPEC-001 (R1 enum frozen); contract surface stable. Authoring against `CheckoutState` (9 values incl. ActionRequired + Confirming) + `CheckoutErrorEnvelope.Reasons.All` + `CheckoutWebhookEnvelope.Events.All` (all in `c80c3e4`).
- **Bundle 2 (idempotency duplicate-submit)** — unblocked. Authors against `IdempotencyContract` constants (per-cart + 15min) at `bbc2faa` and `CheckoutErrorEnvelope.Codes.*RequestInFlight|IdempotencyKeyConflict|IdempotencyKeyRequired*`.
- **Bundle 4 (race / retry / 3DS resume)** — unblocked end-to-end:
  - `ConfirmTwoUsersLastItemTest` — against R3 (reserve-at-confirm).
  - `ReservationTtlExpiryTest` — against R3 (90s TTL).
  - `OneRetryNoReReserveTest` — against R3 (hold-through-retry).
  - `RetryWithoutDoubleChargeTest` — against R2 (per-cart idempotency).
  - `3DSResumeHappyPathTest` — against R1 (redirect contract + `returnUrl=/checkout/{id}/review?resume=1`).
- **Bundle 7 (tax recalc)** — unblocked. `TaxRecalcOnConfirmReturns409Test`, `ReviewSnapshotTtlExpiryTest` against R3/R5.

---

## Apply order — unchanged

refunds v1 100% → SPM v1 100% → cancel v1 100% → checkout v1 (`wi-checkout-1-mappers/` → `wi-checkout-1-backend/`).

QA checkout test plan stack ships in parallel with that pipeline.

---

## Discipline

Binding under reviewer-rejection-protocol. Deviation from R1–R5 requires CHECKOUT-SPEC-003. DR-CO-005 v1 is dead — anyone implementing 24h/per-session idempotency must amend or be rejected.
