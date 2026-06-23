# Refunds v1 + v1b — Canonical Apply-Order Manifest

**Owner:** application-development-squad
**Branch:** `tamir/squad-fixes`
**Status:** Ready for maintainer apply (gh issue create blocked by EMU; this file is the canonical source).
**Companion:** QA's `ISSUE-refunds-v1b-apply-order.md` — content equivalent, this version is committed to repo for diffability.

## Atomic Constraint

> ⚠️ **Steps 2 and 3 in the apply order MUST land in the SAME PR.**
> The v1 flat error envelope (`{error:"refund_already_exists", reason:"..."}`) must never be exposed in any deployed or tested state. The frontend (`RefundModal.tsx b575453`) parses `error.code` from the nested envelope per UX freeze `4c84355` — flat shape would break every 4xx.

## Apply Order (7 steps)

| # | Bundle / Change | Owner | Commit / Path |
|---|----------------|-------|---------------|
| 1 | **wi-refund-1-backend** — RefundsEndpoints + RefundWebhookHandler + InMemoryRefundsRepository + WafRefundsSeams | app-dev | `5d9b891` · `squads/application-development/artifacts/refunds/wi-refund-1-backend/` |
| 2 | **wi-refund-1b-error-envelope** — RefundErrorEnvelope.cs + 10 surgical edits | app-dev | `squads/application-development/artifacts/refunds/wi-refund-1b-error-envelope/` |
| 3 | **refunds-v1-test-plan delta** — 14 integration + 6 P0 gate tests updated to `body.error.code` | QA | `squads/quality-testing/artifacts/refunds/refunds-v1-test-plan/` |
| 4 | **refunds-spec-amendment-cc08d34** — 6 `Skip`'d RefundReasonEnumTests rewritten to nested envelope, then un-Skip | QA | `squads/quality-testing/artifacts/refunds/refunds-spec-amendment-cc08d34/` |
| 5 | **DR-REFUNDS-001 drift detector** — assertions on `body.error.code` for 409 ineligibility | review-deployment | `squads/review-deployment/artifacts/refunds/DR-REFUNDS-001/` |
| 6 | **refunds-post-deploy-smoke.spec.ts** — SMOKE-1 target `body.error.code` | QA | `squads/quality-testing/artifacts/refunds/post-deploy-smoke/` |
| 7 | **refunds-deploy-gate.yml** — assertion-level updates only (no workflow structure change) | review-deployment | `.github/workflows/refunds-deploy-gate.yml` |

Steps 1+2 are app-dev code. Steps 3+4+6 are QA tests. Steps 5+7 are review-deployment guards. All three squads ship within the atomic PR if and only if step 2 ships.

## Frozen Error Code Set (v1b)

Defined in `RefundErrorEnvelope.Codes` (single source of truth — never duplicated as literals in tests or smoke):

```
REFUND_ALREADY_EXISTS
REFUND_INELIGIBLE_CANCELED
REFUND_INELIGIBLE_ALREADY_REFUNDED
REFUND_INELIGIBLE_NOT_CONFIRMED
REFUND_INELIGIBLE_WINDOW_EXPIRED
IDEMPOTENCY_KEY_REQUIRED
IDEMPOTENCY_BODY_MISMATCH
REQUEST_IN_FLIGHT
RATE_LIMITED
MALFORMED_JSON
ORDER_ID_REQUIRED
UNAUTHORIZED
ORDER_NOT_FOUND
```

## Wire Format

```json
// 4xx (nested)
{ "error": { "code": "REFUND_INELIGIBLE_CANCELED", "message": "..." } }

// REFUND_ALREADY_EXISTS adds sibling fields (preserves frontend auto-transition-to-pending at 3s)
{ "error": { "code": "REFUND_ALREADY_EXISTS", "message": "..." }, "refundId": "01H...", "status": "pending" }

// 200/202 success bodies — UNTOUCHED
// GET /api/orders/{id} eligibleActions response — UNTOUCHED
```

## Invariants Preserved

| Invariant | Source | Status |
|-----------|--------|--------|
| Eligibility precedence: `canceled > already_refunded > not_confirmed > window_expired` (most-specific-wins) | DR-REFUND-001 | ✅ preserved |
| Pending + PaymentFailed both map to `NOT_CONFIRMED` | spec-amendment-cc08d34 | ✅ preserved |
| 24h window anchored at `order.ConfirmedAt` | DR-REFUND-001 R1 (cc08d34) | ✅ preserved |
| Idempotency cache shape unchanged (replay byte-for-byte; stale lowercase entries TTL out in 15min) | wi-refund-1b notes | ✅ preserved (no migration needed) |
| Provider ID leak guard — `Envelope()` never serializes `providerRefundId` (re_xxx) | SEC-RFD-001 | ✅ preserved |
| Status codes 1:1 with v1 (202 pending, 200 sync-decline, 400/404/409/422/429 unchanged) | wi-refund-1 | ✅ preserved |
| Webhook failure-code allowlist untouched (PROVIDER_DECLINED, PROVIDER_TIMEOUT, PROVIDER_UNAVAILABLE, INSUFFICIENT_PROVIDER_FUNDS + `refund.failure_reason_unmapped` telemetry on miss) | wi-refund-1 RefundWebhookHandler | ✅ preserved |

## Post-Deploy v1-Regression Sentinels

1. **DR-REFUNDS-001 drift detector** — blocks any response shape regressing to flat `{error:"..."}` on 4xx.
2. **SMOKE-1** — fails build if `body.error` is a string (not object) on any 4xx.
3. **Grep gate** — no lowercase reason strings (`canceled`, `already_refunded`, `not_confirmed`, `window_expired`) appear at top-level response paths.

## Ownership Boundary (clean cut)

- **app-dev** owns `RefundErrorEnvelope.Codes` constants. Any new code MUST be added there first.
- **QA** consumes `Codes.*` directly in test fixtures — no string-literal duplication permitted.
- **review-deployment** owns drift-detector assertions on the deployed HTTP surface.

## Out-of-Scope (route to planning for v2)

- Partial refunds
- Multi-item refunds
- Reason capture in request body
- Goodwill refunds (outside provider settlement)
- Refund history page

## Maintainer Apply Command

When ready, maintainer creates a single PR containing all 7 changes, with this manifest as the PR description body:

```bash
gh pr create \
  --repo tamirdresher/travel-assistant \
  --base main \
  --head tamir/squad-fixes \
  --title "Refunds v1 + v1b: atomic apply (nested error envelope alignment)" \
  --body-file squads/application-development/artifacts/refunds/APPLY-ORDER/README.md
```

---

**Cross-references**

- App-dev v1 backend: knowledge entry `[2026-06-24 00:42]`
- App-dev v1b envelope: knowledge entry `[2026-06-24 00:55]`
- QA test-delta closeout: knowledge entry `[2026-06-24 01:00]`
- UX spec freeze: commit `4c84355`
- Frontend parser: `RefundModal.tsx` commit `b575453`
- Planning resolutions (cc08d34): DR-REFUND-001 R1-R4
