// File: src/TravelAssistant.Api/Checkout/DependencyInjection/CheckoutIdempotencyServiceCollectionExtensions.cs
//
// WI-6 DI reconciliation. Pins the config-key contract between azure-infrastructure-squad's
// containerApp.redis-wiring.patch.bicep env-vars and application-development-squad's
// RedisIdempotencyStore (shipped in wi1c-redis-testauth bundle).
//
// Env-vars set by containerApp.bicep map to .NET config keys via the standard "__" -> ":" convention:
//   Checkout__IdempotencyBackend   -> Checkout:IdempotencyBackend   ("memory" | "redis")
//   Checkout__Redis__Endpoint      -> Checkout:Redis:Endpoint       (e.g. "ta-redis-prod.redis.cache.windows.net:6380")
//   AZURE_CLIENT_ID                -> picked up by DefaultAzureCredential for the user-assigned MI
//
// Dev defaults to memory. Staging/Prod default to redis (set via bicepparam, not in code).
// Health check is wired only when backend == redis, with tags ["ready","redis"] so /health/ready
// includes it but /health/live excludes it (matches azure-infra's RedisHealthCheck.spec.cs gating).

using System;
using Azure.Identity;
using Microsoft.Azure.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using TravelAssistant.Api.Checkout.Idempotency;
using TravelAssistant.Api.HealthChecks;

namespace TravelAssistant.Api.Checkout.DependencyInjection;

public static class CheckoutIdempotencyServiceCollectionExtensions
{
    private const string BackendKey  = "Checkout:IdempotencyBackend";
    private const string EndpointKey = "Checkout:Redis:Endpoint";

    public static IServiceCollection AddCheckoutIdempotency(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var backend = (configuration[BackendKey] ?? "memory").Trim().ToLowerInvariant();

        services.AddSingleton<IIdempotencyKeyDeriver, IdempotencyKeyDeriver>();
        services.Configure<IdempotencyOptions>(configuration.GetSection("Checkout:Idempotency"));

        switch (backend)
        {
            case "memory":
                services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
                break;

            case "redis":
                var endpoint = configuration[EndpointKey];
                if (string.IsNullOrWhiteSpace(endpoint))
                    throw new InvalidOperationException(
                        $"Checkout:IdempotencyBackend=redis requires {EndpointKey} (set by containerApp.bicep env-var Checkout__Redis__Endpoint).");

                services.AddSingleton<IConnectionMultiplexer>(_ =>
                {
                    var options = ConfigurationOptions.Parse(endpoint);
                    options.AbortOnConnectFail = false;
                    options.Ssl = true;
                    options.SslProtocols = System.Security.Authentication.SslProtocols.Tls12;
                    options.ConnectTimeout = 5_000;
                    options.SyncTimeout    = 2_000;

                    // Entra ID auth via user-assigned MI; AZURE_CLIENT_ID picks the right identity
                    // when more than one MI is attached to the Container App.
                    options.ConfigureForAzureWithTokenCredentialAsync(new DefaultAzureCredential())
                           .GetAwaiter().GetResult();

                    return ConnectionMultiplexer.Connect(options);
                });

                services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();

                // Custom MI-aware health check from azure-infra's RedisHealthCheck.spec.cs.
                // Degraded (not Unhealthy) on PING timeout — Standard tier ~30s failover window
                // must not flap liveness or restart the canary.
                services.AddHealthChecks()
                        .AddCheck<RedisHealthCheck>(
                            name: "redis-idempotency",
                            failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded,
                            tags: new[] { "ready", "redis" });
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown {BackendKey} value '{backend}'. Expected 'memory' or 'redis'.");
        }

        return services;
    }
}
