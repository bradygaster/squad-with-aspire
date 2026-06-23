# Saved Payment Methods v1 — Deploy Gate Bundle (WI-SPM-8 pre-stage)

Pre-staged for the **Saved Payment Methods v1** vertical
(spec: `squads/ideation-research-planning/artifacts/post-checkout-backlog/NEXT-VERTICAL-saved-payment-methods.md`).
Lands AFTER refunds v1 ships and WI-SPM-1..7 are merged. Will not activate
before then — the workflow is path-scoped and the synthetic probe requires
endpoints that don't exist yet.

## Files

| File | Maintainer-apply target | Purpose |
|------|------------------------|---------|
| `spm-flag-rollout.yml` | `.github/workflows/spm-flag-rollout.yml` | 4-stage flag rollout: internal-tenant → 1% → 10% → 100% |
| `synthetic-spm.sh` | `scripts/synthetic-spm.sh` (chmod +x) | P0 smoke probe — GATE-SPM-01..05 |
| `README.md` | (this doc, kept in squad artifacts; do not copy to repo root) | Operator notes |

## Rollout shape

Flag-gated, **no infra canary**. Vault-token risk is provider-side (Stripe),
not our infra. Stages flip an App Configuration feature filter:

| Stage | Filter | Soak | Hard gates |
|-------|--------|------|------------|
| `internal-tenant` | Microsoft.Targeting (Group=InternalTenant 100%) | 10m | contract + security + runtime-P0 + 5 SPM smoke gates |
| `canary-1` | Microsoft.Percentage 1% | 30m | + post-flip opt-in band advisory (skipped, low volume) |
| `canary-10` | Microsoft.Percentage 10% | 60m | + opt-in band ENFORCED [2%, 40%] + orphan-delete=0 |
| `prod-100` | Microsoft.Percentage 100% | — | full gate suite |

Rollback = single flag flip (<60s). In-flight saves complete (provider call
already in motion at flip time). No traffic shift needed — endpoint exists
regardless of flag, only UI surface gates on it.

## P0 gates (canary-blocking)

| Gate | Source | Asserts |
|------|--------|---------|
| **GATE-SPM-01** | synthetic-spm.sh | Provider vault token (`provider*Token`, `pm_*`, `tok_*`, `src_*`, `re_*`) never appears in any response DTO |
| **GATE-SPM-02** | synthetic-spm.sh | IDOR on `methodId` returns **404 not 403** for cross-tenant + non-existent own-user (parity) |
| **GATE-SPM-03** | synthetic-spm.sh + `_debug/spm/last-delete-audit` | DELETE calls provider revoke **before** local delete; audit event confirms order |
| **GATE-SPM-04** | synthetic-spm.sh | Unique-key constraint enforces single card per user (2nd POST = 409) |
| **GATE-SPM-05** | synthetic-spm.sh DOM probe | Opt-in checkbox `data-testid="spm-opt-in"` present and **not** rendered with `checked=` attribute |

## Post-flip runtime gates

- **Opt-in rate band** (SEC-SPM-004 runtime guard): enforced at canary-10
  and prod-100. Floor 2% (sanity — checkbox is rendering), ceiling 40%
  (anti-dark-pattern — coerced checkbox would spike). Outside band =
  auto-rollback.
- **Orphan-delete SLO** (GATE-SPM-03 runtime version): zero deletes where
  local row removed without provider revoke. Any orphan = rollback.

## Required GitHub secrets (NEW for this bundle)

| Secret | Source |
|--------|--------|
| `PROVIDER_SANDBOX_TOKEN` | Stripe test mode `tok_visa` or equivalent |

Reuses from prior bundles: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
`AZURE_SUBSCRIPTION_ID`, `APP_CONFIG`, `LOG_ANALYTICS_WORKSPACE`,
`SYNTHETIC_SUB_TOKEN`, `OTHER_SUB_TOKEN`.

## Required GitHub Environments (NEW)

`spm-internal-tenant`, `spm-canary-1`, `spm-canary-10`, `spm-prod-100`.
Reviewers: app-dev + security leads (no QA gate at flag flip — QA gates at
PR merge). Each environment can have wait-timer for forced soak.

## Required dev seams (WI-SPM-1 contract)

- `POST /api/payment-methods` returns `{methodId, brand, last4}` — no provider fields
- `GET /api/payment-methods` returns `{methods:[{methodId, brand, last4}]}` — bounded 0..1
- `DELETE /api/payment-methods/{methodId}` → 204 (own), 404 (other or non-existent), idempotent
- Second POST per user → 409 `{error:"single_method_only"}` (GATE-SPM-04)
- Debug endpoint `_debug/spm/last-delete-audit` (synthetic-token guarded, dev/preprod only) returns `{provider_revoke_called: bool, provider_revoke_before_local: bool}`
- Frontend renders `data-testid="spm-opt-in"` checkbox at `/checkout` — default unchecked, no `checked` attribute

## Reuses (zero new infra)

- `.github/workflows/_preprod-security-gate.yml` (preprod-security-gate-wireup bundle)
- `.github/workflows/_runtime-gate.yml` (qa-runtime-gate-wireup bundle)
- `.github/actions/checkout-rollback` composite action

## Apply order

```
WI-SPM-6 (infra: Cosmos payment-methods container + composite index)
   ↓
WI-SPM-1 (vault endpoints) + WI-SPM-5 (security threat model) + WI-SPM-3 (UX spec)  ← parallel
   ↓
WI-SPM-2 (checkout integration: paymentMethodId path)
   ↓
WI-SPM-7 (QA bundle: contract + IDOR + GDPR erasure + revoke-before-delete)
   ↓
WI-SPM-4 (frontend: opt-in checkbox + profile page)
   ↓
WI-SPM-8 — this bundle lands
```

## What this bundle does NOT include

- **Multi-card support.** v1 is 0..1 cards per user (spec § 9 punt). Bundle
  asserts GATE-SPM-04 (2nd POST = 409). v2 will need pagination + display-name.
- **Wallets (Apple Pay / Google Pay).** Not vaulting — wallets are provider
  tokens passed through, never stored. v2 scope.
- **Saved billing address.** Card-only for v1.
- **Default-card selection.** Trivial when only one card exists; v2 problem.
- **GDPR erasure runtime gate.** SEC-SPM-006 is integration-test asserted in
  WI-SPM-7. Runtime canary doesn't have a 30-day clock — proven once at
  contract-gate, trusted thereafter.

If impl pressure suggests bolting any of these on during v1, route to
ideation-research-planning. **The PCI scope guardrail (SAQ-A) is the
load-bearing constraint** — adding any card-data-touching feature flips us
to SAQ-D and triggers a 6-month audit cycle.

## EMU note

`tamirdresher_microsoft` account cannot push, fork, PR-review or merge on
`tamirdresher/travel-assistant`. Maintainer must apply this bundle by
copying the two files into the paths above, adding the secret and
environments, then merging the WI-SPM-8 PR. No squad apply path exists.
