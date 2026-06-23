using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AspireWithSquad.RateLimiting;

/// <summary>
/// 429 JSON body — wire-bound to UX contract. Header <c>Retry-After</c> mirrors <see cref="RetryAfterSeconds"/>.
/// </summary>
public sealed class RateLimitErrorBody
{
    [JsonPropertyName("code")] public string Code { get; init; } = "RATE_LIMITED";
    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
    [JsonPropertyName("retryAfterSeconds")] public int RetryAfterSeconds { get; init; }
    [JsonPropertyName("scope")] public string Scope { get; init; } = "ip";
}

/// <summary>
/// Pluggable per-scope copy. UX owns the strings; this is the seam.
/// </summary>
public interface IRateLimitCopyProvider
{
    string MessageFor(string endpointId, RateLimitScope scope);
}

internal sealed class DefaultRateLimitCopyProvider : IRateLimitCopyProvider
{
    public string MessageFor(string endpointId, RateLimitScope scope) => scope switch
    {
        RateLimitScope.Account => "Too many attempts on this account. Try again shortly.",
        RateLimitScope.Ip => "Too many attempts from your network. Try again shortly.",
        RateLimitScope.Global => "Authentication is temporarily unavailable. Try again shortly.",
        _ => "Rate limit exceeded."
    };
}

/// <summary>
/// Auth rate-limit pipeline. Runs sliding-window + (optional) cooldown checks against
/// <see cref="IRateLimitStore"/>, applies precedence (longest retry-after wins per §3),
/// enforces a 250±50ms response floor on flagged endpoints (§5.2), and serializes the
/// 429 wire format. On store outage with <see cref="RateLimitFailureMode.Closed503"/>,
/// emits <c>503 Retry-After: 30</c>.
/// </summary>
/// <remarks>
/// MIDDLEWARE ORDERING: must run BEFORE anything that touches the user record (spec §11.1).
/// The DB lookup is itself an enumeration oracle.
/// </remarks>
public sealed class AuthRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRateLimitStore _store;
    private readonly RateLimitKeyDerivation _keys;
    private readonly RateLimitOptions _options;
    private readonly IRateLimitCopyProvider _copy;
    private readonly ILogger<AuthRateLimitMiddleware> _log;

    public AuthRateLimitMiddleware(
        RequestDelegate next,
        IRateLimitStore store,
        RateLimitKeyDerivation keys,
        IOptions<RateLimitOptions> options,
        IRateLimitCopyProvider copy,
        ILogger<AuthRateLimitMiddleware> log)
    {
        _next = next;
        _store = store;
        _keys = keys;
        _options = options.Value;
        _copy = copy;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var policy = ResolvePolicy(context);
        if (policy is null) { await _next(context).ConfigureAwait(false); return; }

        // Email is read from the request without ever hitting the DB. The handler
        // is responsible for the actual auth check; we run constant-time HMAC keying.
        var email = await PeekEmailAsync(context).ConfigureAwait(false);
        var ipAddr = ClientIpResolver.Resolve(context, _options.TrustForwardedFor);

        var sw = Stopwatch.StartNew();
        try
        {
            var decision = await EvaluateAsync(policy, ipAddr, email, context.RequestAborted).ConfigureAwait(false);
            if (!decision.Allowed)
            {
                await Write429Async(context, policy, decision).ConfigureAwait(false);
                return;
            }
            await _next(context).ConfigureAwait(false);
        }
        catch (RateLimitStoreUnavailableException ex)
        {
            if (policy.FailureMode == RateLimitFailureMode.Closed503)
            {
                _log.LogError(ex, "Rate-limit store unavailable on {Endpoint}; failing closed-503.", policy.EndpointId);
                await Write503Async(context).ConfigureAwait(false);
                return;
            }
            _log.LogWarning(ex, "Rate-limit store unavailable on {Endpoint}; failing open (audit).", policy.EndpointId);
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            if (policy.RequiresResponseFloor)
            {
                var floor = TimeSpan.FromMilliseconds(
                    _options.ResponseFloorMs + Random.Shared.Next(0, Math.Max(1, _options.ResponseFloorJitterMs)));
                var remaining = floor - sw.Elapsed;
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining).ConfigureAwait(false);
            }
        }
    }

    private RateLimitPolicy? ResolvePolicy(HttpContext context)
    {
        var endpointId = context.GetEndpoint()?.Metadata.GetMetadata<RateLimitEndpointAttribute>()?.EndpointId;
        if (endpointId is null) return null;
        return _options.Policies.GetValueOrDefault(endpointId);
    }

    private async Task<RateLimitDecision> EvaluateAsync(
        RateLimitPolicy policy, System.Net.IPAddress ip, string? email, CancellationToken ct)
    {
        var ipKey = _keys.IpKey(ip);
        // Account-scope HMAC is computed unconditionally (§5.1 constant-time). When the email is missing,
        // we still synthesize a key from the empty string so timing distribution is preserved.
        var accountKey = _keys.AccountKey(email ?? string.Empty);

        var ipDecision = policy.IpRule is { } ipRule
            ? await _store.SlidingWindowAsync(policy.EndpointId + ":ip", ipKey, ipRule, RateLimitScope.Ip, ct).ConfigureAwait(false)
            : RateLimitDecision.Allow();

        var acctDecision = policy.AccountRule is { } acctRule
            ? await _store.SlidingWindowAsync(policy.EndpointId + ":acct", accountKey, acctRule, RateLimitScope.Account, ct).ConfigureAwait(false)
            : RateLimitDecision.Allow();

        var cooldownDecision = policy.AccountCooldown is { } cd
            ? await _store.CooldownAsync(policy.EndpointId + ":acct", accountKey, cd, RateLimitScope.Account, ct).ConfigureAwait(false)
            : RateLimitDecision.Allow();

        // Precedence: longest retryAfterSeconds wins; its scope is reported. §3.
        var denials = new[] { ipDecision, acctDecision, cooldownDecision }
            .Where(d => !d.Allowed)
            .ToArray();
        if (denials.Length == 0) return RateLimitDecision.Allow();

        var longest = denials[0];
        foreach (var d in denials)
            if (d.RetryAfterSeconds > longest.RetryAfterSeconds) longest = d;
        return longest;
    }

    private async Task Write429Async(HttpContext context, RateLimitPolicy policy, RateLimitDecision decision)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers["Retry-After"] = decision.RetryAfterSeconds.ToString();
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.ContentType = "application/json";
        var body = new RateLimitErrorBody
        {
            Message = _copy.MessageFor(policy.EndpointId, decision.Scope),
            RetryAfterSeconds = decision.RetryAfterSeconds,
            Scope = decision.Scope.ToString().ToLowerInvariant(),
        };
        await JsonSerializer.SerializeAsync(context.Response.Body, body).ConfigureAwait(false);
    }

    private static async Task Write503Async(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers["Retry-After"] = "30";
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            code = "AUTH_TEMPORARILY_UNAVAILABLE",
            message = "Authentication is temporarily unavailable. Try again shortly.",
            retryAfterSeconds = 30,
        }).ConfigureAwait(false);
    }

    private static async Task<string?> PeekEmailAsync(HttpContext context)
    {
        // We only buffer JSON bodies; non-JSON requests fall through and account-scope keys
        // synthesize from empty string (still constant-time).
        if (!string.Equals(context.Request.ContentType?.Split(';')[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
            return null;
        context.Request.EnableBuffering();
        try
        {
            using var doc = await JsonDocument.ParseAsync(context.Request.Body, default, context.RequestAborted).ConfigureAwait(false);
            string? email = null;
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("email", out var emailEl) &&
                emailEl.ValueKind == JsonValueKind.String)
            {
                email = emailEl.GetString();
            }
            return email;
        }
        catch (JsonException) { return null; }
        finally
        {
            context.Request.Body.Position = 0;
        }
    }
}

/// <summary>
/// Tag endpoints (minimal-API or MVC) with a policy id resolved from <see cref="RateLimitOptions.Policies"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RateLimitEndpointAttribute : Attribute
{
    public string EndpointId { get; }
    public RateLimitEndpointAttribute(string endpointId) { EndpointId = endpointId; }
}
