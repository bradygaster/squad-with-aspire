// WI-REFUND-4 (provider-webhook handler, app-side)
// Stripe-style: refund.updated event → reconcile pending → succeeded|failed.
// Dedup via existing webhook signature + SETNX wh:evt:{id} (reuses checkout webhook patterns).
// Failure mapping: provider code → allowlist (PROVIDER_DECLINED | PROVIDER_TIMEOUT |
// PROVIDER_UNAVAILABLE | INSUFFICIENT_PROVIDER_FUNDS). Unmapped → PROVIDER_DECLINED + telemetry.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TravelAssistant.Api.Refunds;

public static class RefundWebhookEndpoints
{
    public static IEndpointRouteBuilder MapRefundWebhooks(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/webhooks/refunds", HandleAsync).AllowAnonymous();
        return routes;
    }

    internal static async Task<IResult> HandleAsync(
        HttpContext http,
        IRefundsRepository refunds,
        IWebhookEventDedup dedup,
        IWebhookSignatureVerifier verifier,
        IRefundsTelemetry telemetry,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var log = loggerFactory.CreateLogger("RefundWebhook");
        using var reader = new StreamReader(http.Request.Body);
        var raw = await reader.ReadToEndAsync(ct);
        var sigHeader = http.Request.Headers["Stripe-Signature"].ToString();

        if (!verifier.Verify(raw, sigHeader))
            return Results.Unauthorized();

        var evt = WebhookEvent.Parse(raw);
        if (evt is null) return Results.BadRequest();

        // Dedup at edge — replays are silent 200
        if (!await dedup.TryClaimAsync(evt.Id, TimeSpan.FromDays(7), ct))
            return Results.Ok();

        if (evt.Type != "refund.updated" && evt.Type != "refund.succeeded" && evt.Type != "refund.failed")
            return Results.Ok(); // ignore unrelated events silently

        var providerRefundId = evt.Data["id"]?.ToString();
        var status = evt.Data["status"]?.ToString();
        var failureCode = evt.Data["failure_reason"]?.ToString();
        if (string.IsNullOrEmpty(providerRefundId)) return Results.BadRequest();

        // Look up by provider ID (set after sync attempt). If not found, log + 200 (out-of-order webhook).
        var refund = await refunds.GetByProviderIdAsync(providerRefundId, ct);
        if (refund is null)
        {
            log.LogWarning("Refund webhook for unknown providerRefundId {prid}", providerRefundId);
            return Results.Ok();
        }

        switch (status)
        {
            case "succeeded":
                await refunds.MarkSucceededAsync(refund.RefundId, ct);
                telemetry.Emit("refund.succeeded", new { refund.RefundId });
                break;
            case "failed":
                var mapped = MapFailureCode(failureCode);
                if (mapped == "PROVIDER_DECLINED" && !string.IsNullOrEmpty(failureCode))
                    telemetry.Emit("refund.failure_reason_unmapped", new { raw = failureCode });
                await refunds.MarkFailedAsync(refund.RefundId, mapped, failureCode, ct);
                telemetry.Emit("refund.failed", new { refund.RefundId, code = mapped });
                break;
        }
        return Results.Ok();
    }

    internal static string MapFailureCode(string? providerCode) => providerCode switch
    {
        "card_declined" or "expired_card" or "incorrect_cvc" => "PROVIDER_DECLINED",
        "processing_error" or "gateway_timeout" => "PROVIDER_TIMEOUT",
        "service_unavailable" => "PROVIDER_UNAVAILABLE",
        "insufficient_funds" => "INSUFFICIENT_PROVIDER_FUNDS",
        _ => "PROVIDER_DECLINED"
    };
}

public interface IWebhookEventDedup
{
    Task<bool> TryClaimAsync(string eventId, TimeSpan ttl, CancellationToken ct);
}
public interface IWebhookSignatureVerifier { bool Verify(string rawBody, string signatureHeader); }
public interface IRefundsTelemetry { void Emit(string name, object payload); }

internal sealed record WebhookEvent(string Id, string Type, Dictionary<string, object?> Data)
{
    public static WebhookEvent? Parse(string raw)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var id = root.GetProperty("id").GetString() ?? "";
            var type = root.GetProperty("type").GetString() ?? "";
            var data = new Dictionary<string, object?>();
            if (root.TryGetProperty("data", out var d) && d.TryGetProperty("object", out var o))
                foreach (var p in o.EnumerateObject()) data[p.Name] = p.Value.ToString();
            return new WebhookEvent(id, type, data);
        }
        catch { return null; }
    }
}

// Repo extension needed for webhook lookup
public static class RefundsRepositoryExtensions
{
    public static Task<RefundRecord?> GetByProviderIdAsync(this IRefundsRepository repo, string providerRefundId, CancellationToken ct)
        => (repo as IRefundsRepositoryByProvider)?.GetByProviderIdAsync(providerRefundId, ct)
           ?? throw new NotSupportedException("Implement IRefundsRepositoryByProvider");
}
public interface IRefundsRepositoryByProvider
{
    Task<RefundRecord?> GetByProviderIdAsync(string providerRefundId, CancellationToken ct);
}
