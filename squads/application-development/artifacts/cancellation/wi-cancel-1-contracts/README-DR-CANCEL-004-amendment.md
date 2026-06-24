# DR-CANCEL-004 amendment — `cancel.declined` event + provider reason mapping

**Status:** Binding. Layered additively on `4551bf5` (DR-CANCEL-003 reconciliation).
**Files added:** `CancelWebhookEnvelope.cs`, `IProviderReasonMapper.cs`.
**Files unchanged:** `IPaymentProviderCancelClient.cs`, `CancelErrorEnvelope.cs`,
`FakePaymentProviderCancelClient.cs`, `README.md`, all three prior READMEs.

## What DR-CANCEL-004 ratified

The literal DR-CANCEL-003 reading (fold `Declined` into `cancel.rejected_by_provider`)
was rejected. The split shipped at `4551bf5` is now binding because:

1. Folding `Declined` would violate DR-CANCEL-002 R1 enum precedence
   (`Declined` = `ALREADY_CAPTURED_AND_REFUNDED` must surface as
   `409 ORDER_NOT_CANCELLABLE{already_refunded}`).
2. Granting refund eligibility on `Declined` would create phantom refund
   eligibility on already-refunded orders.
3. Conflating the paths would lose terminal-vs-retryable semantics.

## Binding mapping table (DR-CANCEL-004 R1)

| `ProviderCancelOutcome` | Webhook event                  | State transition           | Refund eligibility | Rate-cap   |
| ----------------------- | ------------------------------ | -------------------------- | ------------------ | ---------- |
| `Accepted`              | `cancel.accepted`              | → CancelAccepted → Canceled | n/a                | **Spent**  |
| `Pending`               | (none — await webhook)         | stay CancelRequested       | n/a                | hold       |
| `Declined`              | **`cancel.declined`** *(NEW)*  | → Confirmed                | **NOT set**        | Refunded   |
| `GatewayTimeout`        | `cancel.rejected_by_provider`  | → Confirmed                | set true           | Refunded   |
| `Unavailable`           | `cancel.rejected_by_provider`  | → Confirmed                | set true           | Refunded   |

Key distinction: **`Declined` does NOT set refund eligibility** — the order is
already at provider-side terminal refunded state. Setting eligibility true would
grant phantom refunds on already-settled refunds. `cancel.declined` idempotent
POST replay returns `409 ORDER_NOT_CANCELLABLE{reason}` with the mapped reason
from `MapProviderReason()`.

## What WI-CANCEL-1 backend now owes (queued for `wi-cancel-1-backend/`)

Production wiring stays gated on SPM v1 reaching 100%. When dispatched:

1. **`StripeProviderReasonMapper`** + **`AdyenProviderReasonMapper`** —
   `IProviderReasonMapper` implementations per DR-004 R2 table (in
   `IProviderReasonMapper.cs` header).
2. **`cancel.declined` webhook handler** — mirrors `cancel.rejected_by_provider`
   for state (→ Confirmed) / rate-cap (Refunded) / inventory (NO release), BUT
   does NOT set refund eligibility. Persists mapped reason for 409 replay path.
3. **`cancel.declined` allowlist entry in webhook dispatcher** —
   `CancelWebhookEnvelope.Events.CancelDeclined`.
4. **Unmapped-fallback wiring** — when `MapProviderReason()` returns `null`:
   - Treat as `ProviderCancelOutcome.Unavailable` (route to
     `cancel.rejected_by_provider`, NOT `cancel.declined`).
   - Increment `cancel.failure_reason_unmapped` counter.
   - Log `providerReason` to cancel-audit container, discriminator
     `unmapped_declined_reason`. GATE-CANCEL-07 grep enforces it never
     leaks to client.
5. **`_debug/cancel-count/{orderId}` seam (#4)** — return `{pending, spent}`
   shape so QA's `RateLimitNonDeductionOnRejectTest.cs` two-case extension
   (covering both `cancel.rejected_by_provider` and `cancel.declined`) can
   assert both are 0 post-webhook.

## What QA owes (already pre-positioned)

QA has the test surface frozen across `cancel-v1-failurecode-allowlist/` +
`cancel-v1-dr001-followup/`. DR-004 adds:

- `WebhookCancelDeclinedTest.cs` — NEW.
- `ProviderReasonMappingTests.cs` — NEW pure unit, reflects over both adapters'
  `MapProviderReason()` against the DR-004 R2 table + unmapped → null contract.
- `RateLimitNonDeductionOnRejectTest.cs` — extended to 2 cases (rejected +
  declined).
- `CancelWebhookEnvelope.FailureCodes.All` drift guard — already covered by
  QA's `EnumerationGuard` pattern from refunds v1b. Adding a code without
  updating tests breaks build first.

## Ownership boundary (unchanged from refunds v1b model)

- **app-dev** owns: `CancelWebhookEnvelope.FailureCodes.*` constants,
  `CancelWebhookEnvelope.Events.*` constants, `IProviderReasonMapper`
  interface + `CancelIneligibilityReason` enum, per-adapter
  `MapProviderReason()` implementations.
- **QA** consumes: `using static CancelWebhookEnvelope.FailureCodes;`
  `using static CancelWebhookEnvelope.Events;` — never duplicates string
  literals. Drift guard reflects over `.All`.
- **review-deployment** consumes: drift detector asserts deployed webhook
  events match `Events.*`, deployed 409 reasons match `Codes.Reason*`. No
  string duplication in detector either.

## Apply order (unchanged)

refunds v1 → 100% → SPM v1 → 100% → cancel v1.

Within cancel v1 dispatch: CANCEL-6 infra ∥ CANCEL-2 UX → CANCEL-1 backend
(includes everything from DR-001 + DR-002 + DR-003 + DR-004) + seams → QA
bundle lands → 3 ∥ 5 → CANCEL-4 frontend → CANCEL-7 rollout.

## Contract surface for WI-CANCEL-1 backend — now fully frozen

Across DR-CANCEL-001 + DR-002 + DR-003 + DR-004. No further amendments
expected. v2 asks (partial cancel, history page, customer reason capture,
cancel+re-order single txn, goodwill credit) route to ideation, not silently
scoped into v1.

## Out of scope (route to ideation as v2)

- Partial cancel
- Cancel + re-order single transaction
- Customer-facing reason capture (CS-only for v1)
- Goodwill credit on canceled-after-fulfillment
- Cancellation history page (use order detail in v1)
