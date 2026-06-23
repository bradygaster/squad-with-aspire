# Refunds v1b Error Envelope Alignment

**Triggered by:** app-dev commit `32b9384` (WI-REFUND-1b — error envelope freeze per UX spec `4c84355`).

**What broke:** My `synthetic-refund.sh` GATE-RFD-03 and GATE-RFD-04 assert against the old envelope:
```
{ "error": "order_not_refundable", "reason": "window_expired" }
```
v1b ships:
```
{ "error": { "code": "REFUND_INELIGIBLE_WINDOW_EXPIRED", "message": "..." } }
```

**This patch:** 4-line change to `synthetic-refund.sh` — reads `.error.code` instead of `.error.reason`, asserts uppercase `REFUND_INELIGIBLE_*` constants instead of lowercase reason strings.

## Files

- `synthetic-refund.patch` — unified diff against `refunds-deploy-gate/synthetic-refund.sh`

## Apply order

1. `wi-refund-1-backend/` (commit `5d9b891`) — original handler
2. `wi-refund-1b-error-envelope/` (commit `32b9384`) — envelope wrap
3. `refunds-deploy-gate/` (commit `2a74ab9`) — original gate
4. **This patch** — surgical update to probe assertions
5. Then `refunds-canary-promote.yml` dispatch can proceed

## Maintainer apply

```bash
cd <repo-root>
patch -p1 < squads/review-deployment/artifacts/refunds-v1b-envelope-align/synthetic-refund.patch
```

Or hand-edit `synthetic-refund.sh` lines 62–74 per the diff.

## What does NOT change

- **`refunds-canary-promote.yml`** workflow YAML — unchanged
- **`post-deploy-smoke-wire/workflow-snippet.yml`** — unchanged (QA owns SMOKE-1 test internals; Hockney was notified directly by app-dev)
- **`refunds-frontend-gate`** — unchanged (token grep regex untouched)
- **`_preprod-security-gate.yml`** / **`_runtime-gate.yml`** — unchanged
- **`checkout-rollback` composite** — unchanged
- All gate counts, soak windows, error-rate thresholds — unchanged
- Other GATE-RFD-* probes (01, 02, 05, 06, IDOR, Cache-Control, p95, ETag) — unchanged

## Note on 422 idempotency-mismatch

App-dev's v1b also adds `IDEMPOTENCY_*` codes for the 422 path. My probe does not currently assert 422 envelope shape — only status code. No change needed. If future drift detection wants 422 body shape, that's a one-line add to QA's SMOKE suite, not this probe.

## Refunds gate stack (post-patch)

| # | Gate | Source bundle | v1b status |
|---|------|---------------|------------|
| 1 | Preprod security (8 P0) | `preprod-security-gate-wireup` | unaffected |
| 2 | Frontend (token grep + 15 PW + 4 axe + SR) | `refunds-frontend-gate` | unaffected |
| 3 | Runtime P0 (filter Tier=P0) | `qa-runtime-gate-wireup` | unaffected |
| 4 | Runtime P1 (advisory) | `qa-runtime-gate-wireup` | unaffected |
| 5 | Synthetic reachability (11 probes) | `refunds-deploy-gate` | **patched** (this bundle) |
| 6 | Post-deploy contract smoke (5 tests) | `refunds-post-deploy-smoke-wire` | QA-side internal update (notified) |

— review-deployment-squad
