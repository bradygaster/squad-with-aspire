# Checkout Canary Promote — Operator Guide

Companion workflow to `checkout-cicd.yml`. The CI workflow builds + tests + deploys at **0% traffic**. This workflow walks the new revision through **0% → 1% → 10% → 50% → 100%** with manual approval at every stage and automatic rollback on gate failure.

## Files in this bundle

| File | Where it lands in the repo |
|------|----------------------------|
| `checkout-canary-promote.yml` | `.github/workflows/checkout-canary-promote.yml` |
| `checkout-rollback-action.yml` | `.github/actions/checkout-rollback/action.yml` (rename to `action.yml`) |

## Required repo configuration (one-time, maintainer)

### 1. GitHub Environments (Settings → Environments)

| Environment | Approvers | Wait timer |
|-------------|-----------|------------|
| `checkout-canary-dark` | none (auto) | 0 |
| `checkout-canary-1pct` | 1 from `@review-deployment` AND 1 from `@app-dev` | 0 |
| `checkout-canary-10pct` | 1 reviewer | 15 min |
| `checkout-canary-50pct` | 1 reviewer | 30 min |
| `checkout-canary-100pct` | 2 reviewers (release captain + maintainer) | 0 |

### 2. Required secrets

Already declared in `checkout-cicd.yml`. This workflow additionally needs:

- `LOG_ANALYTICS_WORKSPACE` — workspace ID for KQL canary metrics

### 3. Stable image tag convention

After every successful 100% promotion the workflow tags the image as `checkout-api:stable`. The rollback action looks for `checkout-api--stable` revision OR the revision currently holding 100% traffic before the canary started.

## Operator runbook

1. **Build green.** Confirm `checkout-cicd.yml` on `main` finished successfully and stored the image tag.
2. **Open Actions → `checkout-canary-promote` → Run workflow.**
   - `image_tag`: full image ref (e.g. `myregistry.azurecr.io/checkout-api:sha-abc1234`)
   - `revision_suffix`: short label (e.g. `r-2026-06-23-a`) — used as both ACA revision suffix AND traffic-routing label
   - `skip_dark`: leave unchecked unless the image is already deployed at 0%
3. **Watch Stage 0 (Dark).** Smoke probe + synthetic checkout against revision label FQDN. ~3 min.
4. **Stage 1 (1%) requires manual approval.** Before approving, verify in the run logs:
   - ✅ BUG-1 idempotency contract test green for this SHA
   - ✅ BUG-2 failed-replay status code test green for this SHA
   - Both are HARD GATES — workflow exits 1 if either is missing or failing.
5. **Stage 1 soaks 10 min, checks error rate.** >1% 5xx trips auto-rollback.
6. **Stage 2 (10%) waits 15 min then runs.** Checks p95 latency <800ms.
7. **Stage 3 (50%) waits 30 min then runs.** 60 min soak, error budget check.
8. **Stage 4 (100%) requires 2 approvers.** Promotes, tags `:stable`, retains old revision 24h.

## Rollback behavior

Every canary stage has `if: failure()` → invokes `./.github/actions/checkout-rollback`:

1. Flips `checkout:enabled=false` in Azure App Configuration (kill switch)
2. Shifts 100% traffic back to the previous stable revision
3. Deactivates the bad revision
4. Emits a `::notice::` for the on-call channel to pick up

The kill switch ensures even in-flight requests start returning `503 Service Unavailable` with a retry-after header until an operator manually re-enables it post-RCA.

## Gate matrix (matches `checkout-canary-runbook.md` from ideation-research-planning-squad)

| Stage | Gate | Source of truth |
|-------|------|-----------------|
| Dark | `/health/ready` 200 + synthetic checkout green | `synthetic-checkout.sh` |
| 1% | BUG-1 + BUG-2 contract checks green AND 5xx <1% | GH check-runs API + Log Analytics |
| 10% | p95 latency <800ms | Azure Monitor metrics |
| 50% | Error budget held | Log Analytics |
| 100% | Two-person approval | GitHub Environments |

## What this does NOT do

- Database migrations (handled by `checkout-cicd.yml` build stage on `main`)
- Front Door cache invalidation (not needed — checkout responses are non-cacheable)
- Pinning service-bus consumer to the new revision (idempotency primitives are forward+backward compatible; no consumer freeze needed)

## Apply order (maintainer-apply)

This workflow is **independent** of the 6-bundle checkout app-dev stack. It can be merged at any time before promotion. Recommended order:

1. Land app-dev stack: `hotfix-p0` → `wi1c-redis-testauth` → `wi4-wi5` → `wi1a-nfc-amendment` → `wi6-redis-di-reconcile` → `webhook-debug-endpoint`
2. Land security v2 primitives bundle (security-hardening-squad)
3. Land infra Bicep PR #44 (with 3 must-fix amendments from prior review)
4. Land QA test bundle (un-skip all Skip'd tests)
5. Land `checkout-cicd.yml` from `squads/review-deployment/artifacts/checkout-cicd/`
6. **Land this bundle**
7. Run `checkout-cicd` on `main` → produces image tag
8. Manually trigger `checkout-canary-promote` with that tag → walks to 100%

— review-deployment-squad
