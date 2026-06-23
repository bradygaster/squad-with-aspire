# Owed-from-QA tests — apply note

**Source:** quality-testing-squad, Hockney
**Vendored:** 2026-06-23 by application-development-squad (Bennett)

## What this is

Two test files closing the WI-1a coverage matrix (R1 timing + R3 Unicode NFC):

| File | Cases | Skip on land? |
|---|---|---|
| `TimingOracleStatisticalTests.cs` | 3 — Welch's t-test on FixedTimeEquals | **No** — runs live |
| `UnicodeNormalizationTests.cs`     | 6 — NFC/NFD body+key+scope + negative NFKC | **Yes initially**, **un-skip in this PR** |

## Where they land in the repo

Copy both `.cs` files into:
```
tests/TravelAssistant.Api.Tests/Checkout/
```

(Same folder as `IdempotencyWi1aTests.cs` from this bundle.)

## Un-skip path for `UnicodeNormalizationTests.cs`

QA's open question — "does `JsonCanonicalizer` NFC-normalize string values?" — is **YES**, already shipped in the **wi1a-nfc-amendment** bundle (commit `618294f`, re-baselined v2 of `JsonCanonicalizer.cs` + `IdempotencyKeyDerivation.cs` from security-hardening-squad). NFC is inside the primitives:

- `JsonCanonicalizer.WriteString` → NFC-normalizes before UTF-8 write
- `JsonCanonicalizer.WriteObject` → NFC-normalizes property names before sort
- `IdempotencyKeyDerivation.BuildScope` → NFC inside
- `IdempotencyKeyDerivation.DeriveCacheKey` → NFC inside

→ Remove `Skip = "..."` from all 6 cases in `UnicodeNormalizationTests.cs` once the wi1a-nfc-amendment bundle is applied (step 4 of the canary apply stack).

The negative case `Body_FullwidthDigits_AreNotEquivalentToAscii_UnderNfc` will **pass** because v2 uses `NormalizationForm.FormC` (not `FormKC`) per RFC 8785 §3.2.5.

## Apply order on `tamir/squad-fixes` (final)

1. hotfix-p0/                    ← WI-1/2/3 + WI-1a/1b + **these two files**
2. wi1c-redis-testauth/
3. wi4-wi5/
4. wi1a-nfc-amendment/           ← un-skip happens after this lands
5. azure-infra/sec-infra-redis-idempotency
6. wi6-redis-di-reconcile/
7. webhook-debug-endpoint/

## Coverage closeout

40 idempotency-vertical tests total:
- 14 unit (incl. these 9 from QA)
- 8 integration
- 4 regression-unskip
- 5 contract
- 3 timing-statistical (these)
- 6 NFC-unskip (these)

App-dev queue on checkout vertical: **zero**. Holding for maintainer-apply + canary.
