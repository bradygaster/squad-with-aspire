# Redis residual key risk (SEC-3 / DELTA-2 followup)

## Context

`infra/bicep/modules/redis.bicep` enables Entra ID (AAD) auth via the `aad-enabled` config flag. Per Microsoft Cache for Redis documentation, **enabling AAD does not disable the existing access keys**. Keys remain present and remain valid auth material until explicitly disabled.

True key disablement requires `properties.disableAccessKeyAuthentication: true`. This API surface is **Premium SKU only** (GA 2024). On Basic and Standard SKUs the property is silently ignored.

## Posture by SKU

| SKU | `disableAccessKeyAuthentication` honored? | Mitigation |
|---|---|---|
| Basic / Standard (dev, staging) | No | RBAC-based: deny `Microsoft.Cache/redis/listKeys/action` to all principals except a named break-glass group. |
| Premium (prod) | Yes | Set `disableAccessKeyAuthentication: true` via the `disableAccessKeyAuthentication` parameter. Keys become non-functional even if extracted. |

## Risk accepted (current state)

- Dev + staging run Basic/Standard SKUs. Keys exist. Runtime uses AAD only (Container Apps MI). Key extraction requires `listKeys` RBAC on the Redis resource, which is restricted to RG Owners + Cache Contributors.
- Production runs Premium SKU with `disableAccessKeyAuthentication: true`. No residual key risk.

## Verification

After deployment, confirm key-vs-AAD posture:

```bash
# AAD token path should work
redis-cli -h <hostname>.redis.cache.windows.net -p 6380 --tls AUTH <aad-token>

# On Premium with keys disabled, this MUST fail
redis-cli -h <hostname>.redis.cache.windows.net -p 6380 --tls -a <access-key>
```

## Ownership

- Bicep parameterization: azure-infrastructure-squad (DELTA-2 fix landed in INF bundle).
- RBAC deny policy authoring: security-hardening-squad (SEC-9).
- Break-glass group provisioning: owner (manual).
