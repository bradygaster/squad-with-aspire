# Webhook Debug Event-Count Endpoint (closes QA ask #1)

**Branch:** `tamir/squad-fixes` (stack on `wi4-wi5`)
**Closes:** Hockney's QA owed-tests note (qty: 1 endpoint, 1 DI registration, 1 dispatch-path bump)

## What ships

`WebhookEndpoints.debug.v2.cs` — adds:

1. `WebhookDispatchCounter` singleton (`ConcurrentDictionary<string,int>`, process-local).
2. `MapWebhookDebugEndpoints(env)` extension — registers `GET /webhooks/payments/_debug/event-count/{eventId}` ONLY when:
   - `ASPNETCORE_ENVIRONMENT=Development` AND
   - `ASPNETCORE_ENABLE_TEST_AUTH=1`
   - Both must be true. Either alone = no endpoint registered.
3. Patch instructions (in the file's bottom comment block) for the existing `WebhookEndpoints.cs` dispatch path: bump counter AFTER `SETNX wh:evt:{id}` dedup succeeds, BEFORE `Commit/ReleaseAsync` — so a replayed event hits the dedup guard and never bumps the counter.

## Config keys (closes QA ask #2)

Confirmed against `wi4-wi5/WebhookEndpoints.cs`:

| Key | Path in appsettings | Type | Notes |
|---|---|---|---|
| Signing secret | `Checkout:Webhooks:SigningSecret` | string (raw HMAC key) | Per-tenant in prod; env var override `Checkout__Webhooks__SigningSecret` |
| Timestamp tolerance | `Checkout:Webhooks:TimestampToleranceSeconds` | int (seconds, default 300) | Hardcoded 300 in v1; reads config in this amendment |

Hockney — these are the exact keys to put in `ConfigureAppConfiguration` for `WebApplicationFactory<Program>`. No changes needed to your harness skeleton.

## Apply order (final state of the bundle stack)

1. `hotfix-p0` (WI-1, WI-2, WI-3, WI-1a, WI-1b)
2. `wi1c-redis-testauth` (WI-1c + WI-6 store)
3. `wi4-wi5` (WI-4 inventory hold + WI-5 webhook idempotency)
4. `wi1a-nfc-amendment` (re-baseline note — take v2 of security's 3 files)
5. `wi6-redis-di-reconcile` (DI + health check)
6. **`webhook-debug-endpoint` (this bundle)** ← new

All artifacts are patch files; EMU still blocks our push to `tamirdresher/travel-assistant`.

## Why a counter and not OpenTelemetry?

OTel would work but adds a dependency on the collector being up during k6 runs. The counter is 6 lines, zero deps, process-local, removed in prod by env-gate. Hockney's k6 storm pins to one replica via session affinity, so distributed counting is not required.

— Bennett, application-development-squad
