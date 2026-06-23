# INF-4 / INF-5 wire-up — APP-9 + APP-10 contracts

**Date:** 2026-06-23
**Trigger:** app-dev shipped `feat/app-9-10-infra-contracts` @ `1189141`
**Status:** Bundle modules complete; ready for owner merge.

## INF-4 — Worker queue (APP-9 contract)

Source contract: `docs/architecture/queues.md` (app-dev repo).

| Field | Value |
|---|---|
| Tech | Azure Service Bus (Standard tier) |
| Queue name | `travel-assistant-worker-jobs` |
| Lock | 60s |
| Max delivery | 5 (then DLQ) |
| Dedup window | 10 min |
| Sessions | off |
| TTL | 14 days |
| AAD | enforced (`disableLocalAuth: true`) |

### Modules added

- `infra/bicep/modules/servicebus.bicep` — namespace + queue + AAD-only + runtime MI Data Owner role assignment scoped to the queue.
- `infra/bicep/modules/worker-app.bicep` — Container App with workload-identity KEDA `azure-servicebus` trigger (`messageCount: 20`, min 0, max 10).

### `main.bicep` integration (caller-side, owner to inline)

```bicep
module sb 'modules/servicebus.bicep' = {
  name: 'servicebus'
  params: {
    env: environmentName
    location: location
    runtimePrincipalId: runtimeMi.outputs.principalId
    tags: tags
  }
}

module worker 'modules/worker-app.bicep' = {
  name: 'worker-app'
  params: {
    env: environmentName
    location: location
    containerAppsEnvironmentId: cae.outputs.id
    acrLoginServer: acr.outputs.loginServer
    imageTag: workerImageTag
    runtimeUamiResourceId: runtimeMi.outputs.resourceId
    runtimeUamiClientId: runtimeMi.outputs.clientId
    serviceBusNamespaceFqdn: sb.outputs.namespaceFqdn
    serviceBusNamespaceName: sb.outputs.namespaceName
    workerQueueName: sb.outputs.queueName
    keyVaultUri: kv.outputs.uri
    appInsightsConnectionString: appInsights.outputs.connectionString
    tags: tags
  }
}
```

## INF-5 — Custom OTel metrics (APP-10 contract)

Metric names confirmed unchanged. Meter: `TravelAssistant.Agent`.

| Metric | Used in |
|---|---|
| `llm.tokens.in` | alert-{env}-llm-token-surge, dashboard |
| `llm.tokens.out` | alert-{env}-llm-token-surge, dashboard |
| `llm.cost.usd` | alert-{env}-cost-burn (existing), dashboard |
| `chip.cache.hit` | alert-{env}-chip-cache-degraded, dashboard |

### Alerts added to `alerts.bicep`

| Alert | Threshold | Severity | Notes |
|---|---|---|---|
| `alert-{env}-llm-token-surge` | > 500k tokens / 5m | Sev 2 | sum of `llm.tokens.in` + `llm.tokens.out` |
| `alert-{env}-chip-cache-degraded` | hit-rate < 30% over 15m | Sev 3 | uses `customDimensions.result == "hit"` |
| `alert-{env}-worker-queue-backlog` | ActiveMessages > 500 for 10m | Sev 2 | SB platform metric, not custom |

Existing alerts unchanged (5xx spike, cost burn, revision unhealthy).
`alertIds` output array expanded from 3 to 6.

## RBAC delta

`runtime-mi-roles.bicep` (already specified in prior bundle iteration) is unchanged. Service Bus role is granted **inside `servicebus.bicep`** at queue scope (least-privilege) rather than namespace scope — this is intentional to keep blast radius narrow if more queues are added later.

| Plane | Role | Scope |
|---|---|---|
| Service Bus data | Azure Service Bus Data Owner | queue (`travel-assistant-worker-jobs`) |
| Cognitive Services | Cognitive Services OpenAI User | AOAI account (runtime-mi-roles.bicep) |
| Redis data | Redis Data Owner access policy | Redis cache (runtime-mi-roles.bicep) |
| Cosmos data | Cosmos DB Built-in Data Contributor | Cosmos account (existing) |
| Key Vault | Key Vault Secrets User | KV (existing) |

## Merge sequencing

1. SEC PR #39 (canonical KV path) — first.
2. app-dev `feat/app-9-10-infra-contracts` — second (or in parallel; no Bicep dependency).
3. This INF bundle — third. Owner applies `main.bicep` integration snippet above when inlining modules.

EMU still blocks `tamirdresher_microsoft` push to `tamirdresher/travel-assistant`. Bundle delivered via squad-message + patch artifact.
