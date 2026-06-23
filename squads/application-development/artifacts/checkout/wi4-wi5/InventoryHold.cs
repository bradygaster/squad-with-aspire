// WI-4: Inventory hold with atomic reserve/release/expire (closes issue #50).
// Race window in #50: concurrent /checkout/session calls could over-sell because
// stock decrement + hold insert weren't atomic. Fix: single Redis Lua script
// reserves N units against per-SKU available counter and writes hold entry,
// or rolls back entirely. Hold TTL = 15min, matches /confirm idempotency TTL.

namespace TravelAssistant.Api.Checkout;

using System.Text.Json;
using StackExchange.Redis;

public interface IInventoryHoldStore
{
    Task<HoldResult> TryReserveAsync(string sku, int qty, string holdId, string scope, TimeSpan ttl, CancellationToken ct);
    Task ReleaseAsync(string holdId, CancellationToken ct);
    Task<bool> CommitAsync(string holdId, CancellationToken ct);
}

public enum HoldOutcome { Reserved, InsufficientStock, AlreadyHeld, Expired }
public sealed record HoldResult(HoldOutcome Outcome, int Available, int Requested);

public sealed class RedisInventoryHoldStore : IInventoryHoldStore
{
    private readonly IDatabase _db;
    private const string ReserveScript = @"
        local stockKey = KEYS[1]
        local holdKey  = KEYS[2]
        local capZset  = KEYS[3]
        local qty      = tonumber(ARGV[1])
        local ttlSec   = tonumber(ARGV[2])
        local holdJson = ARGV[3]
        local scope    = ARGV[4]
        local now      = tonumber(ARGV[5])

        if redis.call('EXISTS', holdKey) == 1 then
            return {-2, 0}
        end
        local avail = tonumber(redis.call('GET', stockKey) or '0')
        if avail < qty then
            return {-1, avail}
        end
        redis.call('DECRBY', stockKey, qty)
        redis.call('SET', holdKey, holdJson, 'EX', ttlSec)
        redis.call('ZADD', capZset, now, holdKey)
        redis.call('ZREMRANGEBYSCORE', capZset, '-inf', now - (ttlSec * 1000))
        return {1, avail - qty}
    ";

    public RedisInventoryHoldStore(IConnectionMultiplexer mux) => _db = mux.GetDatabase();

    public async Task<HoldResult> TryReserveAsync(string sku, int qty, string holdId, string scope, TimeSpan ttl, CancellationToken ct)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { sku, qty, scope, createdAt = DateTimeOffset.UtcNow });
        var result = (RedisResult[])await _db.ScriptEvaluateAsync(
            ReserveScript,
            new RedisKey[] { $"inv:stock:{sku}", $"inv:hold:{holdId}", $"inv:cap:{scope}" },
            new RedisValue[] { qty, (long)ttl.TotalSeconds, payload, scope, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() })!;
        var status = (int)result[0];
        var avail = (int)result[1];
        return status switch
        {
            1  => new HoldResult(HoldOutcome.Reserved, avail, qty),
            -1 => new HoldResult(HoldOutcome.InsufficientStock, avail, qty),
            -2 => new HoldResult(HoldOutcome.AlreadyHeld, avail, qty),
            _  => throw new InvalidOperationException($"Unexpected reserve status {status}")
        };
    }

    public async Task ReleaseAsync(string holdId, CancellationToken ct)
    {
        // Compensating release: read hold payload, return qty to stock, delete hold.
        // Idempotent — if hold is already gone (expired or released), no-op.
        const string releaseScript = @"
            local holdJson = redis.call('GET', KEYS[1])
            if not holdJson then return 0 end
            local hold = cjson.decode(holdJson)
            redis.call('INCRBY', 'inv:stock:' .. hold.sku, hold.qty)
            redis.call('DEL', KEYS[1])
            return 1
        ";
        await _db.ScriptEvaluateAsync(releaseScript, new RedisKey[] { $"inv:hold:{holdId}" });
    }

    public async Task<bool> CommitAsync(string holdId, CancellationToken ct)
    {
        // Commit = delete hold without returning stock (sale completed).
        return await _db.KeyDeleteAsync($"inv:hold:{holdId}");
    }
}
