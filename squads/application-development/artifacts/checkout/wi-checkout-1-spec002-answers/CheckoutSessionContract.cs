// DR-CO-008 — Session TTL contract for /api/checkout/{cartId} surfaces.
// Resolves QA Q-CO-DEV-2 (checkoutSessionId TTL). Model: fixed-window absolute
// expiry from cart creation, NOT sliding-on-activity. Predictable bounds for
// QA bundle 8 (session-expiry) — exactly 4 boundary tests, not 8.

namespace TravelAssistant.Checkout.Contracts;

public static class CheckoutSessionContract
{
    /// <summary>30 minutes from cart creation. Same value as ideation R3 reservation 90s × 20 retry budget.</summary>
    public const int TtlMinutes = 30;

    /// <summary>Fixed-window absolute expiry. Activity does NOT extend the window.</summary>
    public const string ExpiryModel = "fixed-window";

    /// <summary>Server returns 410 Gone with this code when cart > TTL.</summary>
    public const string ExpiredCode = "CHECKOUT_SESSION_EXPIRED";

    /// <summary>Confirmation page reachable post-TTL when order is already in Confirmed state — order id, not cart id, gates that surface.</summary>
    public const bool ConfirmationReachablePostExpiry = true;

    /// <summary>Server emits this header on every /api/checkout/{cartId} response so the client can compute its own countdown without a clock-skew round-trip.</summary>
    public const string ExpiryHeader = "X-Checkout-Session-Expires-At";
}

/// <summary>
/// DR-CO-009 — Place Order idempotency token contract.
/// Resolves QA Q-CO-DEV-3. Server-issued single-use token (option (b)) — same
/// pattern as cancelToken in DR-CANCEL-002. Retry-safe by construction.
/// </summary>
public static class PlaceOrderTokenContract
{
    /// <summary>GET /api/checkout/{cartId}/review returns a placeOrderToken in the reviewSnapshot payload.</summary>
    public const string ReviewSnapshotField = "placeOrderToken";

    /// <summary>POST /api/checkout/{cartId}/confirm body MUST include this field. 400 PLACE_ORDER_TOKEN_REQUIRED otherwise.</summary>
    public const string ConfirmBodyField = "placeOrderToken";

    /// <summary>Token is single-use. Server-side cache scoped per-cart. Reuse → 409 PLACE_ORDER_TOKEN_CONSUMED.</summary>
    public const string ConsumedCode = "PLACE_ORDER_TOKEN_CONSUMED";

    /// <summary>409 TAX_RECALCULATED or 409 CART_CHANGED → server issues a new token in the re-review payload. Client MUST NOT reuse the old token.</summary>
    public const bool RefreshOnReReview = true;

    /// <summary>16-byte URL-safe base64, opaque to client.</summary>
    public const int TokenByteLength = 16;
}
