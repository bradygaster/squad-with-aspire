# WI-CANCEL-1 Contracts — DR-CANCEL-002 Amendment

**Status:** Additive amendment to `wi-cancel-1-contracts/` (commit `bcec51f`). Closes 4 QA-flagged spec gaps. Source of truth: `squads/ideation-research-planning/artifacts/post-checkout-backlog/DR-CANCEL-002-qa-gap-resolutions.md` (commit `6f5a1ee`).

**No production wiring yet.** Endpoint/repo/DI still lands in `wi-cancel-1-backend/` after SPM v1 hits 100%.

## What this amendment adds

| File | Change | Reason |
|---|---|---|
| `IPaymentProviderCancelClient.cs` | Added `Rejected` outcome to `ProviderCancelOutcome` enum | DR-CANCEL-002 R4 — `cancel.rejected_by_provider` is a distinct domain event, not folded into `cancel.declined` |
| `CancelErrorEnvelope.cs` | NEW — frozen `Codes` constants + `NotCancellable(reason)` + `RequestInFlight(op)` mappers | DR-CANCEL-002 R1 + R3, and dev seam #6 (app-dev OWNS, QA + review-deployment consume) |

## DR-CANCEL-002 resolutions pinned

### R1 — `ORDER_NOT_CANCELLABLE.reason` frozen 4-value enum

```
already_canceled | already_refunded | window_expired | fulfillment_in_progress
```

- **Precedence (caller MUST evaluate in this order, first match wins):**
  1. `already_canceled` (terminal)
  2. `already_refunded` (terminal — post-settle only; pre-settle is REQUEST_IN_FLIGHT)
  3. `window_expired` (time-based, 60min from `confirmedAt`)
  4. `fulfillment_in_progress` (runtime state)
- **`Pending` orders → 404 `ORDER_NOT_FOUND`** (IDOR-safe), NEVER 409. `not_confirmed` is deliberately NOT a reason value.
- Constants in `CancelErrorEnvelope.Codes.Reason*`. QA tests reference constants, never string literals.

### R2 — Rate caps `50/sub + 250/IP per 24h`

- Both keys (`H(sub:cancel-rate)` AND `H(ip:cancel-rate)`) written and checked independently.
- 429 fires if **either** trips. `Retry-After: 3600`.
- GET status uncapped.
- Asymmetry ladder locked: checkout 1000 / refunds 100 / cancel 50.
- Implementation lands in `wi-cancel-1-backend/`.

### R3 — `409 REQUEST_IN_FLIGHT` for cancel-during-refund-pending

Distinct from `ORDER_NOT_CANCELLABLE{reason=already_refunded}`:

| Refund state | Cancel response |
|---|---|
| `Requested` or `ProviderAccepted` (not yet settled) | **`409 REQUEST_IN_FLIGHT`** + `{ operation: "refund", retryAfterSeconds: 30 }` |
| `Settled` | `409 ORDER_NOT_CANCELLABLE` + `reason: "already_refunded"` |
| `Failed` | `200` (cancel proceeds normally) |

Mapper: `CancelErrorEnvelope.RequestInFlight(operation: "refund", retryAfterSeconds: 30)`.

**The `operation` field is the ONLY place "operation" may appear in any cancel response.** Cancel NEVER leaks `cancelType` (GATE-CANCEL-06).

### R4 — `cancel.rejected_by_provider` distinct webhook event

- `IPaymentProviderCancelClient` adapters normalize provider events to **one of two** domain events: `cancel.accepted` OR `cancel.rejected_by_provider`. Plus the existing `cancel.declined` for allowlisted failure codes.
- Stripe mapping:
  - `payment_intent.canceled` → `cancel.accepted`
  - `payment_intent.cancel` 4xx response OR `charge.dispute.created` mid-cancel → `cancel.rejected_by_provider`
- Adyen mapping:
  - `/cancels` success notification → `cancel.accepted`
  - `/cancels` refused notification → `cancel.rejected_by_provider`
- **State machine:**
  - `CancelRequested → CancelAccepted → Canceled` (terminal happy path), OR
  - `CancelRequested → CancelRejected` (terminal-and-retryable, subject to rate cap + window)
- **Inventory release fires ONLY on `cancel.accepted`.** `cancel.rejected_by_provider` NEVER touches inventory.
- `providerReason` captured to `cancel-audit` container, **NEVER serialized to client** — GATE-CANCEL-07 enforces via grep on `providerReason | cancel_xxx | re_xxx | payment_intent` in `dist/`.

## Dev seams owed by WI-CANCEL-1 (all 6 accepted)

| # | Seam | Status |
|---|---|---|
| 1 | `IPaymentProviderCancelClient` + `FakePaymentProviderCancelClient` | ✅ Shipped in this bundle |
| 2 | `IOrderClock : TimeProvider` | ⏳ Lands in `wi-cancel-1-backend/` |
| 3 | `SeedCancellableOrder(orderId, confirmedAt, status, ownerSub, items)` | ⏳ Lands in `wi-cancel-1-backend/` (WAF partial extension) |
| 4 | `_debug/cancel-count/{orderId}` (gated `ASPNETCORE_ENABLE_TEST_AUTH=1`) | ⏳ Lands in `wi-cancel-1-backend/` |
| 5 | `_debug/webhook-dispatch-count/{orderId}` (gated `ASPNETCORE_ENABLE_TEST_AUTH=1`) | ⏳ Lands in `wi-cancel-1-backend/` |
| 6 | `CancelErrorEnvelope.Codes` + `NotCancellable(reason)` mapper | ✅ Shipped in this bundle |

## Ownership boundary (refunds v1b pattern)

- **app-dev OWNS** `CancelErrorEnvelope.Codes` constants. Single source of truth.
- **QA CONSUMES** the constants directly from this file — no string duplication in test fixtures.
- **review-deployment ASSERTS** on the deployed surface (drift detector). Expects `error.code` to match `Codes.*` values exactly.

If any of the three squads need a new code, route a new DR through planning — do not add string literals locally.

## QA bundle deltas required (from DR-CANCEL-002)

QA's pre-positioned bundle at `squads/quality-testing/.squad/session-state/.../cancel-v1-test-plan/` needs:

1. `CancelReasonEnumTests.cs` → verify spelling matches `Codes.Reason*` constants (no string change, just an `using static` import).
2. `CancelIntegrationTests.cs` → add `REQUEST_IN_FLIGHT` 3-transition test (refund-pending→retry, refund-settled→409 already_refunded, refund-failed→200) + `cancel.rejected_by_provider` webhook path test (verify NO inventory release).
3. `cancel-storm.js` → add IP-cap scenario (250/IP/24h) alongside sub-cap (50/sub/24h).
4. `PreprodGate-CANCEL.cs` → add GATE-CANCEL-07 grep (`providerReason | cancel_xxx | re_xxx | payment_intent` against `dist/`).

GATE numbering reserved: 01–05 (DR-001), **06** cancelType leak (DR-001 R4), **07** provider-id/providerReason leak (DR-002 R4).

## Apply order (unchanged from contracts bundle)

1. `wi-cancel-1-contracts/` (this bundle — pure adds, contracts-only)
2. `wi-cancel-1-backend/` (endpoint, repo, DI, real `StripePaymentProviderCancelClient`, seams #2–#5) — TBD post SPM v1 100%
3. `wi-cancel-1-frontend/` (experience-design owns) — TBD

## Out-of-scope for v1 (unchanged)

Partial cancel, cancel+re-order single txn, customer-facing cancel reason capture (CS-only), goodwill credit, cancellation history page (use order detail).
