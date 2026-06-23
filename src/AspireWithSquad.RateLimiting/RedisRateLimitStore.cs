using StackExchange.Redis;

namespace AspireWithSquad.RateLimiting;

/// <summary>
/// Redis-backed sliding-window log + cooldown. Pipelined to a single round-trip per check via Lua. §6.1.
/// </summary>
/// <remarks>
/// SECURITY: keys are HMAC hex digests; raw IPs/emails never reach Redis. See §4 + §10 spec.
/// On any <see cref="RedisException"/> or timeout, throws <see cref="RateLimitStoreUnavailableException"/>
/// — middleware translates per policy <see cref="RateLimitFailureMode"/>.
/// </remarks>
public sealed class RedisRateLimitStore : IRateLimitStore
{
    private readonly IConnectionMultiplexer _redis;

    // KEYS[1] = sorted-set key, ARGV[1] = now-ms, ARGV[2] = window-ms,
    // ARGV[3] = limit, ARGV[4] = nonce, ARGV[5] = ttl-seconds.
    // Returns {allowed (1/0), retryAfterSeconds}.
    private const string SlidingWindowLua = @"
local key = KEYS[1]
local now = tonumber(ARGV[1])
local window = tonumber(ARGV[2])
local limit = tonumber(ARGV[3])
local nonce = ARGV[4]
local ttl = tonumber(ARGV[5])
redis.call('ZREMRANGEBYSCORE', key, 0, now - window)
local count = redis.call('ZCARD', key)
if count >= limit then
    local oldest = redis.call('ZRANGE', key, 0, 0, 'WITHSCORES')
    local oldestScore = tonumber(oldest[2]) or now
    local retryMs = oldestScore + window - now
    local retrySec = math.ceil(retryMs / 1000)
    if retrySec < 1 then retrySec = 1 end
    return {0, retrySec}
end
redis.call('ZADD', key, now, nonce)
redis.call('EXPIRE', key, ttl)
return {1, 0}
";

    public RedisRateLimitStore(IConnectionMultiplexer redis) { _redis = redis; }

    public async Task<RateLimitDecision> SlidingWindowAsync(
        string keyspace, string key, RateLimitRule rule, RateLimitScope scope, CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var windowMs = (long)rule.Window.TotalMilliseconds;
            var ttl = (int)Math.Ceiling(rule.Window.TotalSeconds) + 60;
            var nonce = $"{nowMs}:{Guid.NewGuid():N}";

            var result = (RedisResult[])(await db.ScriptEvaluateAsync(
                SlidingWindowLua,
                new RedisKey[] { $"rl:{keyspace}:{key}" },
                new RedisValue[] { nowMs, windowMs, rule.Limit, nonce, ttl }
            ).ConfigureAwait(false))!;

            var allowed = (int)result[0] == 1;
            if (allowed) return RateLimitDecision.Allow();
            return RateLimitDecision.Deny((int)result[1], scope);
        }
        catch (RedisException ex)
        {
            throw new RateLimitStoreUnavailableException("Redis unavailable for sliding-window check.", ex);
        }
        catch (TimeoutException ex)
        {
            throw new RateLimitStoreUnavailableException("Redis timeout for sliding-window check.", ex);
        }
    }

    public async Task<RateLimitDecision> CooldownAsync(
        string keyspace, string key, TimeSpan cooldown, RateLimitScope scope, CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            var compositeKey = (RedisKey)$"rl:{keyspace}:{key}:cd";
            var ok = await db.StringSetAsync(compositeKey, "1", cooldown, when: When.NotExists)
                .ConfigureAwait(false);
            if (ok) return RateLimitDecision.Allow();
            var ttl = await db.KeyTimeToLiveAsync(compositeKey).ConfigureAwait(false);
            var retrySec = (int)Math.Max(1, Math.Ceiling((ttl ?? cooldown).TotalSeconds));
            return RateLimitDecision.Deny(retrySec, scope);
        }
        catch (RedisException ex)
        {
            throw new RateLimitStoreUnavailableException("Redis unavailable for cooldown check.", ex);
        }
        catch (TimeoutException ex)
        {
            throw new RateLimitStoreUnavailableException("Redis timeout for cooldown check.", ex);
        }
    }
}
