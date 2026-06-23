using System.Collections.Concurrent;

namespace AspireWithSquad.RateLimiting;

/// <summary>
/// In-process sliding-window log + cooldown store. Dev/test only. NOT cluster-safe.
/// See spec §6.2 — production must use Redis.
/// </summary>
public sealed class InMemoryRateLimitStore : IRateLimitStore
{
    private readonly ConcurrentDictionary<string, Window> _windows = new();
    private readonly ConcurrentDictionary<string, long> _cooldowns = new();
    private readonly Func<DateTimeOffset> _now;

    public InMemoryRateLimitStore() : this(() => DateTimeOffset.UtcNow) { }
    public InMemoryRateLimitStore(Func<DateTimeOffset> clock) { _now = clock; }

    private sealed class Window
    {
        public readonly object Sync = new();
        public readonly LinkedList<long> Entries = new();
    }

    public Task<RateLimitDecision> SlidingWindowAsync(
        string keyspace, string key, RateLimitRule rule, RateLimitScope scope, CancellationToken ct)
    {
        var compositeKey = $"{keyspace}:{key}";
        var nowMs = _now().ToUnixTimeMilliseconds();
        var windowMs = (long)rule.Window.TotalMilliseconds;
        var window = _windows.GetOrAdd(compositeKey, _ => new Window());

        lock (window.Sync)
        {
            // ZREMRANGEBYSCORE older than (now - window).
            while (window.Entries.First is { } first && first.Value < nowMs - windowMs)
                window.Entries.RemoveFirst();

            if (window.Entries.Count >= rule.Limit)
            {
                var oldest = window.Entries.First!.Value;
                var retryMs = oldest + windowMs - nowMs;
                var retrySec = (int)Math.Max(1, Math.Ceiling(retryMs / 1000.0));
                return Task.FromResult(RateLimitDecision.Deny(retrySec, scope));
            }

            window.Entries.AddLast(nowMs);
            return Task.FromResult(RateLimitDecision.Allow());
        }
    }

    public Task<RateLimitDecision> CooldownAsync(
        string keyspace, string key, TimeSpan cooldown, RateLimitScope scope, CancellationToken ct)
    {
        var compositeKey = $"{keyspace}:{key}";
        var nowMs = _now().ToUnixTimeMilliseconds();
        var expiryMs = nowMs + (long)cooldown.TotalMilliseconds;

        // Atomic compare-and-swap: only set if absent or expired.
        while (true)
        {
            if (_cooldowns.TryGetValue(compositeKey, out var existing))
            {
                if (existing > nowMs)
                {
                    var retrySec = (int)Math.Max(1, Math.Ceiling((existing - nowMs) / 1000.0));
                    return Task.FromResult(RateLimitDecision.Deny(retrySec, scope));
                }
                if (_cooldowns.TryUpdate(compositeKey, expiryMs, existing))
                    return Task.FromResult(RateLimitDecision.Allow());
            }
            else if (_cooldowns.TryAdd(compositeKey, expiryMs))
            {
                return Task.FromResult(RateLimitDecision.Allow());
            }
        }
    }
}
