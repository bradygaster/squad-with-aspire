// WI-CONFIRM-1: GET /api/checkout/orders/{orderId}/status
// Drop location: src/TravelAssistant.Api/Checkout/OrderStatusEndpoint.cs
//
// Contract (per ideation-research-planning's NEXT-VERTICAL-confirmation-page.md):
//   - States: pending | confirmed | payment_failed | inventory_released | canceled
//   - AuthZ: JWT sub MUST match order owner OR scope=order:read:any (support)
//   - IDOR-safe: 404 (not 403) on sub mismatch (matches checkout endpoint pattern)
//   - ETag + Cache-Control: private, max-age=2 (matches a11y spec's 2s poll cadence)
//   - ETag changes ONLY when state OR paymentState OR fulfillmentState changes
//   - Read-only: no Idempotency-Key required
//
// Wire in Program.cs:
//   app.MapCheckoutOrderStatusEndpoint();

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace TravelAssistant.Api.Checkout;

public enum OrderState
{
    Pending,
    Confirmed,
    PaymentFailed,
    InventoryReleased,
    Canceled,
}

public enum PaymentState
{
    None,
    Authorized,
    Captured,
    Failed,
    Refunded,
}

public enum FulfillmentState
{
    None,
    Reserved,
    Released,
    Committed,
}

public sealed record OrderStatusSnapshot(
    string OrderId,
    string Sub,
    OrderState State,
    PaymentState PaymentState,
    FulfillmentState FulfillmentState,
    DateTimeOffset UpdatedAt);

public sealed record OrderStatusResponse(
    string OrderId,
    string State,
    string PaymentState,
    string FulfillmentState,
    DateTimeOffset UpdatedAt,
    string Etag);

public interface IOrdersRepository
{
    Task<OrderStatusSnapshot?> GetStatusAsync(string orderId, CancellationToken ct);
}

public static class OrderStatusEndpoint
{
    public static IEndpointRouteBuilder MapCheckoutOrderStatusEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/checkout/orders/{orderId}/status", HandleAsync)
            .RequireAuthorization()
            .WithName("GetOrderStatus");
        return app;
    }

    internal static async Task<IResult> HandleAsync(
        string orderId,
        HttpContext http,
        IOrdersRepository repo,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderId) || orderId.Length > 64)
        {
            return Results.BadRequest(new { error = "invalid_order_id" });
        }

        var snapshot = await repo.GetStatusAsync(orderId, ct).ConfigureAwait(false);
        if (snapshot is null)
        {
            // True 404 — order doesn't exist.
            return Results.NotFound();
        }

        var callerSub = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? http.User.FindFirstValue("sub");
        var hasReadAnyScope = HasScope(http.User, "order:read:any");

        if (!hasReadAnyScope && !string.Equals(callerSub, snapshot.Sub, StringComparison.Ordinal))
        {
            // IDOR-safe: 404, not 403. Indistinguishable from "doesn't exist".
            return Results.NotFound();
        }

        var etag = ComputeEtag(snapshot);

        // Conditional GET — honor If-None-Match for the polling client.
        var ifNoneMatch = http.Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrEmpty(ifNoneMatch) && EtagMatches(ifNoneMatch, etag))
        {
            http.Response.Headers.ETag = etag;
            http.Response.Headers.CacheControl = "private, max-age=2";
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        http.Response.Headers.ETag = etag;
        http.Response.Headers.CacheControl = "private, max-age=2";

        var body = new OrderStatusResponse(
            OrderId: snapshot.OrderId,
            State: StateToWire(snapshot.State),
            PaymentState: PaymentStateToWire(snapshot.PaymentState),
            FulfillmentState: FulfillmentStateToWire(snapshot.FulfillmentState),
            UpdatedAt: snapshot.UpdatedAt,
            Etag: etag);

        return Results.Ok(body);
    }

    private static bool HasScope(ClaimsPrincipal user, string required)
    {
        var scope = user.FindFirstValue("scope") ?? user.FindFirstValue("scp");
        if (string.IsNullOrEmpty(scope)) return false;
        foreach (var s in scope.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(s, required, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    // ETag is a hash of (orderId, state, paymentState, fulfillmentState).
    // Excludes UpdatedAt deliberately — AC says "ETag changes ONLY when state
    // OR paymentState OR fulfillmentState changes". A timestamp-only update
    // (e.g. clock refresh) must NOT invalidate the client's cache.
    internal static string ComputeEtag(OrderStatusSnapshot s)
    {
        var canonical = $"{s.OrderId}|{(int)s.State}|{(int)s.PaymentState}|{(int)s.FulfillmentState}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        // Weak ETag — content is functionally equivalent across encodings.
        // 16 hex chars (64 bits) is plenty for collision resistance per orderId.
        return $"W/\"{Convert.ToHexString(bytes, 0, 8)}\"";
    }

    private static bool EtagMatches(string ifNoneMatch, string currentEtag)
    {
        // RFC 7232 §3.2: If-None-Match can be a list; "*" matches anything.
        foreach (var raw in ifNoneMatch.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var v = raw.Trim();
            if (v == "*") return true;
            if (string.Equals(v, currentEtag, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static string StateToWire(OrderState s) => s switch
    {
        OrderState.Pending            => "pending",
        OrderState.Confirmed          => "confirmed",
        OrderState.PaymentFailed      => "payment_failed",
        OrderState.InventoryReleased  => "inventory_released",
        OrderState.Canceled           => "canceled",
        _ => throw new InvalidOperationException($"unknown OrderState {s}"),
    };

    private static string PaymentStateToWire(PaymentState s) => s switch
    {
        PaymentState.None       => "none",
        PaymentState.Authorized => "authorized",
        PaymentState.Captured   => "captured",
        PaymentState.Failed     => "failed",
        PaymentState.Refunded   => "refunded",
        _ => throw new InvalidOperationException($"unknown PaymentState {s}"),
    };

    private static string FulfillmentStateToWire(FulfillmentState s) => s switch
    {
        FulfillmentState.None      => "none",
        FulfillmentState.Reserved  => "reserved",
        FulfillmentState.Released  => "released",
        FulfillmentState.Committed => "committed",
        _ => throw new InvalidOperationException($"unknown FulfillmentState {s}"),
    };
}
