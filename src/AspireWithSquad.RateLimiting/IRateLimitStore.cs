namespace AspireWithSquad.RateLimiting;

/// <summary>
/// Abstraction over the rate-limit storage primitive. Implementations must be pipelined
/// to keep round-trip count to 1 per limit check. See spec §6.
/// </summary>
public interface IRateLimitStore
{
    /// <summary>
    /// Atomically: remove window-expired entries, count remaining, and add the new entry if under limit.
    /// </summary>
    /// <returns>
    /// <see cref="RateLimitDecision.Allow"/> if under limit; otherwise <see cref="RateLimitDecision.Deny"/>
    /// with <c>retryAfterSeconds = ceil((oldestScoreInWindow + windowMs - now) / 1000)</c>.
    /// </returns>
    Task<RateLimitDecision> SlidingWindowAsync(
        string keyspace,
        string key,
        RateLimitRule rule,
        RateLimitScope scope,
        CancellationToken ct);

    /// <summary>
    /// <c>SET NX EX</c>. If the key already exists, returns Deny with retry-after = TTL.
    /// </summary>
    Task<RateLimitDecision> CooldownAsync(
        string keyspace,
        string key,
        TimeSpan cooldown,
        RateLimitScope scope,
        CancellationToken ct);
}

/// <summary>
/// Thrown when the rate-limit store is unreachable and the policy requires fail-closed-503.
/// Middleware translates this into HTTP 503 with Retry-After: 30.
/// </summary>
public sealed class RateLimitStoreUnavailableException : Exception
{
    public RateLimitStoreUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
