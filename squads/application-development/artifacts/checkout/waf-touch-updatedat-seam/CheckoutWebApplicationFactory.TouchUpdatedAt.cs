// Additive partial extension to CheckoutWebApplicationFactory shipped in
// squads/application-development/artifacts/checkout/waf-fake-payment-provider/.
//
// Adds the single seam QA's OrderStatusPollingContractTests.cs (GATE-OSP-02)
// needs to verify the deliberate spec deviation: ETag for GET /api/checkout/orders/{id}/status
// is SHA-256 of (orderId, state, paymentState, fulfillmentState) — UpdatedAt is EXCLUDED.
//
// Therefore a heartbeat-style UpdatedAt touch MUST NOT change the ETag and MUST NOT
// bust the 2s polling cache (a11y re-announce protection).
//
// Make CheckoutWebApplicationFactory `partial` in the base file shipped under
// waf-fake-payment-provider/ if it isn't already. No other change required.

using System;
using System.Collections.Concurrent;

namespace TravelAssistant.Api.Tests.Checkout;

public partial class CheckoutWebApplicationFactory
{
    /// <summary>
    /// Bumps only the UpdatedAt timestamp on the seeded order snapshot.
    /// All ETag-contributing fields (state, paymentState, fulfillmentState) are unchanged.
    /// Throws KeyNotFoundException if the order was never seeded — tests should
    /// SeedOrder(...) first.
    /// </summary>
    public void TouchUpdatedAt(string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            throw new ArgumentException("orderId required", nameof(orderId));

        _orders.AddOrUpdate(
            orderId,
            _ => throw new KeyNotFoundException($"Order '{orderId}' not seeded."),
            (_, snap) => snap with { UpdatedAt = DateTimeOffset.UtcNow });
    }
}
