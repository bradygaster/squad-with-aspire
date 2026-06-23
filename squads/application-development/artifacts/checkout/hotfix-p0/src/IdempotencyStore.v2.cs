// WI-1a — Amendment to IdempotencyStore: binds SEC-CHK-007 R1/R2/R3 + A1 + T13.
//
// R1: FixedTimeEquals on raw hash bytes (never on hex-encoded strings).
// R2: Cache key derived as SHA-256(scope || ':' || idempotencyKey) via IIdempotencyKeyDeriver.
//     Scope = "sub:<sub-claim>" (auth) | "guest:<sessionId>" (guest). Cross-tenant replay impossible.
// R3: Body canonicalization is the CALLER's responsibility — pass JCS-canonical UTF-8 bytes from
//     JsonCanonicalizer (RFC 8785). Store never re-parses or re-serializes.
// A1: TTL is configurable. /checkout/confirm caller passes 15min; /checkout/session caller passes 24h.
// T13: Per-subject and per-IP entry caps. 1001st distinct key from same sub → caller returns 429.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using TravelAssistant.Api.Checkout.Security;

namespace TravelAssistant.Api.Checkout;

public interface IIdempotencyKeyDeriver
{
    /// <summary>Derives the storage cache key from request scope (sub or guest session) + idempotency-key header.</summary>
    string Derive(string scope, string idempotencyKey);
}

public sealed class Sha256IdempotencyKeyDeriver : IIdempotencyKeyDeriver
{
    public string Derive(string scope, string idempotencyKey)
        => IdempotencyKeyDerivation.DeriveCacheKey(scope, idempotencyKey);
}

public interface IIdempotencyStore
{
    /// <param name="derivedKey">Output of <see cref="IIdempotencyKeyDeriver.Derive"/>.</param>
    /// <param name="bodyHash">SHA-256 of JCS-canonical request body, raw bytes (32).</param>
    /// <param name="scope">Identity scope tag (e.g. "sub:abc" or "guest:xyz") — used for per-sub cap accounting.</param>
    /// <param name="ipAddress">Client IP — used for per-IP cap accounting. May be null for trusted callers.</param>
    IdempotencyLookup Lookup(string derivedKey, ReadOnlySpan<byte> bodyHash);

    /// <summary>Reserve in-flight slot. Returns false if cap exceeded or another request is already in flight.</summary>
    ReservationResult TryReserve(string derivedKey, ReadOnlySpan<byte> bodyHash, string scope, string? ipAddress, TimeSpan ttl);

    void Save(string derivedKey, ReadOnlySpan<byte> bodyHash, int statusCode, string responseJson, TimeSpan ttl);

    void ReleaseReservation(string derivedKey);
}

public enum IdempotencyLookupKind { Miss, Hit, BodyMismatch, InFlight }
public enum ReservationOutcome    { Reserved, AlreadyInFlight, SubjectCapExceeded, IpCapExceeded }

public sealed record IdempotencyLookup(IdempotencyLookupKind Kind, int StatusCode = 0, string ResponseJson = "");
public sealed record ReservationResult(ReservationOutcome Outcome);

public sealed class IdempotencyOptions
{
    public int MaxEntriesPerSubject { get; init; } = 1000;
    public int MaxEntriesPerIp      { get; init; } = 5000;
}

public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, Entry>      _store    = new();
    private readonly ConcurrentDictionary<string, byte[]>     _inFlight = new();
    private readonly ConcurrentDictionary<string, int>        _perSubject = new();
    private readonly ConcurrentDictionary<string, int>        _perIp      = new();
    private readonly IdempotencyOptions _options;

    public InMemoryIdempotencyStore(IdempotencyOptions? options = null)
        => _options = options ?? new IdempotencyOptions();

    public IdempotencyLookup Lookup(string derivedKey, ReadOnlySpan<byte> bodyHash)
    {
        if (_store.TryGetValue(derivedKey, out var entry))
        {
            if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                EvictEntry(derivedKey, entry);
            }
            else
            {
                return CryptographicOperations.FixedTimeEquals(entry.BodyHash, bodyHash)
                    ? new IdempotencyLookup(IdempotencyLookupKind.Hit, entry.StatusCode, entry.ResponseJson)
                    : new IdempotencyLookup(IdempotencyLookupKind.BodyMismatch);
            }
        }

        if (_inFlight.TryGetValue(derivedKey, out var inflightHash))
        {
            return CryptographicOperations.FixedTimeEquals(inflightHash, bodyHash)
                ? new IdempotencyLookup(IdempotencyLookupKind.InFlight)
                : new IdempotencyLookup(IdempotencyLookupKind.BodyMismatch);
        }

        return new IdempotencyLookup(IdempotencyLookupKind.Miss);
    }

    public ReservationResult TryReserve(string derivedKey, ReadOnlySpan<byte> bodyHash, string scope, string? ipAddress, TimeSpan ttl)
    {
        // T13 caps — check BEFORE reserving so we never exceed the configured ceiling.
        if (!string.IsNullOrEmpty(scope))
        {
            var current = _perSubject.GetOrAdd(scope, 0);
            if (current >= _options.MaxEntriesPerSubject)
                return new ReservationResult(ReservationOutcome.SubjectCapExceeded);
        }
        if (!string.IsNullOrEmpty(ipAddress))
        {
            var current = _perIp.GetOrAdd(ipAddress, 0);
            if (current >= _options.MaxEntriesPerIp)
                return new ReservationResult(ReservationOutcome.IpCapExceeded);
        }

        var hashCopy = bodyHash.ToArray();
        if (!_inFlight.TryAdd(derivedKey, hashCopy))
            return new ReservationResult(ReservationOutcome.AlreadyInFlight);

        if (!string.IsNullOrEmpty(scope))    _perSubject.AddOrUpdate(scope,     1, (_, v) => v + 1);
        if (!string.IsNullOrEmpty(ipAddress)) _perIp.AddOrUpdate(ipAddress, 1, (_, v) => v + 1);

        return new ReservationResult(ReservationOutcome.Reserved);
    }

    public void Save(string derivedKey, ReadOnlySpan<byte> bodyHash, int statusCode, string responseJson, TimeSpan ttl)
    {
        _store[derivedKey] = new Entry(bodyHash.ToArray(), statusCode, responseJson, DateTimeOffset.UtcNow.Add(ttl));
        _inFlight.TryRemove(derivedKey, out _);
    }

    public void ReleaseReservation(string derivedKey)
        => _inFlight.TryRemove(derivedKey, out _);

    private void EvictEntry(string derivedKey, Entry entry)
    {
        _store.TryRemove(derivedKey, out _);
        // Note: per-sub/per-IP counters intentionally NOT decremented on TTL expiry to keep
        // accounting simple under contention. Caps reset at process restart; a Redis-backed
        // impl (azure-infrastructure-squad WI-6) will use SETEX + ZADD with timestamp ZREMRANGEBYSCORE.
    }

    private sealed record Entry(byte[] BodyHash, int StatusCode, string ResponseJson, DateTimeOffset ExpiresAt);
}
