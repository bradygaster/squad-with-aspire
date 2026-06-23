using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AspireWithSquad.RateLimiting;

public static class RateLimitServiceCollectionExtensions
{
    /// <summary>
    /// Registers the auth rate-limit middleware with an <see cref="InMemoryRateLimitStore"/>.
    /// For production wire a <see cref="RedisRateLimitStore"/> via <see cref="AddRedisRateLimitStore"/>.
    /// </summary>
    public static IServiceCollection AddAuthRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .PostConfigure(opt =>
            {
                if (opt.Policies.Count == 0)
                    opt.Policies = RateLimitOptions.DefaultPolicies();
            })
            .ValidateOnStart();

        services.TryAddSingleton<RateLimitKeyDerivation>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RateLimitOptions>>().Value;
            return new RateLimitKeyDerivation(opts);
        });
        services.TryAddSingleton<IRateLimitCopyProvider, DefaultRateLimitCopyProvider>();
        services.TryAddSingleton<IRateLimitStore, InMemoryRateLimitStore>();
        return services;
    }

    /// <summary>
    /// Replaces the default in-memory store with a Redis-backed one.
    /// Caller must register <c>IConnectionMultiplexer</c> separately (e.g., via Aspire <c>AddRedisClient</c>).
    /// </summary>
    public static IServiceCollection AddRedisRateLimitStore(this IServiceCollection services)
    {
        services.RemoveAll<IRateLimitStore>();
        services.AddSingleton<IRateLimitStore, RedisRateLimitStore>();
        return services;
    }

    /// <summary>
    /// Inserts the auth rate-limit middleware. Place AFTER routing (so endpoint metadata resolves)
    /// and BEFORE anything that performs a DB lookup against the user record.
    /// </summary>
    public static IApplicationBuilder UseAuthRateLimiting(this IApplicationBuilder app)
        => app.UseMiddleware<AuthRateLimitMiddleware>();
}
