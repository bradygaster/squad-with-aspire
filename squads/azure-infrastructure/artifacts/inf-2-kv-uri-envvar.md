# INF-2 follow-up — KeyVault:Uri env var wire-in + redundant module delete

**Author:** azure-infrastructure-squad (Vasquez)
**Depends on:** PR #39 merged + `fcbfb21` (patch `inf-2-inf-4-sec-wireup.patch`) applied
**Decision source:** Bishop (security-hardening-squad) — KV URI is non-secret routing; deliver as plain env var, not Container Apps secret reference.

## Change 1 — Inject `KeyVault__Uri` into the API Container App

Edit `infra/bicep/modules/containerApps.bicep` — add the param and the env entry:

```bicep
// near other params
@description('Key Vault URI for the API workload (read at startup by ProductionGuard + AddAzureKeyVault).')
param keyVaultUri string

// inside the API container app `template.containers[0].env` array, append:
{
  name: 'KeyVault__Uri'
  value: keyVaultUri
}
```

Edit `infra/bicep/main.bicep` — pass it through (the `containerApps` module call already exists; just add the one param):

```bicep
module containerApps './modules/containerApps.bicep' = {
  name: 'containerApps-deployment'
  params: {
    // ...existing params...
    keyVaultUri: keyVault.outputs.vaultUri   // NEW
  }
  dependsOn: [
    keyVault   // ensure KV is provisioned before CA env binds
  ]
}
```

**Why `__` (double underscore):** .NET `IConfiguration` maps `KeyVault__Uri` → `KeyVault:Uri` on Linux containers where `:` is not a valid env var character.

**ProductionGuard contract satisfied:**
- `cfg["KeyVault:Uri"]` non-empty ✅
- `Uri.TryCreate(...)` ✅
- Host ends with `.vault.azure.net` ✅ (Bicep `vaultUri` output is always `https://<name>.vault.azure.net/`)

## Change 2 — Delete redundant top-level module

```bash
git rm infra/modules/keyvault.bicep
# (Bishop confirmed in PR #39 thread — canonical is infra/bicep/modules/keyVault.bicep.)
```

If `infra/modules/` becomes empty, remove the directory too.

## Validation
```bash
az bicep build infra/bicep/main.bicep
# Expect: clean, only pre-existing BCP318 warnings.
```

After deploy, confirm in the portal: Container App → Containers → Environment variables → `KeyVault__Uri` shows the vault URI. Then probe `/health/prod-guard` → 200.

## Why not Container Apps secret reference?
- Vault URI is **not a secret** — `https://kv-<name>.vault.azure.net/` is public DNS. Wrapping it in a `secretRef` adds rotation/permission overhead for zero security benefit.
- ProductionGuard runs at startup, **before** any KV call. It needs the URI synchronously from `IConfiguration`, no secret-resolution hop.
- The auth boundary is the Managed Identity + `DefaultAzureCredential` against the URI — not the URI itself.
