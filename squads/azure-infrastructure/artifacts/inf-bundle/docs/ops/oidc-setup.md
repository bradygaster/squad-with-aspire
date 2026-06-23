# GitHub OIDC → Azure Federation Setup

**Owner action required.** Repo secrets must be set by an account with `admin` on `tamirdresher/travel-assistant` (the `tamirdresher_microsoft` EMU account cannot — only the personal `tamirdresher` owner).

## What gets created (by INF-3 Bicep)

`infra/bicep/modules/github-oidc.bicep` provisions:

1. **User-Assigned Managed Identity** `id-travel-assistant-gh-{env}` per env (dev, staging, prod).
2. **3 Federated Credentials** per UAMI:
   - `main` → `repo:tamirdresher/travel-assistant:ref:refs/heads/main` (for CD-dev)
   - `env-{env}` → `repo:tamirdresher/travel-assistant:environment:{env}` (for CD-staging, CD-prod)
   - `pr` → `repo:tamirdresher/travel-assistant:pull_request` (for PR validation)
3. **Role assignments** scoped to the env resource group: `Contributor` + `AcrPush` + `Key Vault Secrets User`.

## One-time provisioning

```bash
# Deploy OIDC for each env (run as repo owner / sub Owner)
az deployment sub create \
  --location eastus2 \
  --template-file infra/bicep/modules/github-oidc.bicep \
  --parameters env=staging githubRepo=tamirdresher/travel-assistant

# Capture outputs
az deployment sub show -n github-oidc-staging \
  --query 'properties.outputs.{clientId:clientId.value,tenantId:tenantId.value,subscriptionId:subscriptionId.value}'
```

## Required GitHub repo secrets

Set per-environment in **Settings → Environments → {env} → Secrets** (NOT repo-level — env-scoped so prod creds aren't visible to dev workflows):

| Secret | Value | Notes |
|---|---|---|
| `AZURE_CLIENT_ID` | UAMI clientId from output above | Federated — no client secret needed |
| `AZURE_TENANT_ID` | Sub tenant id | Same for all envs |
| `AZURE_SUBSCRIPTION_ID` | Target sub id | May differ per env |

**`azure/login@v2` usage** (already wired in REL workflows):
```yaml
- uses: azure/login@v2
  with:
    client-id: ${{ secrets.AZURE_CLIENT_ID }}
    tenant-id: ${{ secrets.AZURE_TENANT_ID }}
    subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
```

Workflow must have `permissions: { id-token: write, contents: read }` at job level.

## Verify

```bash
# After secrets are set, trigger a dummy PR. Workflow should log:
#   "Login successful using OpenID Connect (OIDC)."
# If you see "AADSTS70021: No matching federated identity record found",
# the subject claim doesn't match — check env name spelling + branch name.
```

---

# Naming Convention (confirms REL workflow assumptions)

REL-1/3/5 patches assumed names. **Confirmed canonical**:

| Resource | Pattern | Example (staging) |
|---|---|---|
| Resource group | `rg-travel-assistant-{env}` | `rg-travel-assistant-staging` ✅ matches REL |
| Container Apps env | `cae-travel-assistant-{env}` | `cae-travel-assistant-staging` |
| Container App: API | `ca-api-{env}` ⚠️ **NOT `api`** | `ca-api-staging` |
| Container App: Web | `ca-web-{env}` | `ca-web-staging` |
| Container App: Worker | `ca-worker-{env}` | `ca-worker-staging` |
| ACR | `acrtravel{envshort}` (no dashes) | `acrtravelstg` (dev=`acrtraveldev`, prod=`acrtravelprod`) |
| Key Vault | `kv-travel-{env}-{4char}` | `kv-travel-staging-a1b2` (4-char uniq suffix) |
| Cosmos | `cosmos-travel-{env}` | `cosmos-travel-staging` |
| Postgres | `pg-travel-{env}` | `pg-travel-staging` |
| Redis | `redis-travel-{env}` | `redis-travel-staging` |
| App Insights | `appi-travel-{env}` | `appi-travel-staging` |
| Log Analytics | `log-travel-{env}` | `log-travel-staging` |
| UAMI (runtime) | `id-travel-app-{env}` | `id-travel-app-staging` |
| UAMI (GH OIDC) | `id-travel-assistant-gh-{env}` | `id-travel-assistant-gh-staging` |

**Action for review-deployment-squad:** Patch `deploy-staging.yml` Container App name from `api` to `ca-api-staging` (and parameterize by `${{ env.AZURE_ENV_NAME }}` so prod gets `ca-api-prod`). ACR name in CI push step: `acrtravel${{ env.AZURE_ENV_SHORT }}` where `dev=dev`, `staging=stg`, `prod=prod`.

Bicep outputs from `main.bicep` (already in inf-bundle, exposing for workflow consumption):
```bicep
output AZURE_CONTAINER_REGISTRY_NAME string = acr.outputs.name
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = acr.outputs.loginServer
output API_CONTAINER_APP_NAME string = api.outputs.name
output WEB_CONTAINER_APP_NAME string = web.outputs.name
output WORKER_CONTAINER_APP_NAME string = worker.outputs.name
output RESOURCE_GROUP_NAME string = rg.name
```

---

# Dev vs Staging Decision

**Decision: staging is the single lower env for v0.1. No separate `dev` env.**

Rationale:
- Cost: a 4th env (dev + staging + prod + local) doubles infra spend for marginal value when local AppHost (APP-1) gives every dev a full stack on their box.
- Velocity: main → staging keeps the deploy path simple; PR previews can come later via INF-6 P1 if needed.
- INF-6 (multi-env) stays P2 unless app-dev squad requests PR-preview envs explicitly.

**Action for review-deployment-squad:** Keep `deploy-staging.yml` as-is (main → staging). Do NOT fork a `deploy-dev.yml`. Update REL-3 acceptance criteria language: "main → staging" not "main → dev". Planning's original REL-3 wording was pre-scope-cut.

`docs/ops/rollback.md` (already implied by REL-3) — staging rollback = `az containerapp revision activate --revision <prev-sha>` against `ca-api-staging`. Prod rollback identical, against `ca-api-prod`, but gated by environment reviewer.
