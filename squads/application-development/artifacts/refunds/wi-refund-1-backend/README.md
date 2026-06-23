# WI-REFUND-1: Refunds backend bundle

Closes WI-REFUND-1 (backend endpoint + eligibility) and WI-REFUND-4 app-side (webhook handler).
Un-blocks QA's 14 integration tests + 6 P0 gate tests in `refunds-v1-test-plan` bundle.
Un-blocks experience-design's WI-REFUND-2 (frontend) — server contract is now stable.

## Files (5)

| File | Purpose |
|---|---|
| `RefundsEndpoints.cs` | POST `/api/refunds`, GET `/api/orders/{id}` with `eligibleActions` |
| `RefundWebhookHandler.cs` | POST `/webhooks/refunds` — Stripe-style dedup + failure-code allowlist |
| `InMemoryRefundsRepository.cs` | In-memory `IOrdersRepository`, `IRefundsRepository`, rate limiter |
| `WafRefundsSeams.cs` | `FrozenRefundClock`, `FakeRefundPaymentProvider`, `RefundDebugCounter`, `_debug/refund-count` |
| `README.md` | This file |

## Contract decisions (frozen)

| Decision | Value | Source |
|---|---|---|
| Window anchor | `order.confirmedAt` | planning cc08d34 |
| Window length | 24h hard cutoff | refunds vertical spec |
| Ineligibility response | `409 Conflict` `{ error: "order_not_refundable", reason }` | planning cc08d34 |
| Reason enum | `canceled \| already_refunded \| not_confirmed \| window_expired` | planning cc08d34 |
| Provider `re_xxx` exposure | **NEVER serialize to clients** (SEC-RFD-001) | planning cc08d34 |
| Rate limit | 100/sub/24h (tighter than checkout's 1000 — different shape) | planning cc08d34 |
| Idempotency | reuses checkout primitives — header `Idempotency-Key`, body-hashed, sub-scoped, 422 on body mismatch, 409 in-flight | hotfix-p0 + wi1a-nfc-amendment |
| Refund ID | ULID, opaque, our-issued | planning cc08d34 |
| Refund scope (v1) | full only, no partial | spec § 9 |
| Failure code allowlist | `PROVIDER_DECLINED \| PROVIDER_TIMEOUT \| PROVIDER_UNAVAILABLE \| INSUFFICIENT_PROVIDER_FUNDS`; unmapped → `PROVIDER_DECLINED` + `refund.failure_reason_unmapped` telemetry | design 4c84355 § 5.2 |

## HTTP status codes

| Endpoint | Status | When |
|---|---|---|
| POST /api/refunds | 202 Accepted | Refund created (pending state, provider call in flight or webhook will finalize) |
| POST /api/refunds | 200 OK | Sync provider decline (cached) |
| POST /api/refunds | 400 | Missing `Idempotency-Key` header, malformed JSON, missing `orderId` |
| POST /api/refunds | 401 | No `sub` claim |
| POST /api/refunds | 404 | Order missing OR sub mismatch (IDOR-safe, no existence leak) |
| POST /api/refunds | 409 | `order_not_refundable` (reason field), in-flight idempotent replay, refund already exists |
| POST /api/refunds | 422 | Idempotency-Key reused with different body |
| POST /api/refunds | 429 | 100/sub/24h cap hit |
| GET /api/orders/{id} | 200 | Always, with `eligibleActions: []` when not refundable (button absent client-side) |
| GET /api/orders/{id} | 404 | IDOR or missing |

## DI wiring

```csharp
// Program.cs
builder.Services.AddRefundsVertical(); // in-memory defaults
app.MapRefundsEndpoints();
app.MapRefundWebhooks();

// Test fixture (CheckoutWebApplicationFactory) — add to ConfigureTestServices:
services.RemoveAll<IRefundClock>();
services.AddSingleton<FrozenRefundClock>(new FrozenRefundClock(DateTimeOffset.Parse("2026-06-24T12:00:00Z")));
services.AddSingleton<IRefundClock>(sp => sp.GetRequiredService<FrozenRefundClock>());

services.RemoveAll<IPaymentProvider>();
services.AddSingleton<FakeRefundPaymentProvider>();
services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<FakeRefundPaymentProvider>());

services.AddSingleton<RefundDebugCounter>();
app.MapRefundDebug();
```

## Test fixture seams (closes QA's 6 asks)

```csharp
// SeedRefundableOrder — partial class extension on CheckoutWebApplicationFactory
public void SeedRefundableOrder(string orderId, string sub, DateTimeOffset confirmedAt,
                                long amount = 9900, string currency = "USD")
{
    var orders = (InMemoryOrdersRepository)Services.GetRequiredService<IOrdersRepository>();
    orders.Seed(new OrderRecord(orderId, sub, "confirmed", confirmedAt, amount, currency,
                                paymentIntentId: $"pi_{orderId}"));
}

public void SeedRefund(string refundId, string orderId, string sub, string status, string? providerRefundId = null)
{
    var refunds = (InMemoryRefundsRepository)Services.GetRequiredService<IRefundsRepository>();
    refunds.InsertAsync(new RefundRecord(refundId, orderId, sub, status,
                                         DateTimeOffset.UtcNow, providerRefundId, null), default).GetAwaiter().GetResult();
}
```

## Apply order

Independent vertical (like wi-confirm-1). Apply after the locked 10-bundle checkout stack;
no ordering dependency with QA's `refunds-v1-test-plan` bundle (tests un-skip per seam).

1. Locked checkout stack (hotfix-p0 → ... → waf-touch-updatedat-seam) — already shipped
2. **This bundle** (`wi-refund-1-backend`) — new files only, no edits to existing
3. QA's `refunds-v1-test-plan` bundle — tests un-skip against seams above
4. WI-REFUND-2 frontend (experience-design hand-off)
5. WI-REFUND-3 UX spec already frozen (4c84355) — pure frontend impl

## Out of scope (per spec § 9)

Partial refunds, multi-item refunds, reason capture from user, goodwill credits,
refund history page, refunds from `/orders/:id`. **Do not bolt on.** Route v2 requests
back to ideation-research-planning.

## Commit

Files staged under `squads/application-development/artifacts/refunds/wi-refund-1-backend/`.
Cross-account EMU push to `tamirdresher/travel-assistant` blocked — pattern is ship-as-artifact
on `tamir/squad-fixes` branch in `squad-with-aspire`, maintainer applies.
