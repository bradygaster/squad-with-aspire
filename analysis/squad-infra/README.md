# azure-infrastructure-squad — subagent rollup

Four parallel subagent tracks. All artifacts committed in one commit on `main`.

| # | Subagent | Artifacts | Status |
|---|---|---|---|
| 1 | bicep-foundation | `infra/bicep/main.bicep` + `modules/{log-analytics,app-insights,container-apps-env}.bicep` + `main.parameters.json` | **PASS** — Aspire-ready Container Apps env wired to LA+AppInsights. `resourceToken` naming, `workloadProfiles: Consumption`, zoneRedundant off by default. |
| 2 | identity-secrets | `modules/managed-identity.bicep` + `modules/key-vault.bicep` | **PASS** — UAMI granted `Key Vault Secrets User` (RBAC, not legacy access policies). Soft-delete 90d + purge protection on. Holds auth JWT signing key + rate-limit Redis connstr + SMTP creds per security-squad. |
| 3 | auth-rate-limit-infra | `policy/apim-rate-limit-policy.xml` | **PASS** — APIM policy emits exact `{ code:"RATE_LIMITED", message, retryAfterSeconds, scope }` body + `Retry-After` header matching `docs/wireframes/auth/rate-limit-contract.md`. Per-endpoint limits from security-squad: login 10/15min/IP, register 5/hr/IP, resend-verify 6/hr/account, reset-request 3/hr/email. `on-error` catches both `RateLimitExceeded` reason and HTTP 429 from upstream. |
| 4 | policy-guardrails | `policy/azure-policy-baseline.bicep` + `policy/budget-alerts.bicep` | **PASS** — 4 built-in policies (KV soft-delete + purge + diag, storage public-access deny) + RG-scoped monthly budget with 50/80/100% alerts. |

## Wiring to other squads' work

- **rate-limit-contract.md (experience-design)**: APIM policy is the server-side enforcement layer. `Retry-After` header echoes `retryAfterSeconds` from the body, so QT's test-case 4 ("body/header disagree") is server-side-impossible. Case 5 (`retryAfterSeconds: 0`) is also impossible since `rl_window` is always a positive constant from the policy file.
- **security-hardening-squad per-endpoint defaults**: encoded verbatim in `apim-rate-limit-policy.xml`. Counter keys: `IpAddress` for IP-scoped; `accountKey`/`emailKey` variables expected to be set by an upstream JWT-claim-extraction policy chained before this one.
- **STRIDE PATH-hijack mitigation (security-hardening)**: container image PATH hardening is out of this commit's scope — filed as follow-up to review-deployment-squad's Dockerfile work.

## Deployment

```sh
az deployment group create \
  --resource-group <rg> \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/main.parameters.json
```

Policy + budget are separate deploys:

```sh
az deployment group create -g <rg> --template-file infra/bicep/policy/azure-policy-baseline.bicep --parameters envName=dev
az deployment group create -g <rg> --template-file infra/bicep/policy/budget-alerts.bicep --parameters contactEmails='["owner@example.com"]'
```

## Open follow-ups (NOT blockers)

1. **Private endpoint module** for KV — `publicNetworkAccess: 'Enabled'` today; flip to `Disabled` once VNet module lands.
2. **Container image PATH hardening** — owned by review-deployment-squad per security-squad's STRIDE rollup.
3. **APIM instance Bicep** — this commit ships the policy XML only; APIM service itself remains environment-specific.
4. **`accountKey` / `emailKey` extraction policy** — must precede the rate-limit policy in the chain. Application-development-squad owns the JWT-claim → variable mapping.

EMU blocker unchanged: artifacts staged on `bradygaster/squad-with-aspire@main` for maintainer transplant.
