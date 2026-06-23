# WI-CANCEL-1 Contracts (Pre-Implementation Freeze)

**Status:** Contract surface frozen. Implementation gated on SPM v1 100% per dispatch order (refunds v1 → 100% → SPM v1 → 100% → cancel v1).

**Source of truth:** `squads/ideation-research-planning/artifacts/post-checkout-backlog/DR-CANCEL-001-spec-resolutions.md` (commit `fa97ade`).

## What ships in this bundle

| File | Purpose |
|---|---|
| `IPaymentProviderCancelClient.cs` | Production interface — hides Stripe-vs-Adyen variance per R4 |
| `FakePaymentProviderCancelClient.cs` | Test seam (required by QA per DR-CANCEL-001) |

**No endpoints, no repository, no DI wiring yet.** Those land in `wi-cancel-1-backend/` once SPM v1 reaches 100%.

## DR-CANCEL-001 resolutions pinned into contracts

- **R1 — Window:** 60 min from `order.confirmedAt`. Enforced in domain layer (not adapter).
- **R2 — Empty cart post-cancel:** No adapter concern; handled in endpoint orchestration.
- **R3 — No new email work:** `OrderStateChanged` reused; cancel adapter does not emit notifications.
- **R4 — Provider variance hidden:** `IPaymentProviderCancelClient` is the only seam; webhook normalizes to single `cancel.accepted` domain event.

## SEC-CANCEL-001 — `CancelType` non-leak (GATE-CANCEL-06)

`CancelType { Void, Refund }` is INTERNAL telemetry/audit only.

- ❌ MUST NOT appear in any HTTP response body.
- ❌ MUST NOT appear in client-bound webhook normalization.
- ✅ MAY appear in internal logs, audit trail, metrics labels.

QA's GATE-CANCEL-06 grep MUST assert:
```
grep -ri '"cancelType"\|"cancel_type"' deployed-response-fixtures/  →  zero matches
```

When app-dev implements `wi-cancel-1-backend/`, the endpoint MUST map `ProviderCancelResult` to the public domain shape WITHOUT serializing `CancelType` or `ProviderRefundId` (mirrors SEC-RFD-001 `re_xxx` guard in refunds v1b `RefundErrorEnvelope`).

## Webhook normalization contract (for QA fake-provider tests)

Provider events → single normalized domain event:

| Provider event (Stripe) | Provider event (Adyen) | Normalized domain event |
|---|---|---|
| `payment_intent.canceled` | `cancellation` notification | `cancel.accepted` |
| `charge.refunded` (full, from cancel flow) | `refund` notification (full, from cancel flow) | `cancel.accepted` |
| `payment_intent.cancellation_failed` | `cancellation_failed` | `cancel.declined` + `failureCode` (allowlist) |

Failure-code allowlist (same shape as refunds v1 `RefundWebhookHandler`):
`PROVIDER_DECLINED | PROVIDER_TIMEOUT | PROVIDER_UNAVAILABLE | ALREADY_CAPTURED_AND_REFUNDED`
Unmapped → `DECLINED` + `cancel.failure_reason_unmapped` telemetry counter.

## Why ship contracts before implementation?

1. **QA un-blocked early.** `FakePaymentProviderCancelClient` seam lets QA author the WI-CANCEL-1 integration suite during the SPM v1 → 100% gap. No idle time.
2. **Adapter surface is the highest-churn risk.** Freezing it pre-impl prevents the refunds-v1→v1b error-envelope re-work pattern.
3. **GATE-CANCEL-06 spec'd before any code can leak `cancelType`.** Cheaper than a post-hoc fix.

## Apply order (when SPM v1 hits 100%)

1. This bundle (contracts only — pure adds, no behavior).
2. `wi-cancel-1-backend/` (endpoint, repo, DI wiring, real `StripePaymentProviderCancelClient`) — TBD.
3. `wi-cancel-1-frontend/` (experience-design owns) — TBD.

## Out-of-scope for v1 (per planning §9 pattern, route v2 asks back to planning)

- Partial cancellation (per-line-item)
- Cancel + re-order in one transaction (R2: post-cancel = empty cart)
- Customer-facing cancel reason capture (CS-only field for v1)
- Goodwill credit on cancel
- Cancellation history page (use order detail page)
