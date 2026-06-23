# WI-6 DI reconciliation — Redis idempotency store wiring

Pins the config-key contract between azure-infrastructure-squad's `containerApp.redis-wiring.patch.bicep`
and application-development-squad's `RedisIdempotencyStore` (shipped earlier in
`squads/application-development/artifacts/checkout/wi1c-redis-testauth/`).

## Config key contract

| Env var (containerApp.bicep) | .NET config key                | Values                                          |
|------------------------------|--------------------------------|-------------------------------------------------|
| `Checkout__IdempotencyBackend` | `Checkout:IdempotencyBackend`  | `memory` (dev) / `redis` (staging, prod)        |
| `Checkout__Redis__Endpoint`    | `Checkout:Redis:Endpoint`      | e.g. `ta-redis-prod.redis.cache.windows.net:6380` |
| `AZURE_CLIENT_ID`              | n/a — picked up by `DefaultAzureCredential` | user-assigned MI client id           |

## What's wired

- **`memory` backend** → `InMemoryIdempotencyStore` (dev default; no Redis dep)
- **`redis` backend** →
  - `IConnectionMultiplexer` singleton, TLS 1.2, port 6380, Entra ID via `DefaultAzureCredential` + `ConfigureForAzureWithTokenCredentialAsync` (zero secrets, zero KV reads — matches `disableAccessKeyAuthentication: true` on the Redis module)
  - `RedisIdempotencyStore` from the `wi1c-redis-testauth` bundle
  - `RedisHealthCheck` (from azure-infra) with `Degraded` failure status (not `Unhealthy`) + tags `["ready","redis"]` so `/health/ready` includes it but `/health/live` excludes it. Avoids canary restart cycling during Standard-tier ~30s failover.

## Apply order (maintainer)

Stacks on top of:

1. `hotfix-p0/` (WI-1, WI-2, WI-3, WI-1a, WI-1b)
2. `wi1c-redis-testauth/` (WI-1c test-auth handler + WI-6 `RedisIdempotencyStore` + bicep env-var patch fragment)
3. `wi4-wi5/` (WI-4 inventory hold + WI-5 webhook idempotency)
4. azure-infra's `sec/infra-redis-idempotency` bundle (Redis module + PE + DNS + `containerApp.redis-wiring.patch.bicep` + `RedisHealthCheck.spec.cs`)
5. **This bundle** — drop `CheckoutIdempotencyServiceCollectionExtensions.cs` at `src/TravelAssistant.Api/Checkout/DependencyInjection/` and call from `Program.cs`:

```csharp
builder.Services.AddCheckoutIdempotency(builder.Configuration);
```

## Replaces

The DI registration sketch in `wi1c-redis-testauth/Program.snippet.cs` (which used a placeholder env-var name). This file is the canonical wiring that matches azure-infra's bicep env-var names exactly.

## Validation checklist

- [ ] `Program.cs` calls `AddCheckoutIdempotency(builder.Configuration)`
- [ ] `params/dev.bicepparam` → `idempotencyBackend = 'memory'`
- [ ] `params/prod.bicepparam` → `idempotencyBackend = 'redis'`
- [ ] `/health/ready` returns 503 in prod when Redis PING > 1s, 200 otherwise
- [ ] `/health/live` returns 200 regardless of Redis state (no restart cycling)
- [ ] Smoke: `POST /checkout/confirm` with replay returns identical body + status from Redis-backed store across two replicas
