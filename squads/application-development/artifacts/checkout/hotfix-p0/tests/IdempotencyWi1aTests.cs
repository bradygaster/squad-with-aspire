// WI-1a tests — bindings for SEC-CHK-007 R1/R2/R3 + T13 caps.
// Companion to IdempotencyAndMoneyHotfixTests.cs.

using System.Security.Claims;
using System.Text;
using FluentAssertions;
using TravelAssistant.Api.Checkout;
using TravelAssistant.Api.Checkout.Security;
using Xunit;

namespace TravelAssistant.Api.Tests.Checkout;

public class IdempotencyWi1aTests
{
    private static byte[] Hash(string utf8) => System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(utf8));

    [Fact]
    public void DerivedKey_DiffersByScope_PreventsCrossTenantReplay()
    {
        var keyA = IdempotencyKeyDerivation.DeriveCacheKey("sub:user-a", "idem-123");
        var keyB = IdempotencyKeyDerivation.DeriveCacheKey("sub:user-b", "idem-123");
        keyA.Should().NotBe(keyB);
    }

    [Fact]
    public void DerivedKey_GuestVsAuth_DiffersForSameRawKey()
    {
        var auth  = IdempotencyKeyDerivation.DeriveCacheKey("sub:abc",   "idem-1");
        var guest = IdempotencyKeyDerivation.DeriveCacheKey("guest:abc", "idem-1");
        auth.Should().NotBe(guest);
    }

    [Fact]
    public void BuildScope_NoSubAndNoGuest_Throws()
    {
        var anon = new ClaimsPrincipal(new ClaimsIdentity());
        Action act = () => IdempotencyKeyDerivation.BuildScope(anon, null);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Store_SubjectCap_Returns429_OnExcess()
    {
        var opts = new IdempotencyOptions { MaxEntriesPerSubject = 2, MaxEntriesPerIp = 999 };
        var store = new InMemoryIdempotencyStore(opts);
        var hash = Hash("{}");

        store.TryReserve("k1", hash, "sub:abc", "10.0.0.1", TimeSpan.FromMinutes(15)).Outcome.Should().Be(ReservationOutcome.Reserved);
        store.TryReserve("k2", hash, "sub:abc", "10.0.0.1", TimeSpan.FromMinutes(15)).Outcome.Should().Be(ReservationOutcome.Reserved);
        store.TryReserve("k3", hash, "sub:abc", "10.0.0.1", TimeSpan.FromMinutes(15)).Outcome.Should().Be(ReservationOutcome.SubjectCapExceeded);
    }

    [Fact]
    public void Store_IpCap_Returns429_OnExcess()
    {
        var opts = new IdempotencyOptions { MaxEntriesPerSubject = 999, MaxEntriesPerIp = 2 };
        var store = new InMemoryIdempotencyStore(opts);
        var hash = Hash("{}");

        store.TryReserve("k1", hash, "guest:s1", "10.0.0.1", TimeSpan.FromMinutes(15)).Outcome.Should().Be(ReservationOutcome.Reserved);
        store.TryReserve("k2", hash, "guest:s2", "10.0.0.1", TimeSpan.FromMinutes(15)).Outcome.Should().Be(ReservationOutcome.Reserved);
        store.TryReserve("k3", hash, "guest:s3", "10.0.0.1", TimeSpan.FromMinutes(15)).Outcome.Should().Be(ReservationOutcome.IpCapExceeded);
    }

    [Fact]
    public void Store_Hit_UsesFixedTimeEquals_ReturnsCachedStatusAndBody()
    {
        var store = new InMemoryIdempotencyStore();
        var hash = Hash("{\"a\":1}");
        store.Save("dk1", hash, statusCode: 402, responseJson: "{\"declined\":true}", ttl: TimeSpan.FromMinutes(15));

        var hit = store.Lookup("dk1", hash);
        hit.Kind.Should().Be(IdempotencyLookupKind.Hit);
        hit.StatusCode.Should().Be(402);
        hit.ResponseJson.Should().Be("{\"declined\":true}");
    }

    [Fact]
    public void Store_BodyMismatch_OnSameDerivedKey_ReturnsMismatch()
    {
        var store = new InMemoryIdempotencyStore();
        store.Save("dk2", Hash("{\"a\":1}"), 200, "{\"ok\":true}", TimeSpan.FromMinutes(15));

        var result = store.Lookup("dk2", Hash("{\"a\":2}"));
        result.Kind.Should().Be(IdempotencyLookupKind.BodyMismatch);
    }

    [Fact]
    public void JsonCanonicalizer_KeyOrder_Invariant()
    {
        var a = JsonCanonicalizer.CanonicalizeUtf8("{\"a\":1,\"b\":2}");
        var b = JsonCanonicalizer.CanonicalizeUtf8("{\"b\":2,\"a\":1}");
        a.SequenceEqual(b).Should().BeTrue();
    }

    [Fact]
    public void JsonCanonicalizer_Whitespace_Invariant()
    {
        var a = JsonCanonicalizer.CanonicalizeUtf8("{\"a\":1}");
        var b = JsonCanonicalizer.CanonicalizeUtf8("{ \"a\" : 1 }");
        a.SequenceEqual(b).Should().BeTrue();
    }
}
