# waf-touch-updatedat-seam

Additive 1-method extension to `CheckoutWebApplicationFactory` to unblock QA's
`OrderStatusPollingContractTests.cs` (specifically GATE-OSP-02).

## What it adds

```csharp
public void TouchUpdatedAt(string orderId)
```

Bumps only `UpdatedAt` on the seeded `OrderStatusSnapshot`. State/payment/fulfillment
fields are untouched, so the SHA-256 ETag (which excludes `UpdatedAt` per WI-CONFIRM-1
AC) MUST remain stable. This is the seam that ratifies our deliberate deviation from
the ideation spec: heartbeat writes do NOT force an a11y re-announce on the 2s poll.

## Drop location

`tests/TravelAssistant.Api.Tests/Checkout/CheckoutWebApplicationFactory.TouchUpdatedAt.cs`

## Prerequisites

1. Apply step 8 of the locked checkout stack: `waf-fake-payment-provider/`.
2. Make the existing `CheckoutWebApplicationFactory` class `partial` (add `partial`
   keyword to its class declaration in the waf-fake-payment-provider file). One-token edit.
3. Apply `wi-confirm-1-order-status/` (independent vertical, commit `1e30d04`).
4. Apply QA's `order-status-polling-tests/` bundle.

## Apply stack (current)

1. `hotfix-p0/`
2. `wi1c-redis-testauth/`
3. `wi4-wi5/`
4. `wi1a-nfc-amendment/`
5. `wi6-redis-di-reconcile/`
6. `webhook-debug-endpoint/`
7. `waf-fake-payment-provider/` (mark class `partial`)
8. `waf-touch-updatedat-seam/` ← THIS BUNDLE
9. Independent: `wi-confirm-1-order-status/`
10. QA: `order-status-polling-tests/`

EMU still blocks direct push to `tamirdresher/travel-assistant`. Maintainer applies
on top of `tamir/squad-fixes` branch.

## Why partial (not edit-in-place)

Keeps the seam in its own file so the diff against the locked 7-bundle production
stack is purely additive — no re-review of the original `CheckoutWebApplicationFactory`
needed. The one `partial` keyword on the existing class is the only edit-in-place change.
