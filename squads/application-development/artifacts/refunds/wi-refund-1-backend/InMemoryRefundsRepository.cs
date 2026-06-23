// In-memory impls for test fixture (CheckoutWebApplicationFactory extension).
// QA's RefundIntegrationTests + 6 gate tests expect these seams:
//   SeedRefundableOrder(orderId, sub, confirmedAt, amount, currency)
//   SeedRefund(refundId, orderId, status, providerRefundId)
//   _debug/refund-count (singleton counter, dev+test-auth gated)

using System.Collections.Concurrent;

namespace TravelAssistant.Api.Refunds;

public sealed class InMemoryOrdersRepository : IOrdersRepository
{
    private readonly ConcurrentDictionary<string, OrderRecord> _orders = new(StringComparer.Ordinal);
    public Task<OrderRecord?> GetAsync(string orderId, CancellationToken ct)
        => Task.FromResult(_orders.TryGetValue(orderId, out var o) ? o : null);
    public void Seed(OrderRecord order) => _orders[order.OrderId] = order;
}

public sealed class InMemoryRefundsRepository : IRefundsRepository, IRefundsRepositoryByProvider
{
    private readonly ConcurrentDictionary<string, RefundRecord> _byId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _orderToRefund = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _providerToRefund = new(StringComparer.Ordinal);

    public Task<RefundRecord?> GetByIdAsync(string refundId, CancellationToken ct)
        => Task.FromResult(_byId.TryGetValue(refundId, out var r) ? r : null);

    public Task<RefundRecord?> GetByOrderIdAsync(string orderId, CancellationToken ct)
        => Task.FromResult(_orderToRefund.TryGetValue(orderId, out var id) && _byId.TryGetValue(id, out var r) ? r : null);

    public Task<RefundRecord?> GetByProviderIdAsync(string providerRefundId, CancellationToken ct)
        => Task.FromResult(_providerToRefund.TryGetValue(providerRefundId, out var id) && _byId.TryGetValue(id, out var r) ? r : null);

    public Task InsertAsync(RefundRecord refund, CancellationToken ct)
    {
        if (!_orderToRefund.TryAdd(refund.OrderId, refund.RefundId))
            throw new InvalidOperationException("Refund already exists for order"); // race-guard at repo boundary
        _byId[refund.RefundId] = refund;
        return Task.CompletedTask;
    }

    public Task UpdateProviderIdAsync(string refundId, string providerRefundId, CancellationToken ct)
    {
        if (_byId.TryGetValue(refundId, out var r))
        {
            _byId[refundId] = r with { providerRefundId = providerRefundId };
            _providerToRefund[providerRefundId] = refundId;
        }
        return Task.CompletedTask;
    }

    public Task MarkSucceededAsync(string refundId, CancellationToken ct)
    {
        if (_byId.TryGetValue(refundId, out var r)) _byId[refundId] = r with { Status = "succeeded" };
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(string refundId, string reasonCode, string? message, CancellationToken ct)
    {
        if (_byId.TryGetValue(refundId, out var r))
            _byId[refundId] = r with { Status = "failed", reasonCode = reasonCode };
        return Task.CompletedTask;
    }
}

// Sliding-window rate limiter (in-memory): 100 attempts per sub per 24h.
// Production replaces with Redis sorted-set (same shape as IdempotencyStore caps).
public sealed class InMemoryRefundRateLimiter : IRefundRateLimiter
{
    private const int Cap = 100;
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _hits = new(StringComparer.Ordinal);
    private readonly IRefundClock _clock;
    public InMemoryRefundRateLimiter(IRefundClock clock) { _clock = clock; }

    public Task<bool> TryConsumeAsync(string sub, CancellationToken ct)
    {
        var list = _hits.GetOrAdd(sub, _ => new List<DateTimeOffset>());
        lock (list)
        {
            var cutoff = _clock.UtcNow - Window;
            list.RemoveAll(t => t < cutoff);
            if (list.Count >= Cap) return Task.FromResult(false);
            list.Add(_clock.UtcNow);
            return Task.FromResult(true);
        }
    }
}
