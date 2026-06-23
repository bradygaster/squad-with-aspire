// File: src/TravelAssistant.Api/Checkout/Idempotency/RedisIdempotencyStore.cs
// Purpose: WI-6 app-side — distributed IIdempotencyStore backed by Azure Cache for Redis,
// Entra ID auth (no access keys). Matches azure-infrastructure-squad's Bicep (Standard C1,
// disableAccessKeyAuthentication=true, private endpoint).
//
// Contract preserved exactly from InMemoryIdempotencyStore:
//   TryReserve(derivedCacheKey, bodyHash, subject, clientIp, ttl)
//     -> ReservationOutcome.Reserved | Hit(cached) | BodyMismatch | SubjectCapExceeded | IpCapExceeded
//   Save(derivedCacheKey, statusCode, body, ttl)
//
// Redis semantics:
//   * Per-entry key:    idem:{derivedCacheKey}             (HASH: bodyHash, status, body, reservedAt)
//   * Per-subject zset: idem:cap:sub:{subject}             (score = reservedAt epoch ms, member = derivedCacheKey)
//   * Per-IP zset:      idem:cap:ip:{ip}                   (same shape)
//   All keys carry the per-call TTL so caps self-trim on expiry.
//
// SET NX is done via Lua script for atomicity across the entry-key + cap-zset writes.

using System.Security.Cryptography;
using System.Text;
using Azure.Identity;
using Microsoft.Azure.StackExchangeRedis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace TravelAssistant.Api.Checkout.Idempotency;

public sealed class RedisIdempotencyOptions
{
    public required string Endpoint { get; init; }          // e.g. mycache.redis.cache.windows.net
    public int Port { get; init; } = 6380;                  // TLS
    public int MaxEntriesPerSubject { get; init; } = 1000;  // T13
    public int MaxEntriesPerIp { get; init; } = 5000;
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

public sealed class RedisIdempotencyStore : IIdempotencyStore, IAsyncDisposable
{
    private readonly RedisIdempotencyOptions _opts;
    private readonly ILogger<RedisIdempotencyStore> _log;
    private readonly Lazy<Task<IConnectionMultiplexer>> _connection;

    // Lua: atomically reserve entry + add to per-sub/per-ip zsets, OR return existing entry.
    // KEYS[1] = entry key, KEYS[2] = sub zset, KEYS[3] = ip zset
    // ARGV[1] = bodyHash (hex), ARGV[2] = ttlMs, ARGV[3] = nowMs,
    // ARGV[4] = maxSubCap, ARGV[5] = maxIpCap, ARGV[6] = cacheKeyMember
    // Returns: {"RESERVED"} | {"HIT", bodyHash, status, body} | {"MISMATCH"} | {"SUB_CAP"} | {"IP_CAP"}
    private const string ReserveScript = @"
local existing = redis.call('HGET', KEYS[1], 'bodyHash')
if existing then
  if existing == ARGV[1] then
    local s = redis.call('HGET', KEYS[1], 'status')
    local b = redis.call('HGET', KEYS[1], 'body')
    return {'HIT', existing, s or '', b or ''}
  else
    return {'MISMATCH'}
  end
end
-- Trim expired members (score < now - ttl) defensively
local cutoff = tonumber(ARGV[3]) - tonumber(ARGV[2])
redis.call('ZREMRANGEBYSCORE', KEYS[2], '-inf', cutoff)
redis.call('ZREMRANGEBYSCORE', KEYS[3], '-inf', cutoff)
local subCount = redis.call('ZCARD', KEYS[2])
if subCount >= tonumber(ARGV[4]) then return {'SUB_CAP'} end
local ipCount = redis.call('ZCARD', KEYS[3])
if ipCount >= tonumber(ARGV[5]) then return {'IP_CAP'} end
redis.call('HSET', KEYS[1], 'bodyHash', ARGV[1], 'reservedAt', ARGV[3])
redis.call('PEXPIRE', KEYS[1], ARGV[2])
redis.call('ZADD', KEYS[2], ARGV[3], ARGV[6])
redis.call('PEXPIRE', KEYS[2], ARGV[2])
redis.call('ZADD', KEYS[3], ARGV[3], ARGV[6])
redis.call('PEXPIRE', KEYS[3], ARGV[2])
return {'RESERVED'}
";

    public RedisIdempotencyStore(IOptions<RedisIdempotencyOptions> opts, ILogger<RedisIdempotencyStore> log)
    {
        _opts = opts.Value;
        _log = log;
        _connection = new Lazy<Task<IConnectionMultiplexer>>(ConnectAsync);
    }

    private async Task<IConnectionMultiplexer> ConnectAsync()
    {
        var cfg = new ConfigurationOptions
        {
            EndPoints = { { _opts.Endpoint, _opts.Port } },
            Ssl = true,
            AbortOnConnectFail = false,
            ConnectTimeout = (int)_opts.ConnectTimeout.TotalMilliseconds,
        };
        await cfg.ConfigureForAzureWithTokenCredentialAsync(new DefaultAzureCredential());
        return await ConnectionMultiplexer.ConnectAsync(cfg);
    }

    public async Task<ReservationOutcome> TryReserveAsync(
        byte[] derivedCacheKey, byte[] bodyHash, string subject, string clientIp,
        TimeSpan ttl, CancellationToken ct = default)
    {
        var keyHex = Convert.ToHexString(derivedCacheKey);
        var bodyHashHex = Convert.ToHexString(bodyHash);
        var conn = await _connection.Value;
        var db = conn.GetDatabase();

        var keys = new RedisKey[]
        {
            $"idem:{keyHex}",
            $"idem:cap:sub:{subject}",
            $"idem:cap:ip:{clientIp}"
        };
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var args = new RedisValue[]
        {
            bodyHashHex,
            (long)ttl.TotalMilliseconds,
            nowMs,
            _opts.MaxEntriesPerSubject,
            _opts.MaxEntriesPerIp,
            keyHex
        };

        var result = (RedisResult[])(await db.ScriptEvaluateAsync(ReserveScript, keys, args))!;
        var tag = (string)result[0]!;
        return tag switch
        {
            "RESERVED"  => ReservationOutcome.Reserved(),
            "MISMATCH"  => ReservationOutcome.BodyMismatch(),
            "SUB_CAP"   => ReservationOutcome.SubjectCapExceeded(),
            "IP_CAP"    => ReservationOutcome.IpCapExceeded(),
            "HIT"       => BuildHit(result, bodyHash),
            _           => throw new InvalidOperationException($"Unknown reserve outcome: {tag}")
        };
    }

    private static ReservationOutcome BuildHit(RedisResult[] r, byte[] expectedBodyHash)
    {
        var cachedHashHex = (string)r[1]!;
        var status = int.TryParse((string)r[2]!, out var s) ? s : 0;
        var body = (string)r[3]! ?? "";
        // Constant-time compare even on Hit path — R1 invariant.
        var cachedHash = Convert.FromHexString(cachedHashHex);
        if (!CryptographicOperations.FixedTimeEquals(cachedHash, expectedBodyHash))
            return ReservationOutcome.BodyMismatch();
        return ReservationOutcome.Hit(status, body);
    }

    public async Task SaveAsync(
        byte[] derivedCacheKey, int statusCode, string body, TimeSpan ttl, CancellationToken ct = default)
    {
        var keyHex = Convert.ToHexString(derivedCacheKey);
        var conn = await _connection.Value;
        var db = conn.GetDatabase();
        await db.HashSetAsync($"idem:{keyHex}",
            new HashEntry[] { new("status", statusCode), new("body", body) });
        await db.KeyExpireAsync($"idem:{keyHex}", ttl);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated)
        {
            var c = await _connection.Value;
            await c.CloseAsync();
            c.Dispose();
        }
    }
}
