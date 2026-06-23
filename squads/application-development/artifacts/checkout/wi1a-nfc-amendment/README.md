# WI-1a NFC Amendment — Re-baseline Note

**Date:** 2026-06-23
**Author:** Bennett (application-development-squad)
**Closes:** R3 (Unicode bypass) gap inside the primitives, not at callers.

## What changed (upstream)

Security-hardening shipped v2 of three files in
`squads/security-hardening/artifacts/checkout-sec-reference/`:

| File | Delta |
|---|---|
| `JsonCanonicalizer.cs` | `WriteString` + `WriteObject` now NFC-normalize strings and member names before sort/serialize |
| `IdempotencyKeyDerivation.cs` | `BuildScope` NFC-normalizes sub-claim and guestSessionId; `DeriveCacheKey` defensively re-normalizes inputs |
| `UnicodeNormalizationContractTests.cs` (new) | 7 unit-level guards inside the bundle |

The v1 → v2 delta is **additive (stricter, not different behavior)** —
anything correct against v1 stays correct against v2.

## App-dev side: zero code changes required

The wiring shipped in
`squads/application-development/artifacts/checkout/wi1c-redis-testauth/src/CheckoutEndpoints.confirm.v2.cs`
and `hotfix-p0/src/IdempotencyStore.v2.cs` already calls these helpers
without ever inspecting their internals:

```csharp
var scope    = IdempotencyKeyDerivation.BuildScope(httpContext.User, guestSessionId);
var cacheKey = IdempotencyKeyDerivation.DeriveCacheKey(scope, idempotencyKeyHeader);
var bodyHash = IdempotencyKeyDerivation.HashBody(
                  JsonCanonicalizer.CanonicalizeUtf8(rawBody));
```

Because NFC moved *inside* `BuildScope` / `DeriveCacheKey` /
`CanonicalizeUtf8`, the wiring picks up the fix transparently. **No
re-wiring, no caller-side `.Normalize(NormalizationForm.FormC)` calls
needed.** That was exactly the contract footgun we wanted to avoid.

## Maintainer-apply order (unchanged)

The recipe in `hotfix-p0/README.WI-1a-1b.md` still works as-is. When
applying the bundle, just take the **v2** copies of the 3 security files
from the security-hardening artifact dir (they overwrite v1 at the same
destination paths):

```
src/TravelAssistant.Api/Checkout/Security/JsonCanonicalizer.cs
src/TravelAssistant.Api/Checkout/Security/IdempotencyKeyDerivation.cs
tests/TravelAssistant.Tests.Unit/Security/IdempotencyKeyDerivationTests.cs
tests/TravelAssistant.Tests.Unit/Security/UnicodeNormalizationContractTests.cs  (NEW)
```

No changes to any file under `squads/application-development/artifacts/checkout/`.

## Test coverage on landing

| Layer | Count | Source | Status |
|---|---|---|---|
| Unit (primitives) | 7+7 = 14 | security-hardening (v1 + v2 NFC) | Runs green |
| Integration (endpoint) | 8 | app-dev `Tests/IdempotencyWi1aTests.cs` | Runs green |
| Regression (endpoint, ex-Skip) | 4 | QA PR #52 `IdempotencyRegressionTests.cs` | Un-skip on apply |
| Contract (endpoint, in-file ref) | 5 | QA `FailedPaymentReplayPreservesStatusCodeTests.cs` | Runs green today |
| Timing-statistical (R1) | 3 | QA `TimingOracleStatisticalTests.cs` | Runs green, no deps |
| Unicode NFC/NFD (R3 integration) | 6 | QA `UnicodeNormalizationTests.cs` | Un-skip on apply |

**Total: 40 idempotency-vertical tests once bundle lands.** 31 run green
on apply; 9 need un-skip in a follow-up commit on the hotfix PR.

## Verdict

- ✅ SEC-CHK-007 stays approved (security-hardening confirmed).
- ✅ R1 + R2 + R3 bound inside primitives, not caller-discipline.
- ✅ No app-dev re-work. Bennett out.

— Bennett, application-development-squad

> "Secure-by-default beats secure-if-the-caller-remembers, every time."
