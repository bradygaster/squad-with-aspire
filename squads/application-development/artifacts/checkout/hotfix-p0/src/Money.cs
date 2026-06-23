using System.Text.Json;
using System.Text.Json.Serialization;

namespace TravelAssistant.Api.Checkout;

// WI-3 (QA-002): Money as integer minor units. Decimal banished from the wire + persistence.
//
// Per-currency exponent (ISO 4217). Reject inputs whose decimal representation
// exceeds the exponent (e.g. JPY with cents -> 400; USD with 3 decimals -> 400).
public readonly record struct Money(long MinorUnits, string CurrencyCode)
{
    private static readonly Dictionary<string, int> Exponents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = 2, ["EUR"] = 2, ["GBP"] = 2, ["CAD"] = 2, ["AUD"] = 2, ["CHF"] = 2,
        ["JPY"] = 0, ["KRW"] = 0, ["VND"] = 0, ["CLP"] = 0,
        ["BHD"] = 3, ["KWD"] = 3, ["OMR"] = 3, ["JOD"] = 3, ["TND"] = 3,
    };

    public static int ExponentFor(string currency)
        => Exponents.TryGetValue(currency, out var e)
            ? e
            : throw new ArgumentException($"Unsupported currency '{currency}'.", nameof(currency));

    // Accepts a decimal value in MAJOR units (e.g. 12.34 USD, 100 JPY, 0.500 BHD)
    // and returns Money in minor units. Rejects values whose precision exceeds
    // the currency exponent.
    public static Money FromDecimalMajor(decimal majorAmount, string currencyCode)
    {
        var exp = ExponentFor(currencyCode);
        var scaled = majorAmount * (decimal)Math.Pow(10, exp);
        if (scaled != decimal.Truncate(scaled))
            throw new ArgumentException(
                $"Amount {majorAmount} has more decimal places than currency '{currencyCode}' allows ({exp}).",
                nameof(majorAmount));
        return new Money((long)scaled, currencyCode.ToUpperInvariant());
    }

    public Money Add(Money other)
    {
        if (!string.Equals(CurrencyCode, other.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Cannot add {CurrencyCode} and {other.CurrencyCode} — mismatched currencies.");
        return new Money(MinorUnits + other.MinorUnits, CurrencyCode);
    }

    public static Money operator +(Money a, Money b) => a.Add(b);

    public override string ToString() => $"{MinorUnits} {CurrencyCode} (minor units)";
}

// Custom JSON converter so Money serializes as { "minorUnits": 1234, "currencyCode": "USD" }
// — never as a decimal — across all checkout contracts.
public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Money must be an object.");

        long minorUnits = 0;
        string? currency = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return new Money(minorUnits, currency
                    ?? throw new JsonException("Money.currencyCode missing."));
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            var prop = reader.GetString();
            reader.Read();
            switch (prop?.ToLowerInvariant())
            {
                case "minorunits": minorUnits = reader.GetInt64(); break;
                case "currencycode": currency = reader.GetString(); break;
            }
        }
        throw new JsonException("Unterminated Money object.");
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("minorUnits", value.MinorUnits);
        writer.WriteString("currencyCode", value.CurrencyCode);
        writer.WriteEndObject();
    }
}
