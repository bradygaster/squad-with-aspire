// Add to Program.cs (or CheckoutServiceCollectionExtensions.cs) after the existing
// in-memory store registration. Replaces it conditionally.

using TravelAssistant.Api.Checkout.Auth;
using TravelAssistant.Api.Checkout.Idempotency;

public static class CheckoutDependencyInjection
{
    public static IServiceCollection AddCheckoutInfrastructure(
        this IServiceCollection services, IConfiguration cfg, IHostEnvironment env)
    {
        // Test auth seam — Development only, or explicit opt-in
        var enableTestAuth = env.IsDevelopment() ||
            Environment.GetEnvironmentVariable("ASPNETCORE_ENABLE_TEST_AUTH") == "1";
        if (enableTestAuth)
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
                           TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        }

        // Client IP resolver — honors XFF only from trusted proxies (Front Door / Container Apps).
        var ipOpts = cfg.GetSection("Checkout:Network").Get<ClientIpResolverOptions>()
                     ?? new ClientIpResolverOptions();
        services.AddSingleton(ipOpts);
        services.AddSingleton<IClientIpResolver, ClientIpResolver>();

        // Idempotency backend selector
        var backend = cfg["Checkout:IdempotencyBackend"] ?? "memory";
        if (string.Equals(backend, "redis", StringComparison.OrdinalIgnoreCase))
        {
            services.Configure<RedisIdempotencyOptions>(cfg.GetSection("Checkout:Redis"));
            services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
        }
        else
        {
            services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        }

        // Per-sub/per-IP entry caps (T13) — config-driven
        services.Configure<IdempotencyOptions>(cfg.GetSection("Checkout:Idempotency"));

        // Health check — review-deployment-squad's canary gate requires this
        var hc = services.AddHealthChecks();
        if (string.Equals(backend, "redis", StringComparison.OrdinalIgnoreCase))
        {
            // NuGet: AspNetCore.HealthChecks.Redis
            // hc.AddRedis(redisConnectionString, name: "redis-idempotency", tags: new[] { "ready" });
            // Connection-string variant requires keys; for Entra-MI use a custom check that
            // does PING via the existing ConnectionMultiplexer singleton.
        }

        return services;
    }
}

// appsettings.json example for staging/prod:
// {
//   "Checkout": {
//     "IdempotencyBackend": "redis",
//     "Redis": { "Endpoint": "checkout-cache.redis.cache.windows.net", "Port": 6380,
//                "MaxEntriesPerSubject": 1000, "MaxEntriesPerIp": 5000 },
//     "Idempotency": { "MaxEntriesPerSubject": 1000, "MaxEntriesPerIp": 5000 },
//     "Network": { "TrustedProxyCidrs": [ "10.0.0.0/8", "100.64.0.0/10" ] }
//   }
// }
