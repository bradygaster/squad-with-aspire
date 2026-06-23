# Confirmation Endpoint Canary Gate

Adds WI-CONFIRM-1 (`GET /api/checkout/orders/{orderId}/status`) to the existing
checkout canary promotion workflow as **Gate 11** alongside the 10 existing
gates from the checkout vertical.

## Why a new gate

The confirmation page ships behind the same checkout feature flag and reuses
the checkout canary harness — but the status endpoint has its own contract
that can fail independently:

| Contract surface | Failure mode if untested in canary |
|---|---|
| **IDOR (404 not 403)** | Leak existence of other-sub orders → security regression |
| **ETag stability** | Frontend 2s poll busts cache every tick → 30× backend QPS |
| **Cache-Control: private, max-age=2** | CDN caches private order data → cross-user leak |
| **5 order states** | Frontend stuck on `pending` skeleton forever |
| **p95 latency** | 2s poll × N concurrent users overwhelms Cosmos RU budget |

The existing 10-gate matrix covers checkout submit/replay/idempotency but not
read-side polling semantics. This is additive — no existing gate is changed.

## Files

| File | Drop location | Purpose |
|---|---|---|
| `synthetic-order-status.sh` | `.github/scripts/synthetic-order-status.sh` (chmod +x) | Probe script — 6 gates, exit codes 1–5 per failure class |
| `workflow-snippet.yml` | merge into `.github/workflows/checkout-canary-promote.yml` as a new step in each stage job | Adds the gate to dark/1%/10%/50%/100% stages |
| `seed-orders.bicep` | optional — append to existing infra Bicep | Seeds the 5 fixed orderIds for the synthetic sub in the canary env |

## Required secrets (additions)

| Secret | Purpose | Source |
|---|---|---|
| `SYNTHETIC_SUB` | sub claim of synthetic principal — must own the seeded orders | App Config (already whitelisted from checkout canary) |
| `OTHER_SUB_TOKEN` | bearer for a DIFFERENT synthetic sub — used for IDOR check | New synthetic principal in canary tenant only |
| `SEEDED_ORDER_PENDING` | orderId | Bicep output from seed-orders.bicep |
| `SEEDED_ORDER_CONFIRMED` | orderId | Bicep output |
| `SEEDED_ORDER_PAYMENT_FAILED` | orderId | Bicep output |
| `SEEDED_ORDER_INVENTORY_RELEASED` | orderId | Bicep output |
| `SEEDED_ORDER_CANCELED` | orderId | Bicep output |

`BASE_URL` and `SYNTHETIC_TEST_TOKEN` already exist from the checkout canary
secret bundle — no rotation needed.

## Gate behavior

Runs **after** the existing checkout synthetic probe in each stage. If the
status-endpoint gate fails, the existing `checkout-rollback` composite action
fires — no new rollback wiring required. Exit codes 1–5 differentiate failure
class in the workflow logs for triage.

## Order of merge

This bundle ships **after** WI-CONFIRM-1 (app-dev endpoint) is on `main` and
**before** the next canary push touches the confirmation flag. It is **NOT**
required for the current checkout vertical to flip — checkout-cicd.yml +
checkout-canary-promote.yml already cover today's scope.

## Out of scope (next vertical, not this bundle)

- Frontend Lighthouse a11y CI step (WI-CONFIRM-3) — owned by experience-design
- NVDA/VoiceOver manual scripts (WI-CONFIRM-2) — owned by QA, not CI-automatable
- Cosmos RU pressure test under 2s-poll storm — separate load test, post-launch

## Apply

```bash
cp synthetic-order-status.sh .github/scripts/
chmod +x .github/scripts/synthetic-order-status.sh
# Merge workflow-snippet.yml into .github/workflows/checkout-canary-promote.yml
# Add the 7 new secrets to the repo + canary GH Environment
```

— review-deployment-squad
