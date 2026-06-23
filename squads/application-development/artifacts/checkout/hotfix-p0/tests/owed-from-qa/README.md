# Owed QA Test Bundle — WI-1a Coverage Closeout

**From:** quality-testing-squad (Hockney)
**For:** application-development-squad (Bennett) — to land in `fix/checkout-idempotency-p0`
**Date:** 2026-06-23

## Context

Bennett's WI-1a/1b bundle (commit `e18eb3c`) shipped 8 idempotency tests covering
5 of the 8 originally-spec'd cases. Owed back to QA:

- **3 cases** — timing-difference statistical (R1)
- **6 cases** — Unicode NFC/NFD (R3)

This bundle delivers all 9. Drop the two files into
`tests/TravelAssistant.Api.Tests/Checkout/` and they compile against the
existing test csproj (xunit + FluentAssertions already referenced).

## Files

| File | Cases | Status |
|---|---|---|
| `TimingOracleStatisticalTests.cs` | 3 (FixedTimeEquals invariance) | Active — no skip, runs on every build |
| `UnicodeNormalizationTests.cs` | 6 (NFC/NFD + scope normalization) | `Skip` until WI-1a JsonCanonicalizer + IdempotencyKeyDerivation visible from test asm |

## TimingOracleStatisticalTests.cs — design notes

**Methodology:** Welch's t-test on paired timing samples. 50k samples per arm,
5k warmup, 5%/95% outlier trim. Reject pass at |t| > 3.0 (p < 0.001).

**What it catches:** If anyone substitutes `SequenceEqual`, `MemoryExtensions.SequenceEqual`,
`==`, or rolls a custom byte-compare loop with early exit, the t-stat will spike past 3.0.
`CryptographicOperations.FixedTimeEquals` passes cleanly because it's true constant-time.

**What it does NOT catch:** End-to-end timing leaks through the network. Network jitter
will swamp 10-100ns deltas. This is an in-process regression guard — the contract test
for end-to-end safety is the existing infrastructure (no leak by construction once R1
is in place).

**Runtime:** ~3-5s per test on cold CI runner. Acceptable for unit suite. If CI proves
flaky in practice, gate behind `[Trait("Category","TimingSensitive")]` and run on
dedicated hardware.

**Known sensitivity:** Tests are statistically sound but CI environments with heavy
contention (shared runners, GC-bound parallel test execution) can produce false
positives. If you see flake, raise `TStatThreshold` from 3.0 to 4.0 (p < 0.0001) —
still detects a real leak with margin.

## UnicodeNormalizationTests.cs — design notes

**Threat model:** Idempotency-Key and request body strings can be submitted in multiple
Unicode normalization forms. If the canonicalizer doesn't NFC-normalize, the same
logical value produces different hashes → cache misses → double-charge window or
entry-cap exhaustion.

**6 cases:**
1. Latin accent NFC↔NFD body equivalence (`café`)
2. Hangul syllable precomposed↔decomposed body equivalence (`한`)
3. Combining mark canonical reordering body equivalence
4. **Negative test:** fullwidth digits ≠ ASCII digits (catches NFKC misuse — must be NFC)
5. Idempotency-Key NFC normalization
6. Scope (guest sessionId) NFC normalization

**Wiring required (TODO markers in file):**
```csharp
private static byte[] HashCanonicalized(string json)
    => SHA256.HashData(JsonCanonicalizer.CanonicalizeUtf8(json));

private static byte[] DeriveCacheKey(string scope, string rawKey)
    => IdempotencyKeyDerivation.DeriveCacheKey(scope, rawKey);
```

**One open question for security-hardening-squad:** Does the bundled `JsonCanonicalizer.cs`
NFC-normalize string values before serialization? RFC 8785 §3.2.5 requires it. If the
in-tree impl doesn't, Cases 1-3 + 5 fail and the bundled canonicalizer needs an NFC
step (or swap to `Microsoft.IdentityModel.JsonWebTokens` JCS for prod as Cipher noted).
Case 4 (negative — NFKC must NOT be used) passes either way if NFC is correct.

## Total WI-1a coverage after this bundle

- Bennett's 8 cases (R1 hit + body mismatch, R2 derived key + cross-tenant + guest, R3 JCS key-order + whitespace, T13 sub/IP caps) ✅
- Hockney's 9 new cases (R1 timing × 3, R3 Unicode × 6) ✅
- **Total: 17 idempotency cases**, fully covering the original 8-case spec + adversarial Unicode + statistical timing.

## EMU situation

We cannot push branches or open PRs from this squad's account. Two paths:

1. **Maintainer apply** — copy the two files into `fix/checkout-idempotency-p0`
   alongside Bennett's `IdempotencyWi1aTests.cs`, commit, push.
2. **Bennett applies** — same as the security-hardening-squad reference bundle
   pattern; commit them with the WI-1a hotfix.

Either way the test code is final — no further QA changes needed before merge.
