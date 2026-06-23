# INF bundle — DELTA fixes (pre-merge, applied)

Security-hardening-squad reviewed `aoai.bicep`, `postgres.bicep`, `redis.bicep` and acked the core auth posture (no shared keys, Entra-only, MI for runtime). Three P1 deltas were folded in pre-merge rather than tracked as follow-ups.

## DELTA-1 · Postgres firewall scoping

**Before:** `AllowAllAzureServicesAndResourcesWithinAzureIps` (0.0.0.0/0.0.0.0) — permitted TCP from any Azure tenant.

**After:** Removed the 0/0 rule. Added `allowedOutboundIps array` parameter. When populated with Container Apps env outbound IPs, generates one explicit `AllowContainerAppsOutbound-{i}` rule per IP. Empty array = no public firewall rules (private endpoint path).

**Caller responsibility:** `main.bicep` must pass `containerAppsEnv.properties.outboundIpAddresses` (or the equivalent for whichever runtime hosts the app).

## DELTA-2 · Redis access-key truthful disablement

**Before:** `disableAccessKeys` param controlled only `redisConfiguration['aad-enabled']` — misleading; keys still existed and worked.

**After:**
- Renamed `disableAccessKeys` → `enableAadAuth` (truthful).
- New param `disableAccessKeyAuthentication` (default false). Set true on Premium SKUs to truly disable keys.
- Bicep guards the Premium-only API: `disableAccessKeyAuthentication: skuName == 'Premium' ? disableAccessKeyAuthentication : false`.
- Basic/Standard mitigation documented at `docs/security/sec-3/redis-residual-key-risk.md` (security-hardening owns SEC-9 deny-policy authoring).

## DELTA-3 · AOAI network ACL deny-by-default

**Before:** `networkAcls.defaultAction: 'Allow'` — endpoint reachable from anywhere on the internet (MI-only gates auth but blast radius is wide).

**After:**
- New params `denyByDefault bool = true` and `allowedOutboundIps array`.
- `networkAcls.defaultAction` now toggles via `denyByDefault`; default = `'Deny'`.
- `ipRules` populated from `allowedOutboundIps`.

**Caller responsibility:** `main.bicep` passes Container Apps outbound IPs + the deployer principal's egress (or runs `azd provision` from a machine inside the allowlist). For dev convenience, set `denyByDefault=false` per-env.

## Files touched

```
infra/bicep/modules/postgres.bicep   — DELTA-1
infra/bicep/modules/redis.bicep      — DELTA-2
infra/bicep/modules/aoai.bicep       — DELTA-3
docs/security/sec-3/redis-residual-key-risk.md   — DELTA-2 mitigation doc (new)
```

## Verification

`bicep build` clean on all three modules (no compile errors introduced). Param defaults preserve security-positive posture (`enableAadAuth=true`, `denyByDefault=true`); permissive paths require explicit caller opt-in.

## Cross-squad sign-off

Security-hardening cleared the bundle for merge. After bundle merges, security-hardening will ship a cleanup PR deleting redundant top-level `infra/modules/keyvault.bicep` (canonical = `infra/bicep/modules/keyVault.bicep`).
