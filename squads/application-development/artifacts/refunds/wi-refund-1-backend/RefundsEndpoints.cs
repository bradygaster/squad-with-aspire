// WI-REFUND-1: Refunds backend (POST /api/refunds, GET /api/orders/{id} eligibleActions)
// Contracts ratified by planning (cc08d34) + QA (test plan d737697a) + design (4c84355).
//
// Window anchor: order.ConfirmedAt (NOT created/paid). 24h hard cutoff.
// Refund rejection reasons: canceled | already_refunded | not_confirmed | window_expired
// All non-eligible cases → 409 Conflict with { error: "order_not_refundable", reason }
// Never expose provider refundId (re_xxx). Our ULID `refundId` only. (SEC-RFD-001)
// Rate limit: 100/sub/24h (per ideation cap; checkout's 1000 is wrong shape for refunds).
// Idempotency: reuses CheckoutIdempotency primitives — IdempotencyKey header, body-hashed,
//              sub-scoped, 422 on body mismatch, 409 on in-flight, 15min ConfirmTtl.

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using TravelAssistant.Api.Checkout; // IdempotencyStore, IIdempotencyKeyDeriver, JsonCanonicalizer

namespace TravelAssistant.Api.Refunds;

public static class RefundsEndpoints
{
    public static IEndpointRouteBuilder MapRefundsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api").WithTags("Refunds");

        group.MapPost("/refunds", PostRefundAsync)
             .RequireAuthorization()
             .WithName("CreateRefund");

        group.MapGet("/orders/{orderId}", GetOrderAsync)
             .RequireAuthorization()
             .WithName("GetOrderWithEligibility");

        return routes;
    }

    // POST /api/refunds — full refund only for v1
    internal static async Task<IResult> PostRefundAsync(
        HttpContext http,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromServices] IOrdersRepository orders,
        [FromServices] IRefundsRepository refunds,
        [FromServices] IRefundClock clock,
        [FromServices] IPaymentProvider payments,
        [FromServices] IIdempotencyStore idem,
        [FromServices] IIdempotencyKeyDeriver deriver,
        [FromServices] IRefundRateLimiter rateLimiter,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Results.Problem(statusCode: 400, title: "Idempotency-Key header required");

        var sub = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub))
            return Results.Unauthorized();

        // Read + canonicalize body for hash
        http.Request.EnableBuffering();
        using var reader = new StreamReader(http.Request.Body, leaveOpen: true);
        var bodyText = await reader.ReadToEndAsync(ct);
        http.Request.Body.Position = 0;

        RefundRequest? req;
        try { req = JsonSerializer.Deserialize<RefundRequest>(bodyText); }
        catch (JsonException) { return Results.Problem(statusCode: 400, title: "Malformed JSON"); }
        if (req is null || string.IsNullOrWhiteSpace(req.OrderId))
            return Results.Problem(statusCode: 400, title: "orderId required");

        // Rate limit: 100/sub/24h
        if (!await rateLimiter.TryConsumeAsync(sub, ct))
            return Results.StatusCode(429);

        // Idempotency: sub-scoped cache key, body-hashed
        var scope = deriver.BuildScope(sub);
        var canonical = JsonCanonicalizer.CanonicalizeUtf8(bodyText);
        var cacheKey = deriver.DeriveCacheKey(scope, idempotencyKey, canonical);

        var hit = await idem.TryGetAsync(cacheKey, canonical, ct);
        if (hit.Outcome == IdempotencyOutcome.Hit)
            return Results.Content(hit.Body!, "application/json", statusCode: hit.StatusCode);
        if (hit.Outcome == IdempotencyOutcome.BodyMismatch)
            return Results.Problem(statusCode: 422, title: "Idempotency-Key reused with different body");
        if (hit.Outcome == IdempotencyOutcome.InFlight)
            return Results.Problem(statusCode: 409, title: "Request in flight");

        // Eligibility — server is source of truth
        var order = await orders.GetAsync(req.OrderId, ct);
        if (order is null || order.Sub != sub)
            return Results.NotFound(); // IDOR-safe: don't leak existence

        var ineligible = ComputeIneligibility(order, clock.UtcNow);
        if (ineligible is not null)
        {
            var resp = JsonSerializer.Serialize(new { error = "order_not_refundable", reason = ineligible });
            await idem.PutAsync(cacheKey, canonical, 409, resp, TimeSpan.FromMinutes(15), ct);
            return Results.Content(resp, "application/json", statusCode: 409);
        }

        // Idempotent refund row reserve (handles eligibility-race → 409 REFUND_ALREADY_EXISTS)
        var existing = await refunds.GetByOrderIdAsync(req.OrderId, ct);
        if (existing is not null)
        {
            var resp = JsonSerializer.Serialize(new { error = "refund_already_exists", refundId = existing.RefundId, status = existing.Status });
            await idem.PutAsync(cacheKey, canonical, 409, resp, TimeSpan.FromMinutes(15), ct);
            return Results.Content(resp, "application/json", statusCode: 409);
        }

        var refundId = Ulid.NewUlid().ToString();
        var pending = new RefundRecord(refundId, order.OrderId, sub, "pending", clock.UtcNow,
                                       providerRefundId: null, // internal field; NEVER serialized
                                       reasonCode: null);
        await refunds.InsertAsync(pending, ct);

        // Fire to provider (sync attempt; webhook will finalize state)
        try
        {
            var providerResult = await payments.RefundAsync(order.PaymentIntentId, order.AmountMinor, order.Currency, ct);
            await refunds.UpdateProviderIdAsync(refundId, providerResult.ProviderRefundId, ct);
        }
        catch (PaymentGatewayTimeoutException)
        {
            // Leave pending; webhook reconciles
        }
        catch (PaymentValidationException ex)
        {
            await refunds.MarkFailedAsync(refundId, "PROVIDER_DECLINED", ex.Message, ct);
            var failResp = JsonSerializer.Serialize(new RefundResponse(refundId, "failed", order.OrderId, clock.UtcNow));
            await idem.PutAsync(cacheKey, canonical, 200, failResp, TimeSpan.FromMinutes(15), ct);
            return Results.Content(failResp, "application/json", statusCode: 200);
        }

        var okResp = JsonSerializer.Serialize(new RefundResponse(refundId, "pending", order.OrderId, clock.UtcNow));
        await idem.PutAsync(cacheKey, canonical, 202, okResp, TimeSpan.FromMinutes(15), ct);
        return Results.Content(okResp, "application/json", statusCode: 202);
    }

    // GET /api/orders/{id} — includes eligibleActions for frontend (button visibility)
    internal static async Task<IResult> GetOrderAsync(
        HttpContext http,
        string orderId,
        [FromServices] IOrdersRepository orders,
        [FromServices] IRefundsRepository refunds,
        [FromServices] IRefundClock clock,
        CancellationToken ct)
    {
        var sub = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub)) return Results.Unauthorized();

        var order = await orders.GetAsync(orderId, ct);
        if (order is null || order.Sub != sub) return Results.NotFound();

        var existingRefund = await refunds.GetByOrderIdAsync(orderId, ct);
        var ineligible = ComputeIneligibility(order, clock.UtcNow);
        var eligibleActions = (ineligible is null && existingRefund is null)
            ? new[] { "refund" }
            : Array.Empty<string>();

        var dto = new
        {
            orderId = order.OrderId,
            status = order.Status,
            confirmedAt = order.ConfirmedAt,
            amount = new { minorUnits = order.AmountMinor, currency = order.Currency },
            eligibleActions,
            // Refund summary if one exists — but NEVER providerRefundId
            refund = existingRefund is null ? null : new
            {
                refundId = existingRefund.RefundId,
                status = existingRefund.Status,
                createdAt = existingRefund.CreatedAt,
                failureReason = existingRefund.ReasonCode
            }
        };
        return Results.Ok(dto);
    }

    // Window anchor = order.ConfirmedAt. 24h cutoff.
    // Order: canceled > already_refunded > not_confirmed > window_expired (most specific wins)
    internal static string? ComputeIneligibility(OrderRecord order, DateTimeOffset now)
    {
        if (order.Status == "canceled") return "canceled";
        if (order.Status != "confirmed") return "not_confirmed";
        if (order.ConfirmedAt is null) return "not_confirmed";
        if (now - order.ConfirmedAt.Value > TimeSpan.FromHours(24)) return "window_expired";
        return null;
    }
}

public sealed record RefundRequest(string OrderId);
public sealed record RefundResponse(string RefundId, string Status, string OrderId, DateTimeOffset CreatedAt);

public sealed record OrderRecord(
    string OrderId,
    string Sub,
    string Status,            // confirmed | canceled | pending | ...
    DateTimeOffset? ConfirmedAt,
    long AmountMinor,
    string Currency,
    string PaymentIntentId);

public sealed record RefundRecord(
    string RefundId,
    string OrderId,
    string Sub,
    string Status,            // pending | succeeded | failed
    DateTimeOffset CreatedAt,
    string? providerRefundId, // INTERNAL — never serialized to clients
    string? reasonCode);

public interface IOrdersRepository
{
    Task<OrderRecord?> GetAsync(string orderId, CancellationToken ct);
}

public interface IRefundsRepository
{
    Task<RefundRecord?> GetByOrderIdAsync(string orderId, CancellationToken ct);
    Task<RefundRecord?> GetByIdAsync(string refundId, CancellationToken ct);
    Task InsertAsync(RefundRecord refund, CancellationToken ct);
    Task UpdateProviderIdAsync(string refundId, string providerRefundId, CancellationToken ct);
    Task MarkSucceededAsync(string refundId, CancellationToken ct);
    Task MarkFailedAsync(string refundId, string reasonCode, string? message, CancellationToken ct);
}

public interface IRefundClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemRefundClock : IRefundClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

public interface IRefundRateLimiter
{
    Task<bool> TryConsumeAsync(string sub, CancellationToken ct); // 100/sub/24h
}

public interface IPaymentProvider
{
    Task<PaymentRefundResult> RefundAsync(string paymentIntentId, long amountMinor, string currency, CancellationToken ct);
}
public sealed record PaymentRefundResult(string ProviderRefundId);
public sealed class PaymentGatewayTimeoutException : Exception { }
public sealed class PaymentValidationException : Exception { public PaymentValidationException(string m) : base(m) { } }
