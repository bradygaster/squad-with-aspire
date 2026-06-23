using System.Text;
using TravelAssistant.Api.Checkout;
using Xunit;

namespace TravelAssistant.Api.Tests.Checkout;

// WI-1 + WI-2 regression: body-hashed idempotency, 422 on mismatch,
// preserve original status code on replay.
public class IdempotencyStoreHotfixTests
{
    private static string Hash(string body)
        => IIdempotencyStore.ComputeCanonicalBodyHash(body);

    [Fact]
    public void Miss_When_Key_Unknown()
    {
        var store = new InMemoryIdempotencyStore();
        var result = store.Lookup("k1", Hash("{}"), subjectClaim: null);
        Assert.Equal(IdempotencyLookupKind.Miss, result.Kind);
    }

    [Fact]
    public void Hit_Returns_Cached_StatusCode_And_Body()
    {
        var store = new InMemoryIdempotencyStore();
        var body = "{\"sessionId\":\"s1\",\"paymentToken\":\"tok\"}";
        store.TryReserve("k1", Hash(body), null);
        store.Save("k1", Hash(body), null,
            statusCode: 402, responseJson: "{\"status\":\"declined\"}");

        var result = store.Lookup("k1", Hash(body), null);

        Assert.Equal(IdempotencyLookupKind.Hit, result.Kind);
        Assert.Equal(402, result.StatusCode);
        Assert.Equal("{\"status\":\"declined\"}", result.ResponseJson);
    }

    [Fact]
    public void BodyMismatch_When_Same_Key_Different_Body()
    {
        var store = new InMemoryIdempotencyStore();
        var bodyA = "{\"sessionId\":\"sA\",\"paymentToken\":\"tok\"}";
        var bodyB = "{\"sessionId\":\"sB\",\"paymentToken\":\"tok\"}";
        store.Save("k1", Hash(bodyA), null, 200, "{\"status\":\"succeeded\"}");

        var result = store.Lookup("k1", Hash(bodyB), null);

        Assert.Equal(IdempotencyLookupKind.BodyMismatch, result.Kind);
    }

    [Fact]
    public void Canonical_Hash_Ignores_Whitespace_And_Key_Order()
    {
        var bodyA = "{\"sessionId\":\"s1\",\"paymentToken\":\"tok\"}";
        var bodyB = "{ \"paymentToken\" : \"tok\" , \"sessionId\" : \"s1\" }";
        Assert.Equal(Hash(bodyA), Hash(bodyB));
    }

    [Fact]
    public void InFlight_When_Reservation_Held_For_Same_Key_And_Body()
    {
        var store = new InMemoryIdempotencyStore();
        var body = "{\"sessionId\":\"s1\"}";
        Assert.True(store.TryReserve("k1", Hash(body), null));

        var second = store.Lookup("k1", Hash(body), null);

        Assert.Equal(IdempotencyLookupKind.InFlight, second.Kind);
    }

    [Fact]
    public void SubjectClaim_Scopes_Keys()
    {
        var store = new InMemoryIdempotencyStore();
        var body = "{\"sessionId\":\"s1\"}";
        store.Save("k1", Hash(body), subjectClaim: "userA", 200, "{\"ownedBy\":\"A\"}");

        var userBLookup = store.Lookup("k1", Hash(body), subjectClaim: "userB");

        Assert.Equal(IdempotencyLookupKind.Miss, userBLookup.Kind);
    }
}

// WI-3 regression: Money minor-units + currency exponent validation.
public class MoneyTests
{
    [Theory]
    [InlineData(12.34, "USD", 1234)]
    [InlineData(100, "JPY", 100)]
    [InlineData(0.500, "BHD", 500)]
    [InlineData(0, "EUR", 0)]
    public void FromDecimalMajor_Produces_Minor_Units(decimal major, string ccy, long expected)
    {
        var money = Money.FromDecimalMajor(major, ccy);
        Assert.Equal(expected, money.MinorUnits);
        Assert.Equal(ccy.ToUpperInvariant(), money.CurrencyCode);
    }

    [Theory]
    [InlineData(12.345, "USD")]   // 3dp not allowed for USD
    [InlineData(100.5, "JPY")]    // any fractional not allowed for JPY
    [InlineData(0.5005, "BHD")]   // 4dp not allowed for BHD
    public void FromDecimalMajor_Rejects_Over_Precision(decimal major, string ccy)
    {
        Assert.Throws<ArgumentException>(() => Money.FromDecimalMajor(major, ccy));
    }

    [Fact]
    public void Add_Rejects_Mixed_Currencies()
    {
        var a = new Money(100, "USD");
        var b = new Money(100, "EUR");
        Assert.Throws<InvalidOperationException>(() => a + b);
    }

    [Fact]
    public void Add_Sums_Same_Currency()
    {
        var a = new Money(100, "USD");
        var b = new Money(50, "USD");
        Assert.Equal(150, (a + b).MinorUnits);
    }
}
