# Azure OpenAI — Region Strategy & Quota Runbook

**INF-2 acceptance:** primary + 2 fallbacks documented; quota request runbook captured; Bicep parameterized by region.

## Region Priority

| Tier | Region | Rationale | Models needed |
|------|--------|-----------|---------------|
| **Primary** | `eastus2` | Highest GlobalStandard quota for gpt-4o family; lowest p95 latency from East-coast users. | gpt-4o, gpt-4o-mini, text-embedding-3-small |
| **Fallback 1** | `swedencentral` | EU data residency option for travelers in EMEA; full model parity. | gpt-4o, gpt-4o-mini, text-embedding-3-small |
| **Fallback 2** | `australiaeast` | APAC latency floor; gpt-4o GA. | gpt-4o, gpt-4o-mini, text-embedding-3-small |

Region is supplied to `modules/aoai.bicep` via the `location` parameter. To switch regions:
```
azd env set AZURE_OPENAI_LOCATION swedencentral
azd provision
```

## Initial Quota Targets (per env)

| Deployment | Dev | Staging | Prod |
|------------|-----|---------|------|
| gpt-4o (GlobalStandard, TPM/1k) | 50 | 200 | 800 |
| gpt-4o-mini (GlobalStandard, TPM/1k) | 200 | 500 | 2000 |
| text-embedding-3-small (Standard, TPM/1k) | 100 | 200 | 500 |

Capacity in `aoai.bicep` is in **thousands of tokens per minute (TPM)**, not requests. Adjust the `capacity` field in the `deployments` array.

## Quota Request Runbook

**File the quota request the same day INF-2 work starts.** Approval can take 3–10 business days.

1. **Azure Portal → AI Services → Quotas** (left nav).
2. Filter: Provider = `Azure OpenAI`, Subscription = `<sub-id>`, Region = `eastus2`.
3. Select model + SKU row (e.g. `gpt-4o GlobalStandard`). Click **Request quota**.
4. Form fields:
   - Requested quota (TPM, in thousands): sum across all envs + 20% headroom (e.g. 50+200+800 = 1050 → request **1300**).
   - Business justification: *"Production travel-planning assistant; per-session refinement chips driving ~5 calls/user/hr at 2k input tokens; FinOps budget 1850 USD/month."*
   - Expected traffic ramp: paste from FinOps forecast.
5. Submit. Note ticket ID in `decisions.md`.
6. If denied: file ticket via **Help + support → New support request → Service & subscription limits (quotas) → Cognitive Services**. Attach the FinOps forecast PDF.

## Pre-Provision Validation

`azure.yaml` `predeploy` hook runs:
```
az cognitiveservices account list-skus --location $AZURE_LOCATION --kind OpenAI --query "[?contains(name,'S0')]"
```
If the SKU list is empty, the region is unavailable for the sub — fall through to Fallback 1.

## Model Availability Matrix (verify before changing primary)

Check live via: `az cognitiveservices model list --location <region> --kind OpenAI -o table`

| Model | eastus2 | swedencentral | australiaeast |
|-------|---------|---------------|---------------|
| gpt-4o 2024-08-06 | ✅ | ✅ | ✅ |
| gpt-4o-mini 2024-07-18 | ✅ | ✅ | ✅ |
| text-embedding-3-small | ✅ | ✅ | ✅ |

Last verified: 2026-06-23 — re-verify quarterly.

## Failover Playbook

If primary region hits quota or has an incident:
1. `azd env set AZURE_OPENAI_LOCATION swedencentral`
2. `azd provision --no-prompt` (provisions new AOAI account in fallback)
3. App reads `AZURE_OPENAI_ENDPOINT` from Container App env — restart revisions to pick up: `az containerapp revision restart -n <app> -g <rg>`
4. Update FinOps budget alert region tag.
5. File post-incident note in `.squad/decisions.md`.
