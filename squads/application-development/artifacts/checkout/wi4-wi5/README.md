# WI-4 + WI-5 — Inventory Hold & Webhook Idempotency

**Branch target:** `fix/checkout-inventory-and-webhook` (new, off `main` after hotfix-p0 lands)
**Closes:** issue #50 (inventory race), issue #49 (missing webhook handler)
**Depends on:** WI-1c Redis wiring (already shipped in `wi1c-redis-testauth/`)

## Files

| Path | Purpose |
|---|---|
| `src/TravelAssistant.Api/Checkout/InventoryHold.cs` | `IInventoryHoldStore` + `RedisInventoryHoldStore` with Lua atomic reserve |
| `src/TravelAssistant.Api/Checkout/WebhookEndpoints.cs` | `/webhooks/payments` with HMAC verify + dedup + hold commit/release |
| `tests/TravelAssistant.Api.Tests/Checkout/InventoryAndWebhookTests.cs` | 10 cases — 6 inventory + 4 webhook |

## Wiring (Program.cs additions)

```csharp
builder.Services.AddSingleton<IInventoryHoldStore, RedisInventoryHoldStore>();
builder.Services.AddSingleton<IWebhookDedupStore, RedisWebhookDedupStore>();
builder.Services.AddSingleton(sp => new WebhookOptions
{
    Secret = builder.Configuration["Payments:WebhookSecret"]
        ?? throw new InvalidOperationException("Payments:WebhookSecret required"),
});

app.MapPaymentWebhook();
```

Add to `/checkout/session` endpoint (after payment intent creation):
```csharp
var hold = await holds.TryReserveAsync(sku, qty, holdId: orderId, scope: $"sub:{sub}",
    ttl: TimeSpan.FromMinutes(15), ct);
if (hold.Outcome == HoldOutcome.InsufficientStock)
    return Results.Json(new { error = "out_of_stock", available = hold.Available }, statusCode: 409);
```

## Threat model — what each piece blocks

| Attack | Defence |
|---|---|
| **Over-sell race (#50)** — two concurrent /session calls for last unit | Lua `DECRBY` + hold-write in one atomic redis call. Loser sees `InsufficientStock`. |
| **Hold leak** — abandoned cart locks stock forever | 15min TTL on hold key; `inv:cap:{scope}` zset trims expired holds on every reserve. |
| **Webhook replay (#49)** — attacker captures legit webhook, re-POSTs | Dedup via `SETNX wh:evt:{id}` with 7-day TTL. Second POST returns 200/"duplicate" with no side effect. |
| **Webhook forgery** — attacker crafts payload | HMAC-SHA256 with `FixedTimeEquals` constant-time compare. |
| **Signature replay window** — old captured webhook | 5min tolerance window on `t=<unix>`. |
| **Double-commit** — `succeeded` event arrives twice fast | Dedup claim happens BEFORE `CommitAsync`. Stock state stable. |
| **Race: succeeded + failed for same payment** | Provider guarantees one terminal event per PaymentIntent. If both arrive, dedup catches second by event-id, not type. If they have different event-ids, last writer wins — but `CommitAsync` and `ReleaseAsync` are both idempotent at the hold layer (delete vs return-stock-then-delete). Worst case is one wrong direction; logged + alertable. |

## Open follow-ups (track separately, NOT in this PR)

- **Webhook secret rotation** — currently single `Payments:WebhookSecret`. Stripe pattern accepts two valid secrets during rotation window. File as P2.
- **Dead-letter queue for non-2xx webhook responses** — Stripe retries with exponential backoff for 72h; if our handler is genuinely broken we need an audit trail. File as P2.
- **Hold reconciliation job** — drift between Redis `inv:stock:*` and database SKU table needs a daily sweep. File as P2.

## Apply order (maintainer)

1. `git checkout -b fix/checkout-inventory-and-webhook` off `main` (after #44, #52, CI PR, and hotfix-p0 merge).
2. Copy `InventoryHold.cs` + `WebhookEndpoints.cs` to `src/TravelAssistant.Api/Checkout/`.
3. Copy `InventoryAndWebhookTests.cs` to `tests/TravelAssistant.Api.Tests/Checkout/`.
4. Apply Program.cs wiring from this README.
5. Add `Payments:WebhookSecret` to `appsettings.Development.json` (test value) and document Key Vault binding for staging/prod (azure-infrastructure squad owns the Key Vault secret provisioning).
6. `dotnet test --filter "FullyQualifiedName~Inventory|FullyQualifiedName~Webhook"` — should be 6/10 green (4 webhook tests skip until WebApplicationFactory harness is in place).
7. Open PR with body: closes #49 #50, references QA's regression suite from PR #52.

## What's still owed after this lands

| WI | Status |
|---|---|
| WI-1 / 1a / 1b / 1c (idempotency hardening) | ✅ shipped, in hotfix-p0 + wi1c-redis-testauth bundles |
| WI-2 (status code preservation) | ✅ shipped in hotfix-p0 |
| WI-3 (currency precision) | ✅ shipped in hotfix-p0 |
| WI-4 (inventory hold) | ✅ this bundle |
| WI-5 (webhook idempotency) | ✅ this bundle |
| WI-6 (Redis store wiring) | ✅ shipped in wi1c-redis-testauth |

All six WIs shipped. The remaining work is maintainer apply + canary ramp under review-deployment's gates.

— Bennett, application-development-squad
