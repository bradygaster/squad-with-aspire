namespace AspireWithSquad.RateLimiting;

/// <summary>
/// Scope of a rate-limit decision. Wire-visible — must match UX contract.
/// </summary>
public enum RateLimitScope
{
    Ip,
    Account,
    Global
}

/// <summary>
/// Outcome of a single rate-limit check.
/// </summary>
/// <param name="Allowed">True if the request may proceed.</param>
/// <param name="RetryAfterSeconds">When denied, seconds the client should wait. Clamped to [1, 3600].</param>
/// <param name="Scope">The scope of the binding constraint when denied.</param>
public readonly record struct RateLimitDecision(bool Allowed, int RetryAfterSeconds, RateLimitScope Scope)
{
    public static RateLimitDecision Allow() => new(true, 0, RateLimitScope.Ip);
    public static RateLimitDecision Deny(int retryAfterSeconds, RateLimitScope scope)
        => new(false, Math.Clamp(retryAfterSeconds, 1, 3600), scope);
}

/// <summary>
/// One sliding-window rule.
/// </summary>
public sealed record RateLimitRule(int Limit, TimeSpan Window);

/// <summary>
/// Per-endpoint policy. See <c>docs/security/auth/rate-limit-enforcement.md</c> §3.
/// </summary>
public sealed class RateLimitPolicy
{
    public required string EndpointId { get; init; }
    public RateLimitRule? IpRule { get; init; }
    public RateLimitRule? AccountRule { get; init; }
    public TimeSpan? AccountCooldown { get; init; }
    public bool RequiresResponseFloor { get; init; }
    public RateLimitFailureMode FailureMode { get; init; } = RateLimitFailureMode.Closed503;
}

public enum RateLimitFailureMode
{
    /// <summary>503 Service Unavailable on store outage. Default for login/register/forgot/resend.</summary>
    Closed503,
    /// <summary>Allow the request on store outage. Audit-logged. Use only for token-bound endpoints.</summary>
    Open
}

/// <summary>
/// Top-level options bound from configuration.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    /// <summary>HMAC-SHA256 key, base64. Required. See §4.</summary>
    public string HmacKey { get; set; } = string.Empty;

    /// <summary>When true, X-Forwarded-For leftmost is honored. Set only behind a known trusted proxy (Front Door / App Gateway). §4.1.</summary>
    public bool TrustForwardedFor { get; set; }

    /// <summary>Minimum handler duration on flagged endpoints (default 250ms).</summary>
    public int ResponseFloorMs { get; set; } = 250;

    /// <summary>Random jitter added to floor (default 50ms).</summary>
    public int ResponseFloorJitterMs { get; set; } = 50;

    /// <summary>Per-endpoint policies, keyed by <see cref="RateLimitPolicy.EndpointId"/>.</summary>
    public Dictionary<string, RateLimitPolicy> Policies { get; set; } = new();

    /// <summary>Built-in v1 defaults from spec §3.</summary>
    public static Dictionary<string, RateLimitPolicy> DefaultPolicies() => new()
    {
        ["login"] = new()
        {
            EndpointId = "login",
            IpRule = new(10, TimeSpan.FromMinutes(15)),
            AccountRule = new(5, TimeSpan.FromMinutes(15)),
            RequiresResponseFloor = true,
        },
        ["register"] = new()
        {
            EndpointId = "register",
            IpRule = new(5, TimeSpan.FromHours(1)),
            RequiresResponseFloor = true,
        },
        ["verify-resend"] = new()
        {
            EndpointId = "verify-resend",
            IpRule = new(20, TimeSpan.FromHours(1)),
            AccountRule = new(5, TimeSpan.FromHours(1)),
            AccountCooldown = TimeSpan.FromSeconds(60),
            RequiresResponseFloor = true,
        },
        ["forgot-password"] = new()
        {
            EndpointId = "forgot-password",
            IpRule = new(5, TimeSpan.FromHours(1)),
            AccountRule = new(3, TimeSpan.FromHours(1)),
            AccountCooldown = TimeSpan.FromSeconds(60),
            RequiresResponseFloor = true,
        },
    };
}
