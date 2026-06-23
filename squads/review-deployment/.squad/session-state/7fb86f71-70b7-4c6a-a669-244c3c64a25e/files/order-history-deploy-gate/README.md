# Order History — Deploy Gate Bundle

**Vertical:** Order History ("My Orders") — WI-HIST-1..7 (per `NEXT-VERTICAL-order-history.md` commit `0ed38fe`).

**Why this is separate from `checkout-canary-promote.yml`:** Order History is **read-only, behind the existing `feature.checkout` flag**. Per the IRP spec, no canary required. This is a standard rolling deploy with hard pre-deploy gates and a post-deploy smoke probe + auto-rollback.

## Files

| File | Drop location | Purpose |
|---|---|---|
| `order-history-deploy.yml` | `.github/workflows/order-history-deploy.yml` | Path-scoped deploy pipeline |
| `synthetic-order-history.sh` | `scripts/synthetic-order-history.sh` (`chmod +x`) | Post-deploy smoke probe — 6 gates |

## Gates

| Gate | Type | Blocks deploy? |
|---|---|---|
| **contract-gate** | Pre-deploy: WI-HIST-1 contract tests green on SHA | ✓ |
| **index-precheck** | Pre-deploy: WI-HIST-6 Cosmos composite index `(sub ASC, createdAt DESC)` exists | ✓ |
| **smoke-gate** | Post-deploy: 6 probe gates (envelope, cursor stability, pageSize cap, IDOR isolation, cache headers, p95<500ms) | ✓ (triggers rollback) |
| **rollback** | On smoke fail: traffic shift to prior revision + auto-incident issue | n/a |

## Smoke probe gates (HIST-01..06)

| ID | Asserts | Maps to AC |
|---|---|---|
| HIST-01 | Response envelope `{ items, nextCursor }` | WI-HIST-1 AC #1 |
| HIST-02 | Cursor pagination is idempotent (same cursor → same page) | WI-HIST-1 AC #4 |
| HIST-03 | `pageSize > 50` returns 400 | WI-HIST-1 AC #5 |
| HIST-04 | Zero cross-`sub` ID overlap between two tokens (IDOR-safe) | WI-HIST-1 AC #7 |
| HIST-05 | `Cache-Control: private, max-age≤30` on list endpoint | WI-HIST-1 AC #8 |
| HIST-06 | p95 latency < 500ms over 20 calls (single-partition query) | WI-HIST-1 AC #9 |

## Required GitHub Secrets

Reused from checkout vertical (no new secrets except the probe tokens):

- `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` — OIDC federation
- `ACR_NAME`, `RG_NAME`, `COSMOS_ACCOUNT` — already present
- `PROD_API_BASE` — already present
- `SYNTHETIC_SUB_TOKEN`, `OTHER_SUB_TOKEN` — **already added** for confirmation gate; reused here

## Apply order

This bundle applies **after** WI-HIST-6 (Cosmos index) and WI-HIST-1 (endpoint) land on `main`. Both are non-blocking for current checkout vertical.

1. Cherry-pick this bundle's two files into a new PR on `tamirdresher/travel-assistant`.
2. Path: copy `order-history-deploy.yml` → `.github/workflows/`, copy `synthetic-order-history.sh` → `scripts/` (`chmod +x`).
3. Merge — workflow self-activates on the next push touching the scoped paths.

## EMU note

Squad account cannot push to `tamirdresher/travel-assistant`. Maintainer applies; everything in this bundle is git-cherry-pickable as a single commit.

— review-deployment-squad
