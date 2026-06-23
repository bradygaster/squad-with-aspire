# waf-fake-payment-provider

Closes QA's PR #53 step 2: register the fake payment provider with QA's `TestPaymentTokens` contract in `WebApplicationFactory<Program>` so the un-Skip'd `BUG1_*` / `BUG2_*` integration tests (and the new `FailedPaymentReplayPreservesStatusCodeTests` when promoted to integration) run green.

## Files

| File | Drop location | Purpose |
|---|---|---|
| `FakePaymentProvider.cs` | `tests/TravelAssistant.Api.Tests/Checkout/Fakes/` | `IPaymentProvider` impl that maps QA's tokens to deterministic outcomes |
| `CheckoutWebApplicationFactory.cs` | `tests/TravelAssistant.Api.Tests/Checkout/` | `WebApplicationFactory<Program>` w/ fake provider + test-auth env-gate + in-memory idempotency |

~70 LOC total (QA quoted ~30; the extra ~40 is the env-gate + in-memory config block, which QA's spec implies but doesn't ship).

## Token contract (matches `TestPaymentTokens.cs` in QA's `wi-1a-followup` bundle)

| Token | HTTP outcome | Cached in IdempotencyStore? |
|---|---|---|
| `tok_visa_ok` | 200 OK | ✅ (terminal success) |
| `tok_chargeDeclined` | 402 Payment Required | ✅ (terminal failure — preserved on replay) |
| `tok_declined` | 402 Payment Required | ✅ (declined alias, same semantics) |
| `tok_fraud_reject` | 403 Forbidden | ✅ (terminal failure) |
| `tok_gateway_timeout` | 504 (via thrown `PaymentGatewayTimeoutException`) | ❌ (non-terminal — retry gets fresh attempt) |
| `tok_invalid` | 400 (via thrown `PaymentValidationException`) | ❌ (validation, not payment outcome) |
| _any other_ | 200 OK with synthetic `pi_test_{orderId}` | ✅ |

Status preservation contract: cached outcomes ride through `IdempotencyStore.v2.Hit()` → `Replay(statusCode, body)` per WI-2. Non-terminal throws bubble up before the cache write, so retries with the same `Idempotency-Key + body` get re-attempted (BUG-2 endpoint-layer contract).

## Two `IPaymentProvider` types referenced

`PaymentResult.Succeeded(...)` / `PaymentResult.Declined(...)` and the two exception types (`PaymentGatewayTimeoutException`, `PaymentValidationException`) are app-dev's existing surface. If they aren't yet on `main`, they ship as part of the hotfix-p0 bundle's payment-provider seam — same interface that's already wired into `CheckoutEndpoints.confirm.v2.cs`. No new types invented here.

## Apply order

Lands as a **test-only** patch on `fix/checkout-idempotency-p0` AFTER the full 7-bundle maintainer-apply stack is in:

```
1. hotfix-p0/
2. wi1c-redis-testauth/
3. wi4-wi5/
4. wi1a-nfc-amendment/
5. azure-infra/sec-infra-redis-idempotency
6. wi6-redis-di-reconcile/
7. webhook-debug-endpoint/
8. waf-fake-payment-provider/    ← this bundle (test project only, no production change)
+ QA's wi-1a-followup/           ← TestPaymentTokens.cs + FailedPaymentReplayPreservesStatusCodeTests.cs
+ QA's owed-tests/               ← TimingOracleStatisticalTests.cs + UnicodeNormalizationTests.cs
```

## What QA un-skips after this lands

On `qa/checkout-bug-regression-tests` (PR #52) or `fix/checkout-idempotency-p0` (PR #53):

- `IdempotencyRegressionTests.BUG1_*` (2 tests) — body-mismatch returns 422
- `IdempotencyRegressionTests.BUG2_*` (2 tests) — failed-payment replay preserves 402/403
- `UnicodeNormalizationTests` (6 tests) — un-skip after step 4 lands
- `FailedPaymentReplayPreservesStatusCodeTests` (5 tests) — already runs live, no un-skip needed

Total live coverage after apply: **40 idempotency-vertical tests** (14 unit + 8 integration + 4 regression-unskip + 5 contract + 3 timing-statistical + 6 NFC-unskip).

## Why singleton, not scoped

`FakePaymentProvider` has no per-request state. Singleton matches the real `StripePaymentProvider` lifetime (transitive HTTP client is the only thing scoped/pooled, and the fake has none). Keeps factory startup cheap for the integration sweep.

— Bennett, application-development-squad
