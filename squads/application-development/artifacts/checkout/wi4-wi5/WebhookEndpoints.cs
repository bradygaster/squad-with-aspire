// WI-5: Payment webhook handler with at-least-once dedup (closes issue #49).
// Stripe-style: provider POSTs to /webhooks/payments with header
// `Stripe-Signature: t=<unix>,v1=<hmac>`. Reject if:
//   - signature invalid (constant-time HMAC-SHA256 compare)
//   - timestamp older than 5min (replay window)
//   - event-id already processed (dedup via Redis SETNX with 7-day TTL)
// Dedup key intentionally separate from idempotency-key store: webhooks are
// provider-driven, idempotency keys are caller-driven. Different scopes,
// different TTLs, different abuse profiles.

namespace TravelAssistant.Api.Checkout;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

public sealed class WebhookOptions
{
    public required string Secret { get; init; }
    public TimeSpan ToleranceWindow { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan DedupTtl { get; init; } = TimeSpan.FromDays(7);
}

public interface IWebhookDedupStore
{
    Task<bool> TryClaimAsync(string eventId, TimeSpan ttl, CancellationToken ct);
}

public sealed class RedisWebhookDedupStore(IConnectionMultiplexer mux) : IWebhookDedupStore
{
    public Task<bool> TryClaimAsync(string eventId, TimeSpan ttl, CancellationToken ct)
        => mux.GetDatabase().StringSetAsync($"wh:evt:{eventId}", "1", ttl, When.NotExists);
}

public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapPaymentWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/payments", async (HttpContext ctx,
            WebhookOptions opts, IWebhookDedupStore dedup, IInventoryHoldStore holds,
            ILogger<WebhookOptions> log, CancellationToken ct) =>
        {
            ctx.Request.EnableBuffering();
            using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
            var rawBody = await reader.ReadToEndAsync(ct);
            ctx.Request.Body.Position = 0;

            if (!ctx.Request.Headers.TryGetValue("Stripe-Signature", out var sigHeader))
                return Results.StatusCode(401);

            if (!TryParseSignature(sigHeader.ToString(), out var ts, out var providedMac))
                return Results.StatusCode(401);

            // Replay window: timestamp must be within tolerance of now.
            var age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ts;
            if (Math.Abs(age) > (long)opts.ToleranceWindow.TotalSeconds)
                return Results.StatusCode(401);

            // Signed payload = "{ts}.{rawBody}" per Stripe spec.
            var signedPayload = $"{ts}.{rawBody}";
            var expectedMac = ComputeHmacSha256(opts.Secret, signedPayload);
            if (!CryptographicOperations.FixedTimeEquals(expectedMac, providedMac))
                return Results.StatusCode(401);

            // Parse event for dedup id + outcome.
            JsonDocument doc;
            try { doc = JsonDocument.Parse(rawBody); }
            catch (JsonException) { return Results.StatusCode(400); }

            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                return Results.StatusCode(400);
            var eventId = idEl.GetString()!;

            // SETNX claim — if already processed, return 200 (provider expects 2xx to stop retry).
            if (!await dedup.TryClaimAsync(eventId, opts.DedupTtl, ct))
            {
                log.LogInformation("Webhook {EventId} already processed; replay ignored", eventId);
                return Results.Ok(new { status = "duplicate" });
            }

            // Dispatch by event type. Only two outcomes flip inventory state.
            var eventType = root.GetProperty("type").GetString();
            var holdId = root.GetProperty("data").GetProperty("object")
                .GetProperty("metadata").GetProperty("hold_id").GetString();

            switch (eventType)
            {
                case "payment_intent.succeeded":
                    await holds.CommitAsync(holdId!, ct);
                    log.LogInformation("Webhook {EventId}: committed hold {HoldId}", eventId, holdId);
                    break;
                case "payment_intent.payment_failed":
                case "payment_intent.canceled":
                    await holds.ReleaseAsync(holdId!, ct);
                    log.LogInformation("Webhook {EventId}: released hold {HoldId}", eventId, holdId);
                    break;
                default:
                    // Unknown event types: ack (we claimed dedup) but no side effect.
                    log.LogInformation("Webhook {EventId}: ignored type {Type}", eventId, eventType);
                    break;
            }

            return Results.Ok(new { status = "processed" });
        });
        return app;
    }

    private static bool TryParseSignature(string header, out long timestamp, out byte[] mac)
    {
        timestamp = 0;
        mac = Array.Empty<byte>();
        var parts = header.Split(',');
        string? tsStr = null;
        string? v1 = null;
        foreach (var p in parts)
        {
            var kv = p.Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0] == "t") tsStr = kv[1];
            else if (kv[0] == "v1") v1 = kv[1];
        }
        if (tsStr is null || v1 is null) return false;
        if (!long.TryParse(tsStr, out timestamp)) return false;
        try { mac = Convert.FromHexString(v1); } catch { return false; }
        return mac.Length == 32;
    }

    private static byte[] ComputeHmacSha256(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    }
}
