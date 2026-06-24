# Apply Instructions — SEC-CHK-008 R6 / GATE-CO-06b-canary

**Bundle:** synthetic canary monitor bicep + runner image build + promotion env scan
**Spec authority:** `squads/security-hardening/artifacts/preprod-security-gate/SEC-CHK-008-R6-canary-bundle-9a-spec.md`
**Source:** `squads/azure-infrastructure/.squad/session-state/859ed293-fe53-4941-b849-7a47a06f82e2/files/infra/synthetic-monitors/` (azure-infrastructure squad, bicep build clean)
**Reason for handoff:** EMU credentials on this workstation cannot `git push` or open PRs against `tamirdresher/travel-assistant` (same lockout pattern as PR #44 + every prior bundle).

## Branch + PR

```bash
git checkout -b squad/sec-chk-008-canary-monitor tamir/squad-fixes
```

## File map

| Source (this bundle) | Target in tamirdresher/travel-assistant |
|---|---|
| `managed-identity.bicep` | `infra/bicep/synthetic-monitors/managed-identity.bicep` |
| `checkout-terminal-canary.bicep` | `infra/bicep/synthetic-monitors/checkout-terminal-canary.bicep` |
| `alerts.bicep` | `infra/bicep/synthetic-monitors/alerts.bicep` |
| `main.bicep` | `infra/bicep/synthetic-monitors/main.bicep` |
| `main.canary.bicepparam` | `infra/bicep/synthetic-monitors/main.canary.bicepparam` |
| `main.prod.bicepparam` | `infra/bicep/synthetic-monitors/main.prod.bicepparam` |
| `README.md` | `infra/bicep/synthetic-monitors/README.md` |
| `canary-runner-image-build.yml` | `.github/workflows/canary-runner-image.yml` |
| `checkout-debug-env-scan.yml` (job snippet) | Append job to `.github/workflows/checkout-canary-promote.yml` and add `checkout-debug-env-scan` to `needs:` of every `promote-*` job |

## Pre-deploy placeholder resolution (azure-infra's 3 items)

1. **AI connection string** — both bicepparam files contain `__SET_VIA_ENV_OR_KEYVAULT__`. Resolve at deploy time via env injection from canary App Insights resource (PR #44 pattern).
2. **PagerDuty action group resource IDs** — bicepparam assumes existing groups in `travel-assistant-shared-rg` named `ag-pagerduty-p1`, `ag-pagerduty-p2`, `ag-pagerduty-p3`. Verify before deploy; A3 (mapper drift) → P1, A1/A2 → P2, A4/A5 → P3.
3. **Canary KV names** — `travel-assistant-kv-canary` (canary slot) and `travel-assistant-kv-canary-prod` (prod slot canary monitor). Confirm naming convention or update bicepparam.

## Runner image — author + build first

The bicep references `travelassistantacr.azurecr.io/canary-runner:v1.0.0`. Image does NOT exist yet. Per spec §"Runner contract" in `README.md`:

- .NET worker, single replica, polls `/checkout/confirm` ~3.75s (16/min)
- Reads 4 test PANs/provider from canary KV via DefaultAzureCredential
- Round-robin PAN selection + CSPRNG ±$0.50 amount jitter
- Emits `checkout.canary_terminal_response_timing` metric (4 dims: provider/slot/reason_internal/session_id)
- Emits `canary.mapper_drift` event when actual response enum ≠ PAN's expected enum
- Liveness `/health/live`, readiness `/health/ready`, SIGTERM drain

Author `apps/canary-runner/` (separate PR or same PR — recommend same PR to keep deploy atomic), then merge runner-image workflow first so image is in ACR before bicep apply.

## Apply order (binding)

1. ⏳ QA ships bundle 9 + 9a build-time portions (config/isolation tests + G6 grep gate)
2. **This PR** lands → image built + pushed to ACR by `canary-runner-image.yml`
3. `az deployment group what-if -g <canary-rg> -f infra/bicep/synthetic-monitors/main.bicep -p infra/bicep/synthetic-monitors/main.canary.bicepparam`
4. Deploy canary slot
5. 7-day burn-in — A1/A2/A3 stay clean
6. Deploy prod slot via `main.prod.bicepparam`
7. 7 more days clean → maintainer flips GATE-CO-06b-canary ☐→✅ in `docs/security/preprod-security-gate.md` with AI query evidence (sister `canary-burnin-tracker` job in `preprod-security-gate-v2-wireup` auto-heartbeats the SLA issue)

## PR title

```
SEC-CHK-008 R6 / GATE-CO-06b-canary: synthetic canary monitor bicep (bundle 9a)
```

## PR body

```
Wires the synthetic canary monitor per SEC-CHK-008-R6 bundle 9a spec.

- 3 bicep modules (managed-identity, checkout-terminal-canary ContainerApp, alerts) + orchestrator + bicepparam for canary + prod slots
- New `checkout.canary_terminal_response_timing` AI metric namespace (does NOT widen prod metric surface — Q-9a-1)
- session_id dim included for cross-correlation (Q-9a-2)
- RBAC scoped to canary KV/AI/LAW only (no prod scope creep)
- 5 alerts: A1 per-reason p99, A2 pair-divergence (fraud-oracle detector), A3 mapper-drift → P1, A4/A5 secondary
- Container App over Azure Monitor synthetic per Q-9a-1
- Single replica fixed; CSPRNG amount jitter to defeat amount-pattern detection
- New `canary-runner-image` workflow builds + pushes image to ACR with Trivy gate
- New `checkout-debug-env-scan` job appended to `checkout-canary-promote.yml` enforcing GATE-CO-06e across bicep + workflow + runner source

Cost: ~$80/month across canary+prod slots (2 ContainerApp replicas + AI custom metric ingestion).

Closes: nothing yet — GATE-CO-06b-canary flips ☐→✅ after 14-day burn-in (7 canary + 7 prod) tracked by `canary-burnin-tracker` job from `preprod-security-gate-v2-wireup` (commit 6070f3f).

Stacked on: tamir/squad-fixes (current checkout base).

Refs: SEC-CHK-008-R6, GATE-CO-06b-canary, Q-9a-1, Q-9a-2.
```

## Reviewers

- `security-hardening` (gate authority for SEC-CHK-008)
- `azure-infrastructure` (bicep author)
- `quality-testing` (owns sister `CanaryRunbookSmokeTest.ps1` and bundle 9/9a build-time tests)

## Zero new secrets / new envs

- AI connection string resolved at deploy from existing canary AI resource
- Reuses `AZURE_CLIENT_ID/TENANT_ID/SUBSCRIPTION_ID` (existing since PR #44)
- ACR creds via managed identity (no PAT)
- Canary KV is new infra resource but no GH-Actions secret pointing at it (DefaultAzureCredential in runner)

## What flips on day 14

Maintainer opens follow-up PR editing `docs/security/preprod-security-gate.md`:

```diff
-| GATE-CO-06b-canary | ... | P1 | ☐ | ... |
+| GATE-CO-06b-canary | ... | P1→P0 | ✅ 2026-MM-DD | <AI query link or alert-history screenshot> |
```

`canary-burnin-tracker` issue auto-closes when the row flips (PR comment hook).
