using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TravelAssistant.Api.Checkout;

// WI-1 (BUG-1): body-hashed idempotency. WI-2 (BUG-2): preserve original status code.
//
// Cache entry binds (key, bodyHash, sub-claim) -> { statusCode, body, createdAt, ttl=24h }.
// On hit + body hash match  -> replay cached response verbatim (status + body).
// On hit + body hash mismatch -> caller returns 422 problem+json.
// Concurrent in-flight same key -> second request gets 409 conflict-in-progress.
public interface IIdempotencyStore
{
    IdempotencyLookup Lookup(string key, string bodyHash, string? subjectClaim);

    // Marks the key as in-flight. Returns false if another request is already in flight.
    bool TryReserve(string key, string bodyHash, string? subjectClaim);

    void Save(string key, string bodyHash, string? subjectClaim, int statusCode, string responseJson);

    void ReleaseReservation(string key, string? subjectClaim);

    static string ComputeBodyHash(ReadOnlySpan<byte> body)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(body, hash);
        return Convert.ToHexString(hash);
    }

    // Canonicalize JSON: parse and re-serialize with sorted property names + no whitespace,
    // so structurally equal bodies hash identically regardless of source formatting.
    static string ComputeCanonicalBodyHash(string jsonBody)
    {
        if (string.IsNullOrWhiteSpace(jsonBody))
            return ComputeBodyHash(ReadOnlySpan<byte>.Empty);
        using var doc = JsonDocument.Parse(jsonBody);
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(doc.RootElement, writer);
        }
        return ComputeBodyHash(ms.ToArray());
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject()
                                            .OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(prop.Name);
                    WriteCanonical(prop.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}

public enum IdempotencyLookupKind { Miss, Hit, BodyMismatch, InFlight }

public sealed record IdempotencyLookup(
    IdempotencyLookupKind Kind,
    int StatusCode = 0,
    string ResponseJson = "");

public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<string, Entry> _store = new();
    private readonly ConcurrentDictionary<string, string> _inFlight = new(); // key -> bodyHash

    private static string ScopedKey(string key, string? subjectClaim)
        => string.IsNullOrEmpty(subjectClaim) ? key : $"{subjectClaim}::{key}";

    public IdempotencyLookup Lookup(string key, string bodyHash, string? subjectClaim)
    {
        var scoped = ScopedKey(key, subjectClaim);

        if (_store.TryGetValue(scoped, out var entry))
        {
            if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _store.TryRemove(scoped, out _);
            }
            else
            {
                return CryptographicOperations.FixedTimeEquals(
                        Encoding.ASCII.GetBytes(entry.BodyHash),
                        Encoding.ASCII.GetBytes(bodyHash))
                    ? new IdempotencyLookup(IdempotencyLookupKind.Hit, entry.StatusCode, entry.ResponseJson)
                    : new IdempotencyLookup(IdempotencyLookupKind.BodyMismatch);
            }
        }

        if (_inFlight.TryGetValue(scoped, out var inflightHash))
        {
            return CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(inflightHash),
                    Encoding.ASCII.GetBytes(bodyHash))
                ? new IdempotencyLookup(IdempotencyLookupKind.InFlight)
                : new IdempotencyLookup(IdempotencyLookupKind.BodyMismatch);
        }

        return new IdempotencyLookup(IdempotencyLookupKind.Miss);
    }

    public bool TryReserve(string key, string bodyHash, string? subjectClaim)
        => _inFlight.TryAdd(ScopedKey(key, subjectClaim), bodyHash);

    public void Save(string key, string bodyHash, string? subjectClaim, int statusCode, string responseJson)
    {
        var scoped = ScopedKey(key, subjectClaim);
        _store[scoped] = new Entry(bodyHash, statusCode, responseJson,
            DateTimeOffset.UtcNow.Add(Ttl));
        _inFlight.TryRemove(scoped, out _);
    }

    public void ReleaseReservation(string key, string? subjectClaim)
        => _inFlight.TryRemove(ScopedKey(key, subjectClaim), out _);

    private sealed record Entry(string BodyHash, int StatusCode, string ResponseJson, DateTimeOffset ExpiresAt);
}
