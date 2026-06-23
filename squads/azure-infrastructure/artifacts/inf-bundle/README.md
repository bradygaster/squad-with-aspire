# Infrastructure Bundle — INF-1 through INF-6

EMU blocks direct push/PR to `tamirdresher/travel-assistant`. This bundle is delivered as a drop-in payload.

## Contents

| Path | INF | Notes |
|------|-----|-------|
| `azure.yaml` | INF-1 | `azd` template; pre-deploy quota probe; post-provision env export. |
| `infra/bicep/modules/aoai.bicep` | INF-2 | AOAI account + 3 model deployments; MI-only auth. |
| `infra/bicep/modules/postgres.bicep` | INF-1 | Flexible server; Entra-only; password auth disabled. |
| `infra/bicep/modules/redis.bicep` | INF-1 | Basic/Standard/Premium; AAD-enabled; non-SSL disabled. |
| `infra/bicep/modules/alerts.bicep` | INF-5 | 5xx spike, LLM cost burn, revision unhealthy. |
| `infra/dashboards/travel-assistant-dashboard.json` | INF-5 | Latency, errors, LLM tokens, LLM cost USD, chip cache hits. |
| `docs/ops/aoai-regions.md` | INF-2 | Primary + 2 fallbacks; quota runbook; failover playbook. |

## Already Shipped Earlier

- `../oidc/github-oidc.bicep` — INF-3 (workload identity federation: main / per-env / PR).
- `../inf-2-kv-uri-envvar.md` — INF-2 KV URI wiring decision.
- `../inf-2-inf-4-sec-wireup.patch` — Container Apps + Key Vault MI wireup.

## How to Apply (when EMU lifts or via human relay)

```pwsh
cd <travel-assistant repo>
git checkout -b infra/inf-bundle
# Copy bundle contents over the repo root:
robocopy <path-to-inf-bundle> . /E /XF README.md
git add azure.yaml infra docs
git commit -m "feat(infra): INF-1/2/5 — azd template, AOAI+Postgres+Redis modules, dashboards, alerts"
gh pr create --title "INF-1/2/5: azd template, AOAI module, observability" --body "See squad delivery."
```

## Wire-Up Notes for `main.bicep`

The existing `main.bicep` needs three module references added:

```bicep
module aoai 'modules/aoai.bicep' = {
  name: 'aoai'
  params: {
    name: '${prefix}-aoai'
    location: aoaiLocation        // new param, see aoai-regions.md
    tags: tags
  }
}

module postgres 'modules/postgres.bicep' = {
  name: 'postgres'
  params: {
    name: '${prefix}-pg'
    location: location
    entraAdminObjectId: appUami.outputs.principalId
    entraAdminName: appUami.outputs.name
    skuName: postgresSkuName       // 'Standard_B1ms' dev, 'Standard_D2ds_v5' staging+
    tier: postgresTier
    tags: tags
  }
}

module redis 'modules/redis.bicep' = {
  name: 'redis'
  params: {
    name: '${prefix}-redis'
    location: location
    skuName: redisSkuName          // 'Basic' dev, 'Standard' staging+
    capacity: redisCapacity
    tags: tags
  }
}

module alerts 'modules/alerts.bicep' = {
  name: 'alerts'
  params: {
    actionGroupId: ops.outputs.actionGroupId
    appInsightsId: monitoring.outputs.appInsightsId
    logAnalyticsId: monitoring.outputs.logAnalyticsId
    environmentName: environmentName
    location: location
    tags: tags
  }
}
```

Outputs to add at bottom of `main.bicep` (consumed by AppHost via `azd env get-values`):
```bicep
output AZURE_OPENAI_ENDPOINT string = aoai.outputs.endpoint
output AZURE_OPENAI_NAME string = aoai.outputs.name
output POSTGRES_FQDN string = postgres.outputs.fqdn
output POSTGRES_DB string = postgres.outputs.databases[0]
output REDIS_HOST string = redis.outputs.hostName
output REDIS_PORT int = redis.outputs.sslPort
```

## Env Parameter Additions

Add to `dev.bicepparam`, `staging.bicepparam`, `prod.bicepparam`:

```bicep
// dev
param aoaiLocation = 'eastus2'
param postgresSkuName = 'Standard_B1ms'
param postgresTier = 'Burstable'
param redisSkuName = 'Basic'
param redisCapacity = 0

// staging
param aoaiLocation = 'eastus2'
param postgresSkuName = 'Standard_D2ds_v5'
param postgresTier = 'GeneralPurpose'
param redisSkuName = 'Standard'
param redisCapacity = 1

// prod
param aoaiLocation = 'eastus2'
param postgresSkuName = 'Standard_D4ds_v5'
param postgresTier = 'GeneralPurpose'
param redisSkuName = 'Standard'
param redisCapacity = 2
```

## INF-3, INF-4, INF-6 Status

- **INF-3** — DONE (see `../oidc/github-oidc.bicep`).
- **INF-4** — Container Apps deployment already exists in `infra/bicep/modules/containerApps.bicep` per prior review. AOAI/Postgres/Redis env vars from this bundle plug into the existing `env` array. Scale rules already HTTP-based for API/Web; Worker needs queue rule added once worker queue is named.
- **INF-6** — Multi-env params + cost tagging shipped earlier in `%TEMP%\infra.patch` (budget.bicep + staging/prod `.bicepparam`).

## AOAI Quota — File Today

INF-2 long-pole risk. Follow `docs/ops/aoai-regions.md` § Quota Request Runbook. Target: 1300 TPM gpt-4o in eastus2.
