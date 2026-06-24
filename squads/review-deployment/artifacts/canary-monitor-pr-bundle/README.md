# SEC-CHK-008 R6 Bundle 9a — Synthetic Canary Monitor

**Owner:** azure-infrastructure-squad
**Reviewers:** security-hardening-squad, quality-testing-squad, review-deployment-squad
**Spec:** `docs/security/sec-chk-008-canary-spec.md` (when pushed) / `squads/security-hardening/artifacts/preprod-security-gate/SEC-CHK-008-R6-canary-bundle-9a-spec.md`
**Gate:** GATE-CO-06b-canary (P1, 7 + 7 day burn-in)

## Files

| File | Purpose |
|------|---------|
| `managed-identity.bicep` | User-assigned MI with canary-scoped RBAC (KV Secrets User on canary KV, Metrics Publisher on canary AI namespace, LA Contributor on canary workspace). **No prod KV, no Cosmos, no app slot config read.** |
| `checkout-terminal-canary.bicep` | Container App canary runner (one per provider per slot). Posts to `/checkout/confirm` every ~3.75s, rotates 4 test PANs. |
| `alerts.bicep` | 5 Azure Monitor / App Insights alert rules (A1–A5). |
| `main.bicep` | Orchestrator — composes MI + 2 runners (stripe + adyen) + alerts per slot. |
| `main.canary.bicepparam` | Canary slot params. |
| `main.prod.bicepparam` | Prod slot params. |

## Decisions

### Q-9a-1 — Container App runner (not Azure Monitor synthetic)

**Decision: Container App.** Azure Monitor synthetic supports multistep + GET, but POST with custom JSON body + per-request bearer + idempotency key + provider `stripe-test-mode` header + CSPRNG amount jitter is dicey. Container App in the same Container Apps Environment as the checkout API gives:

- Identical network posture (same VNet, same egress, same private endpoints)
- Full control over POST body, headers, and timing
- Same managed-identity auth model as the rest of the stack
- Easy local dev / test (Docker run)

Trade-off: we own runner uptime + image pipeline. A4/A5 alerts cover runner failure.

### Q-9a-2 — `session_id` dimension included

Bounded cardinality (~184k unique/day/slot, well under App Insights limits). Debug value > cost when a single anomalous sample needs root-cause.

## Metric namespace

`checkout.canary_terminal_response_timing` — **NEW namespace, NOT widening `checkout.terminal_response_timing`**. Keeps RBAC scoping per GATE-CO-06c clean. Canary namespace dimensions:

- `reason_internal` (full fidelity — `hard_decline` | `fraud_block` | `insufficient_funds_terminal` | `provider_rejected_permanent`)
- `provider` (`stripe` | `adyen`)
- `slot` (`canary` | `prod`)
- `session_id` (per Q-9a-2)

Risk/finance dashboards keep reading `checkout.terminal_response_timing` (single-value `reason_public=declined_terminal`). Security + SRE dashboards read the canary namespace.

## Alert rules

| Rule | Severity | Window | Trigger |
|------|----------|--------|---------|
| A1 — Per-reason p99 ceiling | P2 | 5min | p99 > 1000ms (floor 800 + 200ms slack) sustained 5min |
| A2 — Pair-divergence | P2 | 5min | max(p99) − min(p99) across reasons > 100ms sustained 5min — **the actual fraud-oracle leak detector** |
| A3 — Mapper drift | P1 | 15min | test PAN stopped producing expected enum — recalibrate before re-enabling |
| A4 — Sample rate | P3 | 10min | samples/min < 12 (expected 16) — runner broken |
| A5 — Metric ingestion | P3 | 10min | zero samples — App Insights ingestion broken |

## Test card storage

Test PANs live in the **canary-scoped Key Vault** (`travel-assistant-kv-canary` for canary, `travel-assistant-kv-canary-prod` for prod). Secret names:

- `stripe-hard-decline-pan` → `4000000000000002`
- `stripe-fraud-block-pan` → `4100000000000019`
- `stripe-insufficient-funds-terminal-pan` → `4000000000009995`
- `stripe-provider-rejected-permanent-pan` → `4000000000000069`
- `adyen-*-pan` → equivalents from spec

**Never in `src/`, never in `infra/bicep/` outside `synthetic-monitors/`.** QA bundle 9a grep gate G6 enforces.

## Apply order (binding per security spec)

1. ✅ QA ships bundle 9 build-time portion
2. ✅ QA ships bundle 9a build-time portion (configuration + isolation tests)
3. **→ THIS BUNDLE: deploy to canary slot**
4. 7-day burn-in on canary slot — A1/A2 stay clean
5. Deploy to prod slot (same bicep, prod bicepparam)
6. 7 more days clean → GATE-CO-06b-canary flips ✅ in `docs/security/preprod-security-gate.md`

review-deployment owns the burn-in clock + gate flip PR with evidence (App Insights links + alert history).

## Validation

```pwsh
az bicep build --file main.bicep
az bicep build-params --file main.canary.bicepparam
az bicep build-params --file main.prod.bicepparam
az deployment group what-if `
  --resource-group travel-assistant-canary-rg `
  --template-file main.bicep `
  --parameters main.canary.bicepparam
```

## EMU push status

Push to `tamirdresher/travel-assistant` blocked by EMU on this workstation (same constraint as PR #44 / WI-6 patch+bundle). Files are pre-staged here. Filed GitHub issue (this turn) requesting review-deployment-squad open PR from these artifacts.

## Open follow-ups (out of scope for v1)

- Canary runner Dockerfile + .NET worker source (`infra/synthetic-monitors/runner/`) — implementation owed by review-deployment as part of build pipeline. Image must emit `checkout.canary_terminal_response_timing` metric with the 4 dimensions and `canary.mapper_drift` custom event on PAN-to-reason mismatch.
- TLS cert rotation for canary endpoint domain (covered by existing Front Door cert automation).
- Cost: 2× Container Apps consumption replicas (always-on, 0.25 vCPU / 0.5GiB) per slot ≈ ~$30/slot/month. P3 alerts shared with existing observability stack — no additional alert cost.
