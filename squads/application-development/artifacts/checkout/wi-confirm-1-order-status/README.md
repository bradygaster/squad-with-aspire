# WI-CONFIRM-1 — Order Status Endpoint

**Closes:** ideation-research-planning's WI-CONFIRM-1 in `NEXT-VERTICAL-confirmation-page.md` (commit `24326cf`)
**Bundle commit:** see `git log` on `main`
**Apply order:** independent of the locked 8-bundle checkout-vertical stack. This is the start of the confirmation-page vertical and can land in its own PR.

## Files

| File | Drop location | Lines |
|---|---|---|
| `src/OrderStatusEndpoint.cs` | `src/TravelAssistant.Api/Checkout/OrderStatusEndpoint.cs` | ~170 |
| `tests/OrderStatusEndpointTests.cs` | `tests/TravelAssistant.Api.Tests/Checkout/OrderStatusEndpointTests.cs` | ~180 |

## Wire-up

Single line in `Program.cs` after the existing checkout endpoint registration:

```csharp
app.MapCheckoutOrderStatusEndpoint();
```

Bind your existing `OrdersRepository` to `IOrdersRepository` in DI. The interface surface is intentionally one method (`GetStatusAsync(orderId, ct)`) so an existing repo class can `: IOrdersRepository` without refactor.

## Contract — matches a11y spec §Polling Contract

```http
GET /api/checkout/orders/{orderId}/status
Authorization: Bearer <jwt>
If-None-Match: W/"…"  (optional)

200 OK
ETag: W/"<8 hex>"
Cache-Control: private, max-age=2
Content-Type: application/json

{
  "orderId":"order-1",
  "state":"pending|confirmed|payment_failed|inventory_released|canceled",
  "paymentState":"none|authorized|captured|failed|refunded",
  "fulfillmentState":"none|reserved|released|committed",
  "updatedAt":"2026-06-23T20:00:00Z",
  "etag":"W/\"…\""
}
```

## Security pattern reuse

- **IDOR-safe 404** — same pattern as `POST /api/checkout/confirm` from `hotfix-p0`. Sub mismatch returns 404, indistinguishable from "order doesn't exist". No `403` leakage.
- **JWT `sub` resolution** — uses `ClaimTypes.NameIdentifier` first, falls back to raw `sub` claim. Matches `TestAuthHandler` from `wi1c-redis-testauth` so the same `Bearer test:{sub}` tokens work in tests.
- **Support scope override** — `scope=order:read:any` lets a support agent read foreign orders. Standard OAuth scope syntax (space-separated, parses `scope` or `scp` claim).

## ETag design (matches AC strictly)

ETag is a SHA-256 of `(orderId, state, paymentState, fulfillmentState)`. **Excludes `UpdatedAt`** — the AC explicitly says "ETag changes ONLY when state OR paymentState OR fulfillmentState changes". A timestamp-only ledger touch (clock refresh, audit-trail write) must NOT invalidate the polling client's `max-age=2` cache — otherwise we'd defeat caching on every 2s poll.

Weak ETag (`W/`) because we're computing from a content fingerprint, not byte-equivalent of the body. RFC 7232 §2.1 — correct for our semantics.

`If-None-Match` honored → 304 with `ETag` + `Cache-Control` headers echoed (per RFC 7232 §4.1).

## Tests — 10 cases, all live (no Skip)

| # | Test | What it pins |
|---|---|---|
| 1–5 | `ReturnsCorrectWireValue_ForEachState` | All 5 states map to spec wire values |
| 6 | `SubMismatch_Returns404_NotForbidden` | IDOR pattern |
| 7 | `SupportScope_CanReadAnyOrder` | `order:read:any` override |
| 8 | `MissingOrder_Returns404` | True not-found |
| 9 | `Etag_IsStable_WhenStateUnchanged` | Idempotent reads |
| 10 | `Etag_Changes_WhenStateChanges` | Cache invalidation correctness |
| 11 | `Etag_DoesNotChange_WhenOnlyUpdatedAtChanges` | AC pin — timestamp excluded |
| 12 | `IfNoneMatch_Returns304_WhenEtagMatches` | RFC 7232 conditional GET |
| 13 | `Unauthorized_Returns401` | AuthN required |
| 14 | `InvalidOrderId_Returns400` | Input validation |

## WAF dependency

Tests rely on `CheckoutWebApplicationFactory` from the `waf-fake-payment-provider` bundle (commit `9761ce6`) — already on `main`. Two new test-only methods to add to that factory:

```csharp
public void SeedOrder(string orderId, string sub, OrderState state = OrderState.Pending);
public void UpdateOrderState(string orderId, OrderState newState);
public void TouchUpdatedAt(string orderId);
```

Suggested impl: `ConcurrentDictionary<string, OrderStatusSnapshot>` keyed by orderId, registered as a singleton `IOrdersRepository` via `ConfigureTestServices`. ~30 LOC addition.

## What this un-blocks

- **WI-CONFIRM-2** (quality-testing-squad) — NVDA/VoiceOver scripts now have a live endpoint to script against
- **WI-CONFIRM-3** (experience-design → app-dev) — frontend `useOrderStatus(orderId)` hook + `ConfirmationPage.tsx` can consume this contract

## No canary required

Per planning's apply note: read-only endpoint behind the existing checkout feature flag. No DB schema change, no infra delta. Standard PR review path.

— Bennett, application-development-squad
