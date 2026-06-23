# WI-REFUND-1b — Error Envelope Alignment

**Closes:** experience-design UX spec wire-format gap (commit `4c84355`).
**Stack position:** Independent from locked 10-bundle checkout stack. Applies AFTER `wi-refund-1-backend/` (commit `5d9b891`).
**Files:** 1 new (`RefundErrorEnvelope.cs`), 1 patched (`RefundsEndpoints.cs` — 10 surgical replacements documented inline).

## Why this exists

WI-REFUND-1 backend shipped before UX spec finalized. Spec §5.2 froze wire format as `{ "error": { "code": "REFUND_*", "message": "..." } }`. My v1 shipped flat lowercase: `{ "error": "refund_already_exists" }`. Frontend `RefundModal.tsx` (experience-design bundle `b575453`) parses `error.code` — would break on every error path.

This patch is **pure wire-format alignment**. No behavioral change. Same status codes, same eligibility precedence, same idempotency caching, same webhook handler (already matches spec — `PROVIDER_DECLINED|TIMEOUT|UNAVAILABLE|INSUFFICIENT_PROVIDER_FUNDS`).

## What changed

| Surface | v1 (flat lowercase) | v1b (spec-frozen) |
|---|---|---|
| Envelope | `{ "error": "code" }` | `{ "error": { "code": "CODE", "message": "..." } }` |
| Already-exists | `error = "refund_already_exists"` | `error.code = "REFUND_ALREADY_EXISTS"` + sibling `refundId`/`status` |
| Ineligibility | `error = "order_not_refundable", reason: "window_expired"` | `error.code = "REFUND_INELIGIBLE_WINDOW_EXPIRED"` |
| Body mismatch (422) | `Problem(title: "...")` | `error.code = "IDEMPOTENCY_BODY_MISMATCH"` |
| In-flight (409) | `Problem(title: "...")` | `error.code = "REQUEST_IN_FLIGHT"` |
| Rate cap (429) | bare status | `error.code = "RATE_LIMITED"` |
| Idem key missing (400) | `Problem(title: "...")` | `error.code = "IDEMPOTENCY_KEY_REQUIRED"` |
| Malformed JSON (400) | `Problem(title: "...")` | `error.code = "MALFORMED_JSON"` |
| orderId missing (400) | `Problem(title: "...")` | `error.code = "ORDER_ID_REQUIRED"` |
| 404 IDOR | bare `NotFound()` | `error.code = "ORDER_NOT_FOUND"` |

## What didn't change

- **Status codes** — every code maps 1:1 to its v1 status. No frontend status-code logic breaks.
- **Idempotency caching** — replays return byte-for-byte identical bodies (now in new shape). Stale lowercase entries TTL out within 15min `ConfirmTtl`; no migration step. PutAsync still receives `(cacheKey, canonical, status, body, ttl, ct)` unchanged.
- **Webhook handler** — `RefundWebhookHandler.cs` failure-code allowlist already matches spec exactly. Untouched.
- **GET /api/orders/{id}** — `eligibleActions` array is success-path; no envelope change. `Results.Ok(dto)` unchanged.
- **Success bodies** — `RefundResponse` record (200/202) unchanged. `failed` outcome at 200 unchanged (already a happy-path cached body).
- **Eligibility precedence** — `canceled > already_refunded > not_confirmed > window_expired` preserved in `ComputeIneligibility`. `RefundError.Ineligible()` just translates the reason string to the spec code.

## Apply path

1. Drop `RefundErrorEnvelope.cs` next to `RefundsEndpoints.cs` (same `TravelAssistant.Refunds` namespace).
2. Apply the 10 search/replace edits documented inline in the comment block at the bottom of `RefundErrorEnvelope.cs`. Each block has the v1 line as `OLD:` and the v1b line as `NEW:`. Order doesn't matter — they're independent.
3. No DI registration changes. No NuGet adds. No config changes.

## Test impact

QA's `refunds-v1-test-plan` bundle (14 integration + 6 P0 gates) — assertions on error responses need a one-line update to read `body.error.code` instead of `body.error`. Apply once per test file:

```csharp
// v1: Assert.Equal("refund_already_exists", body.GetProperty("error").GetString());
// v1b: Assert.Equal("REFUND_ALREADY_EXISTS", body.GetProperty("error").GetProperty("code").GetString());
```

QA's post-deploy smoke (`refunds-post-deploy-smoke.spec.ts` — Hockney's 5-test contract gate):
- **SMOKE-1** (4-value reason enum on 409) — assertion target moves from `body.reason` to `body.error.code` and expects `REFUND_INELIGIBLE_{REASON}` form. **Flag for QA.**
- **SMOKE-2** (provider-id leak guard on 2xx) — unchanged, success path untouched.
- **SMOKE-3** (Redis idempotency replay byte-for-byte) — unchanged, cached body shape just changes once, then replay is deterministic.
- **SMOKE-4, SMOKE-5** — unchanged.

## What this does NOT close

- **DR-REFUNDS-001 (drift detector on enum)** — review-deployment-squad's contract gate should now expect `REFUND_INELIGIBLE_*` codes on 409, not raw reason strings. Their `2a74ab9` config needs a one-line update. **Notifying review-deployment-squad.**

## Refunds vertical app-dev queue: empty

WI-REFUND-1 (v1 + v1b) and WI-REFUND-5 (webhook handler) shipped. WI-REFUND-2 frontend impl is experience-design's WI-REFUND-7 bundle (`b575453`). WI-REFUND-3 UX spec is experience-design's `4c84355`. WI-REFUND-4 QA is in QA's bundle. WI-REFUND-6 contract tests and WI-REFUND-7 monitoring belong to QA + review-deployment respectively.

Apply order: independent from 10-bundle checkout stack, ships parallel after `wi-refund-1-backend/`.
