// tests/TravelAssistant.Api.Tests/Checkout/UnicodeNormalizationTests.cs
// Owed by QA per WI-1a coverage gap. Unicode NFC/NFD invariance for R3 (RFC 8785 JCS).
//
// THE THREAT: An attacker submits the same logical idempotency key or body
// with Unicode normalization variants (NFC vs NFD vs NFKC vs NFKD) hoping
// the cache treats them as different keys / different bodies, opening a
// replay window or a payment double-charge.
//
// CONTRACT: RFC 8785 JCS requires NFC normalization of all string values
// before serialization. Security squad's JsonCanonicalizer should normalize
// to NFC. If it doesn't, the same logical string yields different byte
// sequences → different hashes → idempotency-key cache thinks they're
// different requests.
//
// SCOPE: 6 cases covering:
//   - NFC vs NFD on body string values (the canonicalizer's job)
//   - NFC vs NFD on idempotency key (the key deriver's job)
//   - Combining marks (é vs e + combining acute)
//   - Hangul syllables (precomposed vs decomposed jamo)
//   - Compatibility forms (NFKC fullwidth digits vs ASCII)
//   - Order-independence: two combining marks in different orders

using System.Globalization;
using System.Text;
using FluentAssertions;
using Xunit;

namespace TravelAssistant.Api.Tests.Checkout;

public class UnicodeNormalizationTests
{
    // Replace with real type once visible from test asm. Skipped until WI-1a wiring lands
    // (the merge-gate PR #53 already established this skip pattern).
    private const string SkipUntilWi1a =
        "Requires WI-1a wired: IdempotencyKeyDerivation + JsonCanonicalizer from security-hardening-squad. Un-skip in fix/checkout-idempotency-p0 PR.";

    // ---------------------------------------------------------------------
    // BODY canonicalization — NFC normalization invariance
    // ---------------------------------------------------------------------

    [Fact(Skip = SkipUntilWi1a)]
    public void Body_LatinAccent_NfcAndNfd_ProduceSameHash()
    {
        // "café" — NFC: c-a-f-é (4 chars), NFD: c-a-f-e + combining acute (5 chars)
        // Same logical content, different bytes. JCS must normalize → same hash.
        var nfc = "{\"city\":\"caf\u00E9\"}";              // U+00E9 (é precomposed)
        var nfd = "{\"city\":\"cafe\u0301\"}";              // e + U+0301 (combining acute)

        nfc.IsNormalized(NormalizationForm.FormC).Should().BeTrue();
        nfd.IsNormalized(NormalizationForm.FormD).Should().BeTrue();

        var hashNfc = HashCanonicalized(nfc);
        var hashNfd = HashCanonicalized(nfd);
        hashNfd.Should().Equal(hashNfc,
            "JCS must NFC-normalize string values — otherwise an attacker can submit the same logical payload " +
            "and bypass idempotency by switching normalization forms.");
    }

    [Fact(Skip = SkipUntilWi1a)]
    public void Body_HangulSyllable_PrecomposedAndDecomposed_ProduceSameHash()
    {
        // "한" — NFC: U+D55C single syllable. NFD: U+1112 + U+1161 + U+11AB jamo.
        // Same visual, same logical content. JCS must normalize.
        var nfc = "{\"name\":\"\uD55C\"}";
        var nfd = "{\"name\":\"\u1112\u1161\u11AB\"}";

        var hashNfc = HashCanonicalized(nfc);
        var hashNfd = HashCanonicalized(nfd);
        hashNfd.Should().Equal(hashNfc, "Hangul syllable normalization is required by NFC");
    }

    [Fact(Skip = SkipUntilWi1a)]
    public void Body_CombiningMarkOrder_ProducesSameHashAfterNormalization()
    {
        // Two combining marks: combining cedilla (U+0327) + combining acute (U+0301).
        // Unicode canonical reordering algorithm puts them in deterministic order.
        // NFC normalization must apply this — different input orders → same output.
        var orderA = "{\"v\":\"e\u0327\u0301\"}";  // cedilla then acute
        var orderB = "{\"v\":\"e\u0301\u0327\"}";  // acute then cedilla

        var hashA = HashCanonicalized(orderA);
        var hashB = HashCanonicalized(orderB);
        hashB.Should().Equal(hashA,
            "Canonical reordering of combining marks must produce identical bytes — " +
            "otherwise an attacker can produce arbitrarily many distinct cache keys for the same logical body.");
    }

    [Fact(Skip = SkipUntilWi1a)]
    public void Body_FullwidthDigits_AreNotEquivalentToAscii_UnderNfc()
    {
        // NEGATIVE TEST. NFC must NOT fold fullwidth digits to ASCII (that's NFKC).
        // If our JCS impl accidentally uses NFKC, "１２３" and "123" would hash the same,
        // which is a SEMANTIC change — fullwidth "１" is a different glyph and a
        // legitimate value to preserve. NFKC is wrong here.
        var ascii = "{\"amount\":\"123\"}";
        var fullwidth = "{\"amount\":\"\uFF11\uFF12\uFF13\"}";

        var hashAscii = HashCanonicalized(ascii);
        var hashFullwidth = HashCanonicalized(fullwidth);
        hashFullwidth.Should().NotEqual(hashAscii,
            "NFC (not NFKC) is correct — fullwidth digits are semantically distinct from ASCII. " +
            "If this test fails, JCS is using NFKC and silently coercing user input.");
    }

    // ---------------------------------------------------------------------
    // IDEMPOTENCY-KEY derivation — NFC normalization invariance
    // ---------------------------------------------------------------------

    [Fact(Skip = SkipUntilWi1a)]
    public void DerivedKey_IdempotencyKeyWithCombiningMarks_NormalizedBeforeHashing()
    {
        // Attacker submits Idempotency-Key headers that are visually identical but
        // differ in normalization form. If the deriver doesn't NFC-normalize the
        // raw key string before hashing, each variant becomes a distinct cache
        // entry and idempotency is broken.
        var keyNfc = "order-caf\u00E9-2025";
        var keyNfd = "order-cafe\u0301-2025";

        var derivedNfc = DeriveCacheKey(scope: "sub:alice", rawKey: keyNfc);
        var derivedNfd = DeriveCacheKey(scope: "sub:alice", rawKey: keyNfd);

        derivedNfd.Should().Equal(derivedNfc,
            "Idempotency-Key strings must be NFC-normalized before scope-binding and hashing. " +
            "Otherwise the same logical key creates N cache entries (entry-cap exhaustion + replay window).");
    }

    [Fact(Skip = SkipUntilWi1a)]
    public void DerivedKey_NonNormalizedScope_NormalizedBeforeHashing()
    {
        // The scope component (e.g., guest:{sessionId}) may carry user-controlled
        // data in the guest path. If sessionId contains combining marks (e.g., a
        // Unicode-aware session generator), the deriver must normalize it too —
        // otherwise the same guest can present two normalizations of the same
        // sessionId and get distinct cache buckets.
        var rawKey = "order-2025";
        var sessionNfc = "session-caf\u00E9";
        var sessionNfd = "session-cafe\u0301";

        var derivedNfc = DeriveCacheKey(scope: $"guest:{sessionNfc}", rawKey: rawKey);
        var derivedNfd = DeriveCacheKey(scope: $"guest:{sessionNfd}", rawKey: rawKey);

        derivedNfd.Should().Equal(derivedNfc,
            "Scope strings (including guest sessionId) must be NFC-normalized before key derivation.");
    }

    // ---------------------------------------------------------------------
    // helpers — replaced with real types once visible
    // ---------------------------------------------------------------------

    private static byte[] HashCanonicalized(string json)
    {
        // TODO: wire to JsonCanonicalizer.CanonicalizeUtf8 + SHA-256 once test asm sees the type.
        // var canonical = JsonCanonicalizer.CanonicalizeUtf8(json);
        // return SHA256.HashData(canonical);
        throw new NotImplementedException("Wire to JsonCanonicalizer + SHA256.HashData in WI-1a PR");
    }

    private static byte[] DeriveCacheKey(string scope, string rawKey)
    {
        // TODO: wire to IdempotencyKeyDerivation.DeriveCacheKey(scope, rawKey).
        throw new NotImplementedException("Wire to IdempotencyKeyDerivation.DeriveCacheKey in WI-1a PR");
    }
}
