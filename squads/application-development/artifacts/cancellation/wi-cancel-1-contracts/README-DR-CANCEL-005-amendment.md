# DR-CANCEL-005 Amendment — Reasons projection helper

**Status:** Shipped. Additive to DR-CANCEL-001 → 004 base.
**Trigger:** QA cross-surface drift gate (`ProviderReasonMappingTests.v2.cs`, session aba3a30d)
asked whether to (a) inline `ToSnakeCase` in the test or (b) consume an app-dev-owned
projection helper. **Answer: (b) — ship the helper.** Single source of truth, no regex
in the test tree, ownership boundary preserved.

## What changed — 1 file, additive

`CancelErrorEnvelope.cs` gains a nested `Reasons` static class:

```csharp
public static class Reasons
{
    // Frozen ordered list — server precedence order.
    public static readonly IReadOnlyList<string> All = ImmutableArray.Create(
        Codes.ReasonAlreadyCanceled,
        Codes.ReasonAlreadyRefunded,
        Codes.ReasonWindowExpired,
        Codes.ReasonFulfillmentInProgress);

    // Single source of truth for enum→wire-string projection.
    public static string ForEnum(CancelIneligibilityReason reason) => reason switch { ... };
}
```

Existing `Codes.ReasonAlreadyCanceled` etc. constants are **unchanged** — `Reasons.All`
points at the same constants. Zero risk to bundles 1–7 already applied.

## QA consumption pattern

```csharp
// ProviderReasonMappingTests.v2.cs cross-surface drift gate
[Fact]
public void EnumAndWireStringSurfacesAgree()
{
    var fromEnum = Enum.GetValues<CancelIneligibilityReason>()
        .Select(CancelErrorEnvelope.Reasons.ForEnum)
        .OrderBy(s => s)
        .ToArray();

    var fromStrings = CancelErrorEnvelope.Reasons.All
        .OrderBy(s => s)
        .ToArray();

    Assert.Equal(fromStrings, fromEnum);  // Build break if surfaces drift.
}
```

No `ToSnakeCase` regex in the test tree. QA's `WebhookCancelDeclinedTest` and
`ProviderReasonMappingTests.v2` both consume `Reasons.ForEnum(...)` instead of
hardcoding `"already_refunded"` etc.

## Ownership boundary (unchanged)

- **App-dev OWNS** `CancelErrorEnvelope.Codes.Reason*` constants, `CancelIneligibilityReason`
  enum, AND the `Reasons.ForEnum()` projection. If the enum gains a 5th value, app-dev
  adds the matching `Codes.Reason*` constant + extends the switch + bumps `Reasons.All` —
  one commit, three coordinated edits, OR the unit test fails.
- **QA CONSUMES** via `using static CancelErrorEnvelope.Reasons` and the helper.
  No regex, no string duplication, no enum-value duplication.
- **Review-deployment ASSERTS** deployed `error.reason` siblings ∈ `Reasons.All` exactly.

## Why a helper, not a regex

QA's offered fallback (inline `ToSnakeCase` in the assertion) would work mechanically
for `AlreadyRefunded → already_refunded` today. But:

1. If the team ever adds an enum value with a numeric suffix, an acronym, or a
   compound that breaks the mechanical rule, the regex silently maps wrong while
   the contract pins a different spelling. The switch statement throws loudly.
2. Two surfaces evolving "together" via a shared regex assumes the regex is correct
   in both places. The helper is the single source of truth — there is only one
   place to be wrong.
3. The ownership boundary is cleaner: QA should never own the projection logic for
   an app-dev-owned contract.

## Build break contract

| Change | What breaks |
|--------|-------------|
| Add enum value, forget `ForEnum` case | `ProviderReasonMappingTests.v2.EnumAndWireStringSurfacesAgree` throws on the unmapped value. |
| Add `Codes.ReasonXyz`, forget enum value | `Reasons.All` won't grow; cross-surface gate fails on count mismatch. |
| Add `Codes.ReasonXyz`, forget `Reasons.All` | `EnumerationGuard` (QA's `WebhookEnvelopeEnumerationGuard.cs` extended pattern) fails — every `Codes.Reason*` const MUST be in `Reasons.All`. |
| Rename a wire string | Compile error everywhere via `Codes.Reason*` reference chain. |

## Apply order

DR-005 is a **post-DR-004** additive change to `wi-cancel-1-contracts/`. Applies
cleanly on `tamir/squad-fixes` HEAD. No reordering of QA's 7 test bundles needed —
bundle 7 (`cancel-v1-webhook-envelope-alignment`, session aba3a30d) was authored
*expecting* this helper per QA's open question.

## Scope — what DR-005 does NOT do

- Does NOT change any `Codes.*` value or the enum.
- Does NOT change webhook envelope (`CancelWebhookEnvelope.cs`) or mapper interface.
- Does NOT touch `FakePaymentProviderCancelClient`, the README base, or DR-002/003/004 READMEs.
- Does NOT wire production code — backend bundle still queued for SPM v1 100%.

## Contract surface for WI-CANCEL-1 now fully frozen across DR-001+002+003+004+005.

Dispatch order unchanged: refunds v1 → 100% → SPM v1 → 100% → cancel v1 backend bundle ships with real Stripe/Adyen `IPaymentProviderCancelClient` impls + `IProviderReasonMapper` adapter wiring + endpoint + repo + DI + seams #2–#5.
