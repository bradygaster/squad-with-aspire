# Refunds v1 Canary Deploy Gate

**Bundle:** `refunds-canary-promote.yml` + `synthetic-refund.sh`
**Vertical:** Refunds v1 (POST /api/refunds, GET /api/refunds/{id}, Stripe webhook)
**Type:** Full canary (write path, money movement) — NOT rolling deploy

## Why canary, not rolling

Refunds move money. Even at low volume, a buggy revision serving 100% of traffic for 30 seconds can issue
duplicate refunds, leak provider IDs, or process out-of-window orders. Same risk class as checkout → same
ceremony, with refund-specific gates and metric thresholds.

## Stage shape

| Stage | Traffic | Soak | Why |
|-------|---------|------|-----|
| dark | 0% | 3min | Direct revision URL probe; ingress sees nothing |
| canary-1 | 1% | 10min | Real users, blast radius ~1 refund/hr at current volume |
| canary-10 | 10% | 30min | + webhook replay storm (refund-storm.js, dedup>99.9%) |
| prod-100 | 100% | — | Skip 50% — refund volume too low for meaningful intermediate signal |

**Tighter than checkout:** error-rate threshold 0.5% (vs checkout 1%) because every error is potential money loss. P95 latency 1500ms (vs checkout 800ms) because Stripe call dominates round-trip.

## Stacked gates (all must pass at each stage)

1. **contract-gate** — QA's `RefundIntegrationTests` + `RefundsPreprodGateTests` (6 GATE-RFD-01..06) + `RefundWindowTests` (window anchor = `confirmedAt`) green on image SHA via GH check-runs API
2. **infra-precheck** — WI-REFUND-6 deliverables present: refunds container with `/idempotencyKey` unique key, `stripe-refund-webhook-secret` in Key Vault, `feature.refunds_v1_enabled` flag in App Config
3. **security-gate** — Reuses existing `_preprod-security-gate.yml` (P0 fail-closed, 5 checks)
4. **runtime-gate-p0** — Reuses existing `_runtime-gate.yml` with Tier=P0, re-runs at every stage boundary
5. **synthetic-refund.sh** — 11 black-box probes mapping to GATE-RFD-01..06 + UX contracts:
   - Happy path → 202 + state=pending
   - Provider `re_xxx` NOT serialized (GATE-RFD-06 / SEC-RFD-001)
   - Idempotency replay → same refundId
   - Window expired → 409 + `reason: "window_expired"`
   - Canceled order → 409 + `reason: "canceled"`
   - IDOR cross-sub → 404 (NOT 403 — prevents enumeration)
   - GET cache: `Cache-Control: private, max-age≤30`
   - p95 < 1500ms over 20 GETs
6. **runtime-gate-p1-advisory** — At 100% stage, `continue-on-error: true`, auto-opens `gate:p1-slippage,sla:7d` issue on failure

## Rollback

Reuses existing `checkout-rollback` composite action:
- Flips App Config `feature.refunds_v1_enabled` → false (instant UI button hide; ~30s App Config TTL)
- Shifts ACA traffic 100% back to prior stable revision
- Deactivates bad revision (preserves logs for forensics, frees compute)
- Auto-opens `incident:refunds-rollback` issue with stage + revision + error excerpt

Triggered automatically on any gate failure in any promote-* stage.

## Required secrets / vars (delta from checkout vertical)

**New repo secrets:**
- `STRIPE_WEBHOOK_TEST_SECRET` — for refund-storm.js HMAC signing
- `SEEDED_ORDER_REFUND_WINDOW_EXPIRED` — synthetic order >24h past confirmedAt
- `SEEDED_ORDER_CANCELED` — synthetic order in Canceled state

**Reused from existing setup:**
- `SYNTHETIC_SUB_TOKEN`, `OTHER_SUB_TOKEN`, `SEEDED_ORDER_CONFIRMED` (from confirmation gate)
- `AZURE_CLIENT_ID/TENANT_ID/SUBSCRIPTION_ID`, `LOG_ANALYTICS_WORKSPACE` (from checkout vertical)

**New repo vars:**
- `COSMOS_ACCOUNT`, `KV_NAME` — for infra precheck
- (`ACR_NAME`, `APP_CONFIG`, `PUBLIC_DOMAIN`, `ACA_DOMAIN`, `ACA_RG` reused from checkout)

**New GH Environments** (each requires named-reviewer approval):
- `refunds-dark`, `refunds-canary-1`, `refunds-canary-10`, `refunds-prod-100`

Recommended reviewers per stage:
- dark / 1: any one of {app-dev lead, qa lead}
- 10: + security lead (storm runs here)
- 100: + infra lead + security lead (money-movement final flip)

## Apply order (maintainer)

This bundle ships **after** the refunds vertical reaches code-complete. Sequence on `tamir/squad-fixes`:

1. **WI-REFUND-6 infra** (Bicep: refunds container, Stripe secrets, App Config flag) → deploy to preprod
2. **WI-REFUND-1 app-dev** (POST /api/refunds + 6 dev seams) → image builds, contract tests run
3. **QA bundle apply** (un-skip RefundIntegrationTests + GATE-RFD tests against seams)
4. **WI-REFUND-2/3/5** (status endpoint, frontend UX, telemetry) parallel
5. **WI-REFUND-4 webhook** (Stripe handler with HMAC verify + replay dedup)
6. **WI-REFUND-7** (data model finalize) merged
7. **THIS BUNDLE applies:**
   - `cp refunds-canary-promote.yml .github/workflows/`
   - `cp synthetic-refund.sh scripts/`
   - `chmod +x scripts/synthetic-refund.sh`
   - Set new secrets/vars/environments listed above
   - Push tag `refunds-v1-canary-ready` → unlocks `workflow_dispatch` from main

## What this does NOT do (out of scope, explicit)

Per IRP spec § 9 scope guardrail — these are v2, NOT to be added:
- Partial refunds, multi-item refunds, refund reason capture
- Refund history page (use order history vertical's existing list)
- Goodwill credit issuance, store credit refunds
- Refund-to-different-payment-method (returns to original card only)

Per coordination with experience-design: feature flag gates **UI button visibility only**.
Server endpoint always exists (per security review preference) — kill-switch flips UI not API surface.

## Operator quick reference

**Trigger promotion:**
```bash
gh workflow run refunds-canary-promote.yml \
  --repo tamirdresher/travel-assistant \
  -f image_sha=abc123def456
```

**Emergency rollback (skip waiting for next gate failure):**
```bash
gh workflow run checkout-rollback.yml \
  -f app=refunds-api \
  -f bad_revision=<rev-name> \
  -f kill_switch_flag=feature.refunds_v1_enabled
```

**Skip webhook storm (emergency only, requires security sign-off recorded in deploy ticket):**
```bash
gh workflow run refunds-canary-promote.yml \
  -f image_sha=abc123def456 \
  -f skip_storm=true
```

## Review-deployment squad sign-off

Refunds vertical now has the same 5-gate stack as checkout vertical (contract+infra → security → runtime-P0 → synthetic+storm → runtime-P1 advisory) with refund-specific thresholds (0.5% errors / 1500ms p95 / 99.9% webhook dedup) and the SEC-RFD-001 provider-ID non-exposure assertion baked into the synthetic probe.

Bundle is self-contained: no edits required to existing checkout workflows. Drop-in apply when refunds vertical hits code-complete.
