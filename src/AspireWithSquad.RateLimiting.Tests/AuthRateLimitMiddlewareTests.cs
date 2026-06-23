using System.Net;
using System.Net.Http.Json;
using System.Text;
using AspireWithSquad.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AspireWithSquad.RateLimiting.Tests;

public class AuthRateLimitMiddlewareTests
{
    private static string MakeHmacKey() => Convert.ToBase64String(new byte[32]);

    private static IHost BuildHost(Action<RateLimitOptions>? configure = null, Action<IServiceCollection>? services = null)
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(s =>
                {
                    var cfg = new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["RateLimit:HmacKey"] = MakeHmacKey(),
                        }).Build();
                    s.AddAuthRateLimiting(cfg);
                    s.Configure<RateLimitOptions>(opt => configure?.Invoke(opt));
                    s.AddRouting();
                    services?.Invoke(s);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthRateLimiting();
                    app.UseEndpoints(e =>
                    {
                        e.MapPost("/api/auth/login", () => Results.Ok(new { ok = true }))
                            .WithMetadata(new RateLimitEndpointAttribute("login"));
                        e.MapPost("/api/auth/verify/resend", () => Results.Ok(new { ok = true }))
                            .WithMetadata(new RateLimitEndpointAttribute("verify-resend"));
                        e.MapPost("/no-policy", () => Results.Ok(new { ok = true }));
                    });
                });
            });
        var host = builder.Start();
        return host;
    }

    private static StringContent Json(object body) =>
        new(System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    [Fact]
    public async Task UnconfiguredEndpoint_BypassesLimiter()
    {
        using var host = BuildHost();
        var client = host.GetTestClient();
        for (var i = 0; i < 50; i++)
        {
            var r = await client.PostAsync("/no-policy", Json(new { }));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }
    }

    [Fact]
    public async Task IpLimit_Trips_Returns429_WithRetryAfterAndScope()
    {
        // Tight policy: 2 / 1min for fast deterministic test.
        using var host = BuildHost(opt =>
        {
            opt.Policies["login"] = new RateLimitPolicy
            {
                EndpointId = "login",
                IpRule = new RateLimitRule(2, TimeSpan.FromMinutes(1)),
                RequiresResponseFloor = false,
            };
        });
        var client = host.GetTestClient();

        var first = await client.PostAsync("/api/auth/login", Json(new { email = "a@b.test" }));
        var second = await client.PostAsync("/api/auth/login", Json(new { email = "a@b.test" }));
        var third = await client.PostAsync("/api/auth/login", Json(new { email = "a@b.test" }));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((HttpStatusCode)429, third.StatusCode);
        Assert.True(third.Headers.Contains("Retry-After"));
        var body = await third.Content.ReadFromJsonAsync<RateLimitErrorBody>();
        Assert.NotNull(body);
        Assert.Equal("RATE_LIMITED", body!.Code);
        Assert.Equal("ip", body.Scope);
        Assert.InRange(body.RetryAfterSeconds, 1, 3600);
        // Header & body must agree (§1).
        var header = third.Headers.GetValues("Retry-After").First();
        Assert.Equal(body.RetryAfterSeconds.ToString(), header);
    }

    [Fact]
    public async Task AccountLimit_Reports_ScopeAccount_Even_For_Nonexistent_Email()
    {
        // Account-scope key derives from email HMAC w/o DB lookup; existence-agnostic by construction (§5).
        using var host = BuildHost(opt =>
        {
            opt.Policies["login"] = new RateLimitPolicy
            {
                EndpointId = "login",
                IpRule = new RateLimitRule(1000, TimeSpan.FromMinutes(1)),
                AccountRule = new RateLimitRule(2, TimeSpan.FromMinutes(1)),
                RequiresResponseFloor = false,
            };
        });
        var client = host.GetTestClient();

        await client.PostAsync("/api/auth/login", Json(new { email = "nobody@test.invalid" }));
        await client.PostAsync("/api/auth/login", Json(new { email = "nobody@test.invalid" }));
        var third = await client.PostAsync("/api/auth/login", Json(new { email = "nobody@test.invalid" }));

        Assert.Equal((HttpStatusCode)429, third.StatusCode);
        var body = await third.Content.ReadFromJsonAsync<RateLimitErrorBody>();
        Assert.Equal("account", body!.Scope);
    }

    [Fact]
    public async Task EmailCaseAndWhitespace_AreNormalized_ForAccountKey()
    {
        using var host = BuildHost(opt =>
        {
            opt.Policies["login"] = new RateLimitPolicy
            {
                EndpointId = "login",
                IpRule = new RateLimitRule(1000, TimeSpan.FromMinutes(1)),
                AccountRule = new RateLimitRule(2, TimeSpan.FromMinutes(1)),
                RequiresResponseFloor = false,
            };
        });
        var client = host.GetTestClient();

        await client.PostAsync("/api/auth/login", Json(new { email = "USER@Example.com" }));
        await client.PostAsync("/api/auth/login", Json(new { email = "  user@example.com  " }));
        // Same normalized email → 3rd request from same logical account trips even with different casing.
        var third = await client.PostAsync("/api/auth/login", Json(new { email = "user@example.com" }));

        Assert.Equal((HttpStatusCode)429, third.StatusCode);
    }

    [Fact]
    public async Task Cooldown_Atomicity_OneSuccess_RestDeny()
    {
        // 100 concurrent verify-resend with same email → exactly 1 success.
        using var host = BuildHost(opt =>
        {
            opt.Policies["verify-resend"] = new RateLimitPolicy
            {
                EndpointId = "verify-resend",
                IpRule = new RateLimitRule(1000, TimeSpan.FromMinutes(5)),
                AccountRule = new RateLimitRule(100, TimeSpan.FromMinutes(5)),
                AccountCooldown = TimeSpan.FromSeconds(60),
                RequiresResponseFloor = false,
            };
        });
        var client = host.GetTestClient();

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => client.PostAsync("/api/auth/verify/resend", Json(new { email = "race@b.test" })))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        var ok = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        var denied = results.Count(r => (int)r.StatusCode == 429);
        Assert.Equal(1, ok);
        Assert.Equal(99, denied);
        var anyDenied = results.First(r => (int)r.StatusCode == 429);
        var body = await anyDenied.Content.ReadFromJsonAsync<RateLimitErrorBody>();
        Assert.Equal("account", body!.Scope);
        Assert.InRange(body.RetryAfterSeconds, 59, 60);
    }

    [Fact]
    public async Task RedisOutage_FailClosed_Returns503_WithRetryAfter30()
    {
        using var host = BuildHost(
            opt => opt.Policies["login"] = new RateLimitPolicy
            {
                EndpointId = "login",
                IpRule = new RateLimitRule(10, TimeSpan.FromMinutes(1)),
                RequiresResponseFloor = false,
                FailureMode = RateLimitFailureMode.Closed503,
            },
            s =>
            {
                s.RemoveAll<IRateLimitStore>();
                s.AddSingleton<IRateLimitStore, AlwaysFailingStore>();
            });
        var client = host.GetTestClient();
        var r = await client.PostAsync("/api/auth/login", Json(new { email = "a@b.test" }));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal("30", r.Headers.GetValues("Retry-After").First());
    }

    [Fact]
    public async Task Precedence_LongestRetryAfter_Wins()
    {
        // Build a store that returns IP=900s retry-after and Account=300s; longest (IP) must be reported.
        using var host = BuildHost(
            opt => opt.Policies["login"] = new RateLimitPolicy
            {
                EndpointId = "login",
                IpRule = new RateLimitRule(1, TimeSpan.FromHours(1)),
                AccountRule = new RateLimitRule(1, TimeSpan.FromHours(1)),
                RequiresResponseFloor = false,
            },
            s =>
            {
                s.RemoveAll<IRateLimitStore>();
                s.AddSingleton<IRateLimitStore, FixedDecisionStore>();
            });
        var client = host.GetTestClient();
        var r = await client.PostAsync("/api/auth/login", Json(new { email = "a@b.test" }));
        Assert.Equal((HttpStatusCode)429, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<RateLimitErrorBody>();
        Assert.Equal(900, body!.RetryAfterSeconds);
        Assert.Equal("ip", body.Scope);
    }

    [Fact]
    public void IpKey_IPv6_Uses64PrefixNormalization()
    {
        var opts = new RateLimitOptions { HmacKey = MakeHmacKey() };
        var kd = new RateLimitKeyDerivation(opts);
        var a = IPAddress.Parse("2001:db8:1::dead:beef");
        var b = IPAddress.Parse("2001:db8:1::cafe:f00d");
        Assert.Equal(kd.IpKey(a), kd.IpKey(b)); // same /64
        var c = IPAddress.Parse("2001:db8:2::1");
        Assert.NotEqual(kd.IpKey(a), kd.IpKey(c)); // different /64
    }

    [Fact]
    public void RetryAfter_IsClampedTo3600()
    {
        var deny = RateLimitDecision.Deny(99999, RateLimitScope.Ip);
        Assert.Equal(3600, deny.RetryAfterSeconds);
    }

    // ---- Test doubles ----
    private sealed class AlwaysFailingStore : IRateLimitStore
    {
        public Task<RateLimitDecision> SlidingWindowAsync(string ks, string k, RateLimitRule r, RateLimitScope s, CancellationToken c)
            => throw new RateLimitStoreUnavailableException("simulated outage");
        public Task<RateLimitDecision> CooldownAsync(string ks, string k, TimeSpan cd, RateLimitScope s, CancellationToken c)
            => throw new RateLimitStoreUnavailableException("simulated outage");
    }

    private sealed class FixedDecisionStore : IRateLimitStore
    {
        public Task<RateLimitDecision> SlidingWindowAsync(string ks, string k, RateLimitRule r, RateLimitScope s, CancellationToken c)
            => Task.FromResult(s == RateLimitScope.Ip
                ? RateLimitDecision.Deny(900, RateLimitScope.Ip)
                : RateLimitDecision.Deny(300, RateLimitScope.Account));
        public Task<RateLimitDecision> CooldownAsync(string ks, string k, TimeSpan cd, RateLimitScope s, CancellationToken c)
            => Task.FromResult(RateLimitDecision.Allow());
    }
}
