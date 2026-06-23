# WI-1c + WI-6 app-side bundle

Stacks on top of `hotfix-p0/` (commit e18eb3c). Same branch: `fix/checkout-idempotency-p0`.

## What this delivers

| Work item | What | For whom |
|---|---|---|
| **WI-1c.1 — Test auth seam** | `TestAuthHandler` accepting `Authorization: Bearer test:{sub}` → `ClaimsPrincipal` with `NameIdentifier` | QA cases 1, 2, 7 |
| **WI-1c.2 — XFF in IP cap** | `ClientIpResolver` honors `X-Forwarded-For` only from trusted proxy CIDRs (anti-spoof) | QA case 8 |
| **WI-6 app-side — Redis store** | `RedisIdempotencyStore : IIdempotencyStore`, Entra ID auth (no keys), Lua-atomic reserve | azure-infrastructure-squad hand-off (`sec/infra-redis-idempotency`) |
| **DI wiring** | `Checkout:IdempotencyBackend=memory\|redis` config switch | All envs |
| **Container App env fragment** | `Checkout__Redis__Endpoint` from Bicep output | review-deployment canary gate |

## Files

| File | Destination |
|---|---|
| `src/TestAuthHandler.cs` | `src/TravelAssistant.Api/Checkout/Auth/TestAuthHandler.cs` |
| `src/ClientIpResolver.cs` | `src/TravelAssistant.Api/Checkout/Idempotency/ClientIpResolver.cs` |
| `src/RedisIdempotencyStore.cs` | `src/TravelAssistant.Api/Checkout/Idempotency/RedisIdempotencyStore.cs` |
| `src/DI-registration.cs` | Merge into `Program.cs` (extension method shown) |
| `src/ContainerApp.env-additions.bicep` | Patch fragment for `infra/checkout/modules/containerApp.bicep` |
| `tests/TestAuthAndXffTests.cs` | `tests/TravelAssistant.Api.Tests/Checkout/TestAuthAndXffTests.cs` |

## NuGet additions (`src/TravelAssistant.Api/TravelAssistant.Api.csproj`)

```xml
<PackageReference Include="StackExchange.Redis" Version="2.7.33" />
<PackageReference Include="Microsoft.Azure.StackExchangeRedis" Version="3.0.1" />
<PackageReference Include="Azure.Identity" Version="1.12.0" />
```

## Apply recipe (maintainer / non-EMU account)

```bash
cd <travel-assistant>
git checkout fix/checkout-idempotency-p0   # branch holding WI-1/2/3 + WI-1a/1b
# Copy files from squads/application-development/artifacts/checkout/wi1c-redis-testauth/
# Merge DI-registration.cs into Program.cs.
# Apply the Bicep env-var patch.
dotnet build && dotnet test
git add -A && git commit -m "feat(checkout): test auth seam, XFF in IP cap, Redis idempotency store (WI-1c/WI-6)"
```

## Sequencing

1. **PR1 (`fix/checkout-idempotency-p0`)**: WI-1, WI-1a, WI-1b, WI-1c, WI-2, WI-3. Un-skip QA tests in PR #52.
2. **PR2 (`sec/infra-redis-idempotency`)**: azure-infrastructure-squad's Bicep — must merge before staging deploy of PR1.
3. **PR3 (still pending — WI-4)**: inventory hold service.
4. **PR4 (still pending — WI-5)**: webhook idempotency.

## EMU note

Same blocker as every other artifact this session. Patch + raw files live here for a non-EMU account to apply.

— Bennett, application-development-squad
