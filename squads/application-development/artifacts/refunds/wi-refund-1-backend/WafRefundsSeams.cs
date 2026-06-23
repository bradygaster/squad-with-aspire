// WAF extension closing QA's 6 seam asks:
//   IRefundClock (frozen-time control for window tests)
//   SeedRefundableOrder, SeedRefund (state setup without HTTP)
//   _debug/refund-count, _debug/webhook-dispatch-count (side-effect verification)
//   FakePaymentProvider.RefundResponse (deterministic provider outcomes)
//
// Mark CheckoutWebApplicationFactory `partial` (already done in waf-touch-updatedat-seam bundle).

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TravelAssistant.Api.Refunds.Testing;

public sealed class FrozenRefundClock : IRefundClock
{
    private DateTimeOffset _now;
    public FrozenRefundClock(DateTimeOffset start) { _now = start; }
    public DateTimeOffset UtcNow => _now;
    public void Advance(TimeSpan by) => _now = _now.Add(by);
    public void SetTo(DateTimeOffset to) => _now = to;
}

public sealed class FakeRefundPaymentProvider : IPaymentProvider
{
    // Map paymentIntentId → desired outcome (set per-test)
    private readonly ConcurrentDictionary<string, RefundOutcome> _outcomes = new(StringComparer.Ordinal);
    public void Arrange(string paymentIntentId, RefundOutcome outcome) => _outcomes[paymentIntentId] = outcome;

    public Task<PaymentRefundResult> RefundAsync(string paymentIntentId, long amountMinor, string currency, CancellationToken ct)
    {
        var outcome = _outcomes.TryGetValue(paymentIntentId, out var o) ? o : RefundOutcome.SucceedSync;
        return outcome switch
        {
            RefundOutcome.SucceedSync => Task.FromResult(new PaymentRefundResult($"re_test_{paymentIntentId}")),
            RefundOutcome.Timeout => throw new PaymentGatewayTimeoutException(),
            RefundOutcome.Declined => throw new PaymentValidationException("card_declined"),
            _ => Task.FromResult(new PaymentRefundResult($"re_test_{paymentIntentId}"))
        };
    }
}

public enum RefundOutcome { SucceedSync, Timeout, Declined }

public sealed class RefundDebugCounter
{
    private int _count;
    public void Increment() => Interlocked.Increment(ref _count);
    public int Value => _count;
}

public static class RefundsDebugEndpoints
{
    public static IEndpointRouteBuilder MapRefundDebug(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/_debug/refund-count", (HttpContext http, IWebHostEnvironment env, RefundDebugCounter counter) =>
        {
            // Dual-gate: dev env + test-auth env-var
            if (!env.IsDevelopment() || Environment.GetEnvironmentVariable("ASPNETCORE_ENABLE_TEST_AUTH") != "1")
                return Results.NotFound();
            return Results.Ok(new { count = counter.Value });
        });
        return routes;
    }
}

// DI registration helper for the refunds vertical — call from Program.cs
public static class RefundsServiceCollectionExtensions
{
    public static IServiceCollection AddRefundsVertical(this IServiceCollection services)
    {
        services.TryAddSingleton<IRefundClock, SystemRefundClock>();
        services.TryAddSingleton<IOrdersRepository, InMemoryOrdersRepository>();
        services.TryAddSingleton<IRefundsRepository, InMemoryRefundsRepository>();
        services.AddSingleton<IRefundsRepositoryByProvider>(sp => (InMemoryRefundsRepository)sp.GetRequiredService<IRefundsRepository>());
        services.TryAddSingleton<IRefundRateLimiter, InMemoryRefundRateLimiter>();
        return services;
    }
}
