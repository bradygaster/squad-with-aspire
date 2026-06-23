// Amendment to squads/application-development/artifacts/checkout/wi4-wi5/WebhookEndpoints.cs
//
// Closes QA ask #1 (Hockney): adds a Development-only side-effect counter
// so WebhookHarnessTests + webhook-replay-storm.js can verify TRUE dedup
// (event_id dispatched at most once downstream), not just HTTP-level 2xx.
//
// Gate: ASPNETCORE_ENVIRONMENT=Development AND ASPNETCORE_ENABLE_TEST_AUTH=1.
// Both must be true. Never registered in Production regardless of env vars.
//
// Counter is incremented INSIDE the dispatch path (after dedup SETNX succeeds,
// before holds.CommitAsync/ReleaseAsync), so a replayed event_id that hits the
// SETNX guard does NOT bump the counter — which is exactly what the tests assert.

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TravelAssistant.Api.Checkout.Webhooks;

/// <summary>
/// Process-local counter of how many times a given webhook event_id was
/// dispatched to the downstream handler (Commit/Release). Dedup correctness
/// = every key in this dict has value == 1 after a replay storm.
///
/// Reset per process. NOT distributed — single ACA replica only. The k6
/// webhook-replay-storm.js test pins traffic to one replica via session
/// affinity header for this reason.
/// </summary>
public sealed class WebhookDispatchCounter
{
    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);

    public void Increment(string eventId) =>
        _counts.AddOrUpdate(eventId, 1, static (_, n) => n + 1);

    public int Get(string eventId) =>
        _counts.TryGetValue(eventId, out var n) ? n : 0;
}

public static class WebhookDebugEndpoints
{
    /// <summary>
    /// Registers the debug endpoint IF AND ONLY IF env is Development AND
    /// ASPNETCORE_ENABLE_TEST_AUTH=1. In any other configuration this is a no-op
    /// and the endpoint does not exist on the route table.
    /// </summary>
    public static IEndpointRouteBuilder MapWebhookDebugEndpoints(
        this IEndpointRouteBuilder app,
        IWebHostEnvironment env)
    {
        if (!env.IsDevelopment())
        {
            return app;
        }

        var testAuthEnabled = string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENABLE_TEST_AUTH"),
            "1",
            StringComparison.Ordinal);

        if (!testAuthEnabled)
        {
            return app;
        }

        app.MapGet("/webhooks/payments/_debug/event-count/{eventId}",
            (string eventId, WebhookDispatchCounter counter) =>
                Results.Ok(new { count = counter.Get(eventId) }))
           .WithName("WebhookDebugEventCount")
           .ExcludeFromDescription();

        return app;
    }
}

// ---------------------------------------------------------------------------
// DI registration (Program.cs / startup):
//
//   builder.Services.AddSingleton<WebhookDispatchCounter>();
//
//   // ... after app.MapWebhookEndpoints(...):
//   app.MapWebhookDebugEndpoints(app.Environment);
//
// ---------------------------------------------------------------------------
// Patch to existing WebhookEndpoints.cs dispatch path:
//
// In the handler for /webhooks/payments, AFTER the SETNX dedup guard succeeds
// (i.e., this event_id is being processed for the first time) and BEFORE
// calling holds.CommitAsync / holds.ReleaseAsync, inject the counter bump:
//
//   // existing code:
//   var firstSeen = await dedup.TryReserveAsync($"wh:evt:{eventId}", TimeSpan.FromDays(7), ct);
//   if (!firstSeen)
//   {
//       return Results.Ok();  // idempotent replay — already processed
//   }
//
//   // NEW (one line, behind DI-resolved optional dep):
//   counter?.Increment(eventId);
//
//   // existing dispatch:
//   await (eventType switch
//   {
//       "payment_intent.succeeded"      => holds.CommitAsync(orderId, ct),
//       "payment_intent.payment_failed" => holds.ReleaseAsync(orderId, ct),
//       "payment_intent.canceled"       => holds.ReleaseAsync(orderId, ct),
//       _                               => Task.CompletedTask,
//   });
//
//   return Results.Ok();
//
// The handler signature gains:
//   WebhookDispatchCounter? counter   // nullable — null in prod, instance in dev
//
// In production the singleton is not registered, so DI resolves null and the
// `counter?.Increment(...)` is a noop with zero allocation.
