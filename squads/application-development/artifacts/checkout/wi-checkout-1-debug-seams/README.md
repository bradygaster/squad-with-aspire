# wi-checkout-1-debug-seams — Frozen `_debug/*` route + response contract

**Bundle scope.** Pre-staged route + field-name contract for the 8 `_debug/*` endpoints required by QA bundles 3/4/5 (session `68815dc6`) and exp-design EXP-CHECKOUT-001 bundle 4 (cart-diff seed). Single source of truth — when `wi-checkout-1-backend/` ships, handlers wire against `CheckoutDebugSeamContract.Routes.*` constants directly. Any route or field rename is a build break.

**Apply order.** Ships pre-backend. QA fixtures bind to these constants today; handlers materialize when backend bundle lands.

## Gating discipline (matches cancel v1 + GATE-CO-06e)

- Every endpoint reads `Environment.GetEnvironmentVariable("CHECKOUT_DEBUG") == "1"` **at request time** — no `IOptions<>`, no static cache. Flipping requires process restart.
- When unset: return **404**, not 403. 403 confirms the route exists → its own enumeration oracle. The middleware must short-circuit identically to a non-existent route.
- `?debug=1` query string and `X-Debug-Mode` header on **production** routes (`/api/checkout/*`) are rejected with 400 regardless of env-var state — covered by `ForceReasonTestSeam.ShouldReject400DebugEscapeHatch` (security bundle commit `317e476`).
- review-deployment GATE-CO-06e scans every deployment env beyond `dev` for `CHECKOUT_DEBUG` presence → fail-deploy.

## Route catalog

| Route | Method | Purpose | Consumer |
|---|---|---|---|
| `/_debug/inventory-reservation/{sku}` | GET | `{ reserved, holders[{orderId,expiresAt}] }` | QA bundle 4 reservation-race + GATE-CO-03 |
| `/_debug/inventory-ledger/{sku}` | GET | `{ acquireCount, convertToSaleCount, releaseCount }` | QA bundle 4 one-retry-no-double-reserve |
| `/_debug/inventory-reservation/{sessionId}/expire` | POST | Force TTL expiry without 90s wall-clock wait | QA bundle 4 janitor-sweeps-orphans |
| `/_debug/inventory-reservation/janitor/run` | POST | Manual janitor invocation | QA bundle 4 |
| `/_debug/provider-call-count/{checkoutSessionId}` | GET | `{ callCount }` for replay assertion | QA bundle 3 idempotency replay |
| `/_debug/review-snapshot/{checkoutSessionId}` | GET | `{ computedAt, ttlSeconds, total, taxTotal, shippingTotal }` | QA bundle 5 tax recalc |
| `/_debug/review-snapshot/{checkoutSessionId}/expire` | POST | Age snapshot past 60s TTL | QA bundle 5 force-409 path |
| `/_debug/provider-delay/{checkoutSessionId}` | PATCH / DELETE | Inject artificial latency for in-flight 409 window | QA bundle 3 in-flight-duplicate |
| `/_debug/cart-changes/{cartId}` | POST | Seed `changes[]` on a cart without chaining failed-confirm | exp-design bundle 4 |

`ITimeProvider` DI replacement is an acceptable substitute for `*/expire` endpoints if the backend author prefers — QA bundle 4 §"Required test seams" explicitly accepts either.

## Body shape — cart-changes seed (exp-design ask)

```json
POST /_debug/cart-changes/{cartId}
{
  "changes": [
    {
      "lineItemId": "li_abc123",
      "kind": "price_increased",
      "reason": "price_changed",
      "oldPriceMinorUnits": 12000,
      "newPriceMinorUnits": 14500
    },
    {
      "lineItemId": "li_def456",
      "kind": "removed",
      "reason": "out_of_stock"
    }
  ]
}
```

`kind` ∈ `removed | price_increased | price_decreased | quantity_reduced | shipping_unavailable` (frozen 5-value allowlist per DR-CO-EXPDESIGN-ANSWERS-001 Q2).
`reason` ∈ `CheckoutErrorEnvelope.Codes.Reason*` allowlist (`out_of_stock | price_changed | shipping_unavailable`).

Seeded state persists for 60s OR until any user-initiated cart mutation (same lifecycle rule as real `CONFIRM_REJECTED` cart diff).

## Q-CO-RECONCILE — answer to QA

**No reconciliation owed.** Production code at `src/TravelAssistant.Api/Checkout/**` is currently empty (greenfield) — checkout v1 still queued behind refunds v1 → SPM v1 → cancel v1. There is no reserve-at-add-to-cart legacy path to undo. When `wi-checkout-1-backend/` lands, the confirm handler will reserve at `Review → Confirming` from the start per CHECKOUT-SPEC-001 R3.

GATE-CO-03 (reserved == 0 across `{Cart, Shipping, Payment, Review}`) will be greenfield-clean on first run. No DR-CO-RECONCILE-001 patch needed.

## Mapper drift tests — QA owns

When `wi-checkout-1-mappers/` ships (Stripe + Adyen `IProviderDeclineReasonMapper` concrete implementations, same shape as cancel `06873f7`), QA authors `MappingTable` exhaustive-coverage tests against the `IReadOnlyDictionary<string, PaymentDeclineReason>` view exposed by both adapters. No new envelope shape needed — `Reasons.ForEnum()` symmetric assertions and `StringComparer.Ordinal` cross-adapter equality match the cancel `ProviderReasonMappingTests.v2.cs` pattern verbatim.

## CI grep gates (post-apply)

```bash
# Forbid debug routes leaking into production handler trees
grep -rE '_debug/(inventory|provider-call|review-snapshot|cart-changes|provider-delay)' src/TravelAssistant.Api/ | grep -v 'Checkout/Debug/'

# Forbid reading CHECKOUT_DEBUG outside the debug middleware
grep -rE 'CHECKOUT_DEBUG' src/TravelAssistant.Api/ | grep -v 'Checkout/Debug/'

# Confirm short-circuit returns 404, not 403, when env unset
grep -rE 'StatusCodes\.Status403Forbidden' src/TravelAssistant.Api/Checkout/Debug/
```

All three must return empty after backend wires.

## Ownership boundary (refunds v1b model, preserved)

- **app-dev OWNS** `CheckoutDebugSeamContract.Routes.*` + `ResponseFields.*` constants and the env-gate middleware
- **QA CONSUMES** via `using static CheckoutDebugSeamContract` — zero magic strings in test fixtures by construction
- **review-deployment ENFORCES** GATE-CO-06e env-var absence past dev + 404-not-403 short-circuit on canary post-deploy

## Files

| File | Purpose |
|---|---|
| `CheckoutDebugSeamContract.cs` | Frozen route + field-name constants |
| `README.md` | This file — apply order, gating discipline, route catalog, reconcile answer |
