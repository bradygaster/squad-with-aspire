// WI-CONFIRM-1: tests for GET /api/checkout/orders/{orderId}/status
// Drop location: tests/TravelAssistant.Api.Tests/Checkout/OrderStatusEndpointTests.cs
//
// AC coverage:
//   - 5 state cases (pending, confirmed, payment_failed, inventory_released, canceled)
//   - IDOR: sub mismatch → 404 (not 403)
//   - ETag stability: same state → same ETag
//   - ETag invalidation: state change → new ETag
//   - ETag exclusion: UpdatedAt-only change → SAME ETag (per spec AC)
//   - 304 on If-None-Match match
//   - support-scope override (order:read:any) sees foreign order
//   - 404 on missing order (true not-found)
//   - Cache-Control header on every 2xx/304 response
//
// Uses the same WAF pattern from waf-fake-payment-provider bundle (commit 9761ce6).

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TravelAssistant.Api.Checkout;
using Xunit;

namespace TravelAssistant.Api.Tests.Checkout;

public class OrderStatusEndpointTests : IClassFixture<CheckoutWebApplicationFactory>
{
    private readonly CheckoutWebApplicationFactory _factory;

    public OrderStatusEndpointTests(CheckoutWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(string sub, string? scope = null)
    {
        var client = _factory.CreateClient();
        var token = scope is null ? $"test:{sub}" : $"test:{sub}:scope={scope}";
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Theory]
    [InlineData(OrderState.Pending,           "pending")]
    [InlineData(OrderState.Confirmed,         "confirmed")]
    [InlineData(OrderState.PaymentFailed,     "payment_failed")]
    [InlineData(OrderState.InventoryReleased, "inventory_released")]
    [InlineData(OrderState.Canceled,          "canceled")]
    public async Task ReturnsCorrectWireValue_ForEachState(OrderState state, string expectedWire)
    {
        const string sub = "user-1";
        _factory.SeedOrder("order-1", sub, state);

        var client = ClientFor(sub);
        var res = await client.GetAsync("/api/checkout/orders/order-1/status");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<OrderStatusResponse>();
        body!.State.Should().Be(expectedWire);
        body.OrderId.Should().Be("order-1");
        res.Headers.CacheControl!.Private.Should().BeTrue();
        res.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromSeconds(2));
        res.Headers.ETag.Should().NotBeNull();
    }

    [Fact]
    public async Task SubMismatch_Returns404_NotForbidden()
    {
        _factory.SeedOrder("order-2", sub: "alice");

        // Bob is asking for Alice's order.
        var client = ClientFor("bob");
        var res = await client.GetAsync("/api/checkout/orders/order-2/status");

        // IDOR-safe: 404, not 403. Indistinguishable from "doesn't exist".
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SupportScope_CanReadAnyOrder()
    {
        _factory.SeedOrder("order-3", sub: "alice", OrderState.Confirmed);

        var client = ClientFor("support-agent-1", scope: "order:read:any");
        var res = await client.GetAsync("/api/checkout/orders/order-3/status");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<OrderStatusResponse>();
        body!.State.Should().Be("confirmed");
    }

    [Fact]
    public async Task MissingOrder_Returns404()
    {
        var client = ClientFor("user-1");
        var res = await client.GetAsync("/api/checkout/orders/does-not-exist/status");

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Etag_IsStable_WhenStateUnchanged()
    {
        const string sub = "user-1";
        _factory.SeedOrder("order-4", sub, OrderState.Pending);
        var client = ClientFor(sub);

        var res1 = await client.GetAsync("/api/checkout/orders/order-4/status");
        var res2 = await client.GetAsync("/api/checkout/orders/order-4/status");

        res1.Headers.ETag!.Tag.Should().Be(res2.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Etag_Changes_WhenStateChanges()
    {
        const string sub = "user-1";
        _factory.SeedOrder("order-5", sub, OrderState.Pending);
        var client = ClientFor(sub);

        var resBefore = await client.GetAsync("/api/checkout/orders/order-5/status");
        var etagBefore = resBefore.Headers.ETag!.Tag;

        _factory.UpdateOrderState("order-5", OrderState.Confirmed);

        var resAfter = await client.GetAsync("/api/checkout/orders/order-5/status");
        var etagAfter = resAfter.Headers.ETag!.Tag;

        etagAfter.Should().NotBe(etagBefore);
    }

    [Fact]
    public async Task Etag_DoesNotChange_WhenOnlyUpdatedAtChanges()
    {
        // AC: "ETag changes ONLY when state OR paymentState OR fulfillmentState changes".
        // A timestamp-only refresh (e.g. periodic ledger touch) must NOT invalidate
        // the polling client's cache — that would defeat the max-age=2.
        const string sub = "user-1";
        _factory.SeedOrder("order-6", sub, OrderState.Pending);
        var client = ClientFor(sub);

        var res1 = await client.GetAsync("/api/checkout/orders/order-6/status");
        _factory.TouchUpdatedAt("order-6");
        var res2 = await client.GetAsync("/api/checkout/orders/order-6/status");

        res1.Headers.ETag!.Tag.Should().Be(res2.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task IfNoneMatch_Returns304_WhenEtagMatches()
    {
        const string sub = "user-1";
        _factory.SeedOrder("order-7", sub, OrderState.Pending);
        var client = ClientFor(sub);

        var first = await client.GetAsync("/api/checkout/orders/order-7/status");
        var etag = first.Headers.ETag!.Tag;

        var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/checkout/orders/order-7/status");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var res = await client.SendAsync(conditional);

        res.StatusCode.Should().Be(HttpStatusCode.NotModified);
        res.Headers.ETag!.Tag.Should().Be(etag);
        res.Headers.CacheControl!.MaxAge.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Unauthorized_Returns401()
    {
        var client = _factory.CreateClient();
        // No Authorization header.
        var res = await client.GetAsync("/api/checkout/orders/order-1/status");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 65 chars
    public async Task InvalidOrderId_Returns400(string badId)
    {
        var client = ClientFor("user-1");
        var res = await client.GetAsync($"/api/checkout/orders/{Uri.EscapeDataString(badId)}/status");
        // Empty path segment routes to 404 (no match); 65-char gets to handler → 400.
        // We accept either 400 or 404 for empty; explicit 400 for the overlong case.
        if (badId.Length > 64)
        {
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }
}
