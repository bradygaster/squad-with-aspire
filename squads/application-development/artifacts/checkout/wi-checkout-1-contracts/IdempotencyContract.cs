// WI-CHECKOUT-1 contracts — DR-CO-005 (Q-CO-6 answer)
// Idempotency-Key header contract for POST /api/checkout/{sessionId}/confirm.
//
// Decisions:
//   - Header name: "Idempotency-Key" (RFC 9457 / Stripe convention)
//   - Scope: per-checkout-session-id (NOT per-user, NOT per-cart). Sessions are short-lived (30 min TTL).
//     Same key + same session + same body = cached response replay (200/202 with original Location).
//     Same key + same session + DIFFERENT body = 409 IDEMPOTENCY_KEY_CONFLICT.
//     Same key + DIFFERENT session = treated as new key (sessions are the scope).
//   - Required on confirm endpoint. Missing = 400 with error code BAD_REQUEST + message
//     "Idempotency-Key header is required on confirm."
//   - Cache TTL: 24h after first observation (matches refund idempotency cache shape).
//   - Format: UUID v4 RECOMMENDED but not enforced (16-128 ASCII bytes accepted).
//   - In-flight guard: while a request with key K is being processed, a duplicate K
//     returns 409 REQUEST_IN_FLIGHT with retryAfterSeconds (NOT 200 cached) — same shape
//     as CancelErrorEnvelope.RequestInFlight.

namespace TravelAssistant.Checkout.Contracts;

public static class IdempotencyContract
{
    public const string HeaderName = "Idempotency-Key";

    public const int MinKeyLengthBytes = 16;
    public const int MaxKeyLengthBytes = 128;
    public const int CacheTtlHours = 24;

    /// <summary>
    /// Validates header byte-length per spec. Returns true if length is in [16,128].
    /// Format (UUID vs opaque) is NOT enforced — caller responsibility to use UUID v4.
    /// </summary>
    public static bool IsValidKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        var len = System.Text.Encoding.ASCII.GetByteCount(key);
        return len >= MinKeyLengthBytes && len <= MaxKeyLengthBytes;
    }
}
