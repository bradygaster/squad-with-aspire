using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using StackExchange.Redis;
using TravelAssistant.Api.Checkout;
using Xunit;

namespace TravelAssistant.Api.Tests.Checkout;

// WI-4 + WI-5 acceptance tests. Wire against in-memory fakes; integration suite
// re-runs against real Redis once WI-6 wiring lands.

public sealed class InventoryHoldTests
{
    [Fact]
    public async Task Reserve_WhenStockSufficient_DecrementsAndCreatesHold()
    {
        var store = new FakeInventoryStore(initialStock: 10);
        var result = await store.TryReserveAsync("sku-1", qty: 3, holdId: "h1", scope: "sub:alice",
            ttl: TimeSpan.FromMinutes(15), ct: default);
        result.Outcome.Should().Be(HoldOutcome.Reserved);
        result.Available.Should().Be(7);
    }

    [Fact]
    public async Task Reserve_WhenInsufficientStock_DoesNotDecrement()
    {
        var store = new FakeInventoryStore(initialStock: 2);
        var result = await store.TryReserveAsync("sku-1", qty: 3, holdId: "h1", scope: "sub:alice",
            ttl: TimeSpan.FromMinutes(15), ct: default);
        result.Outcome.Should().Be(HoldOutcome.InsufficientStock);
        store.GetStock("sku-1").Should().Be(2);
    }

    [Fact]
    public async Task Reserve_ConcurrentCalls_OnlyOneCanWinSameHoldId()
    {
        var store = new FakeInventoryStore(initialStock: 10);
        var t1 = store.TryReserveAsync("sku-1", 1, "h-dup", "sub:a", TimeSpan.FromMinutes(15), default);
        var t2 = store.TryReserveAsync("sku-1", 1, "h-dup", "sub:a", TimeSpan.FromMinutes(15), default);
        var results = await Task.WhenAll(t1, t2);
        results.Count(r => r.Outcome == HoldOutcome.Reserved).Should().Be(1);
        results.Count(r => r.Outcome == HoldOutcome.AlreadyHeld).Should().Be(1);
    }

    [Fact]
    public async Task Release_ReturnsStockToAvailable()
    {
        var store = new FakeInventoryStore(initialStock: 10);
        await store.TryReserveAsync("sku-1", 3, "h1", "sub:a", TimeSpan.FromMinutes(15), default);
        await store.ReleaseAsync("h1", default);
        store.GetStock("sku-1").Should().Be(10);
    }

    [Fact]
    public async Task Release_OnExpiredOrUnknownHold_IsIdempotentNoOp()
    {
        var store = new FakeInventoryStore(initialStock: 10);
        await store.ReleaseAsync("never-existed", default); // must not throw
        store.GetStock("sku-1").Should().Be(10);
    }

    [Fact]
    public async Task Commit_DeletesHoldWithoutReturningStock()
    {
        var store = new FakeInventoryStore(initialStock: 10);
        await store.TryReserveAsync("sku-1", 3, "h1", "sub:a", TimeSpan.FromMinutes(15), default);
        var committed = await store.CommitAsync("h1", default);
        committed.Should().BeTrue();
        store.GetStock("sku-1").Should().Be(7);
    }
}

public sealed class WebhookEndpointTests
{
    private const string Secret = "whsec_test_12345";

    [Fact]
    public async Task ValidSignature_RecentTimestamp_NewEvent_Returns200AndCommitsHold()
    {
        var body = JsonSerializer.Serialize(new
        {
            id = "evt_1",
            type = "payment_intent.succeeded",
            data = new { @object = new { metadata = new { hold_id = "h1" } } }
        });
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = ComputeSig(ts, body);

        var holds = new FakeInventoryStore(initialStock: 10);
        await holds.TryReserveAsync("sku-1", 3, "h1", "sub:a", TimeSpan.FromMinutes(15), default);

        var resp = await PostWebhook(body, $"t={ts},v1={sig}", holds);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        holds.HoldExists("h1").Should().BeFalse(); // committed → deleted
        holds.GetStock("sku-1").Should().Be(7);    // stock NOT returned
    }

    [Fact]
    public async Task InvalidSignature_Returns401()
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var resp = await PostWebhook("{}", $"t={ts},v1={new string('a', 64)}", new FakeInventoryStore(0));
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TimestampOutsideToleranceWindow_Returns401()
    {
        var oldTs = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var sig = ComputeSig(oldTs, "{}");
        var resp = await PostWebhook("{}", $"t={oldTs},v1={sig}", new FakeInventoryStore(0));
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DuplicateEventId_SecondCallReturns200WithoutSideEffect()
    {
        var body = JsonSerializer.Serialize(new
        {
            id = "evt_dup",
            type = "payment_intent.payment_failed",
            data = new { @object = new { metadata = new { hold_id = "h1" } } }
        });
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = ComputeSig(ts, body);

        var holds = new FakeInventoryStore(initialStock: 10);
        await holds.TryReserveAsync("sku-1", 3, "h1", "sub:a", TimeSpan.FromMinutes(15), default);

        var first = await PostWebhook(body, $"t={ts},v1={sig}", holds);
        var stockAfterFirst = holds.GetStock("sku-1"); // 10 after release
        var second = await PostWebhook(body, $"t={ts},v1={sig}", holds);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        holds.GetStock("sku-1").Should().Be(stockAfterFirst); // no double-release
    }

    private static string ComputeSig(long ts, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{ts}.{body}"));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    private static Task<HttpResponseMessage> PostWebhook(string body, string sig, IInventoryHoldStore holds)
    {
        // Test harness wiring elided — see README for WebApplicationFactory<Program> setup.
        // This is a contract sketch the integration test fills in once endpoints register.
        throw new SkipException("Wire to WebApplicationFactory<Program> in PR #4 follow-up.");
    }

    private sealed class SkipException(string r) : Exception(r);
}

// Test double matching IInventoryHoldStore semantics without Redis.
internal sealed class FakeInventoryStore : IInventoryHoldStore
{
    private readonly Dictionary<string, int> _stock = new();
    private readonly Dictionary<string, (string Sku, int Qty)> _holds = new();
    private readonly object _lock = new();

    public FakeInventoryStore(int initialStock) => _stock["sku-1"] = initialStock;

    public int GetStock(string sku) { lock (_lock) return _stock.GetValueOrDefault(sku); }
    public bool HoldExists(string id) { lock (_lock) return _holds.ContainsKey(id); }

    public Task<HoldResult> TryReserveAsync(string sku, int qty, string holdId, string scope, TimeSpan ttl, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_holds.ContainsKey(holdId))
                return Task.FromResult(new HoldResult(HoldOutcome.AlreadyHeld, _stock.GetValueOrDefault(sku), qty));
            var avail = _stock.GetValueOrDefault(sku);
            if (avail < qty) return Task.FromResult(new HoldResult(HoldOutcome.InsufficientStock, avail, qty));
            _stock[sku] = avail - qty;
            _holds[holdId] = (sku, qty);
            return Task.FromResult(new HoldResult(HoldOutcome.Reserved, avail - qty, qty));
        }
    }

    public Task ReleaseAsync(string holdId, CancellationToken ct)
    {
        lock (_lock)
        {
            if (!_holds.TryGetValue(holdId, out var h)) return Task.CompletedTask;
            _stock[h.Sku] = _stock.GetValueOrDefault(h.Sku) + h.Qty;
            _holds.Remove(holdId);
            return Task.CompletedTask;
        }
    }

    public Task<bool> CommitAsync(string holdId, CancellationToken ct)
    {
        lock (_lock) return Task.FromResult(_holds.Remove(holdId));
    }
}
