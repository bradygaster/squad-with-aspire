# GitHub OIDC for travel-assistant

Bicep module that provisions the OIDC trust review-deployment-squad needs to make `deploy-staging.yml` actually deploy.

## What this creates

- User-assigned managed identity `id-gha-travel-assistant-{env}`
- Three federated credentials:
  - `repo:tamirdresher/travel-assistant:ref:refs/heads/main` (push-to-main deploys)
  - `repo:tamirdresher/travel-assistant:environment:{env}` (GH Environment gate)
  - `repo:tamirdresher/travel-assistant:pull_request` (PR previews)
- Contributor role on the target resource group

## Apply

```bash
# Per environment (staging + prod = two RGs, two deployments)
az group create -n rg-travel-staging -l westeurope
az deployment group create \
  -g rg-travel-staging \
  -f github-oidc.bicep \
  -p envName=staging githubOrg=tamirdresher repoName=travel-assistant

# Read the outputs into repo secrets/vars
az deployment group show -g rg-travel-staging -n github-oidc \
  --query properties.outputs -o json
```

## Wire to repo secrets (requires repo admin — EMU blocks us)

Set these as **repository secrets** (or environment secrets per `staging`/`prod`):

| Secret | Source |
|---|---|
| `AZURE_CLIENT_ID` | `outputs.AZURE_CLIENT_ID` |
| `AZURE_TENANT_ID` | `outputs.AZURE_TENANT_ID` |
| `AZURE_SUBSCRIPTION_ID` | `outputs.AZURE_SUBSCRIPTION_ID` |

And these as **repository variables**:

| Variable | Value |
|---|---|
| `AZURE_ENV_NAME` | `staging` (and `prod` for the prod env) |
| `AZURE_LOCATION` | e.g. `westeurope` |

Once set, `.github/workflows/deploy-staging.yml` from REL-2 stops being a no-op.

## Notes for review-deployment-squad

- The PR federated credential lets a future PR-preview workflow deploy ephemeral envs without long-lived secrets.
- If you want tighter scope, replace `Contributor` with a combo of `Container Apps Contributor` + `Key Vault Secrets User` + `AcrPush` — happy to split it.
- EMU constraint applies: we cannot push this Bicep to the repo or open the PR. Patch + this README are the deliverable.
