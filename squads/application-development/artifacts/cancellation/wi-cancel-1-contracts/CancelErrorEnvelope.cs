// WI-CANCEL-1 error envelope (frozen per DR-CANCEL-002 R1+R3).
//
// Dev seam #6 owed by app-dev: app-dev OWNS Codes constants + NotCancellable(reason) mapper.
// QA + review-deployment consume from here — NO string duplication anywhere (refunds v1b pattern).
//
// Wire shape matches refunds v1b (UX freeze 4c84355): nested error object with `code` discriminator.
// Frontend parses error.code; flat lowercase strings deliberately rejected.
//
//   { "error": { "code": "...", "message": "..." }, ...siblings }
//
// SEC-CANCEL-001: NEVER include CancelType, ProviderRefundId, providerReason, or any
// re_xxx / pi_xxx / cancel_xxx provider IDs. GATE-CANCEL-06 + GATE-CANCEL-07 enforce.

namespace TravelAssistant.Checkout.Cancellation;

public static class CancelErrorEnvelope
{
    /// <summary>
    /// Frozen error code set. NO additions without a new DR. QA + review-deployment
    /// consume these constants directly — never duplicate as string literals.
    /// </summary>
    public static class Codes
    {
        // DR-CANCEL-002 R1 — ORDER_NOT_CANCELLABLE.reason closed 4-value enum.
        // Server evaluates in precedence order; returns first match.
        // Spelling is contract — case-sensitive, snake_case.
        public const string ReasonAlreadyCanceled         = "already_canceled";
        public const string ReasonAlreadyRefunded         = "already_refunded";
        public const string ReasonWindowExpired           = "window_expired";
        public const string ReasonFulfillmentInProgress   = "fulfillment_in_progress";

        // Top-level error.code values (UPPER_SNAKE per refunds v1b convention).
        public const string OrderNotCancellable           = "ORDER_NOT_CANCELLABLE";
        public const string RequestInFlight               = "REQUEST_IN_FLIGHT";              // DR-CANCEL-002 R3
        public const string IdempotencyKeyRequired        = "IDEMPOTENCY_KEY_REQUIRED";
        public const string IdempotencyBodyMismatch       = "IDEMPOTENCY_BODY_MISMATCH";
        public const string RateLimited                   = "RATE_LIMITED";
        public const string MalformedJson                 = "MALFORMED_JSON";
        public const string OrderIdRequired               = "ORDER_ID_REQUIRED";
        public const string Unauthorized                  = "UNAUTHORIZED";
        public const string OrderNotFound                 = "ORDER_NOT_FOUND";                // Pending orders → 404 (IDOR-safe, R1)
    }

    /// <summary>
    /// 409 ORDER_NOT_CANCELLABLE mapper. `reason` MUST be one of the 4 Codes.Reason* constants.
    /// Server-side precedence (caller responsibility): AlreadyCanceled > AlreadyRefunded
    /// > WindowExpired > FulfillmentInProgress. Returns first matching reason.
    /// </summary>
    public static object NotCancellable(string reason)
    {
        return new
        {
            error = new
            {
                code = Codes.OrderNotCancellable,
                message = MessageForReason(reason),
            },
            reason = reason,  // sibling — preserves refunds v1b decision (frontend convenience read)
        };
    }

    /// <summary>
    /// 409 REQUEST_IN_FLIGHT mapper for cancel-during-refund-pending (DR-CANCEL-002 R3).
    /// Distinct from ORDER_NOT_CANCELLABLE{already_refunded} which only fires post-settle.
    /// `operation` is the only field where "operation" appears in any cancel response —
    /// cancel NEVER leaks CancelType.
    /// </summary>
    public static object RequestInFlight(string operation = "refund", int retryAfterSeconds = 30)
    {
        return new
        {
            error = new
            {
                code = Codes.RequestInFlight,
                message = $"A {operation} is currently in flight for this order. Retry after it settles.",
            },
            operation = operation,
            retryAfterSeconds = retryAfterSeconds,
        };
    }

    /// <summary>Generic envelope for non-eligibility errors. Use Codes.* constants.</summary>
    public static object Envelope(string code, string message)
    {
        return new
        {
            error = new { code, message },
        };
    }

    private static string MessageForReason(string reason) => reason switch
    {
        Codes.ReasonAlreadyCanceled       => "This order has already been canceled.",
        Codes.ReasonAlreadyRefunded       => "This order has already been refunded and cannot be canceled.",
        Codes.ReasonWindowExpired         => "The 60-minute cancellation window has expired.",
        Codes.ReasonFulfillmentInProgress => "This order is being prepared for shipment and can no longer be canceled.",
        _ => throw new ArgumentException(
                $"Unknown cancellation reason '{reason}'. Use CancelErrorEnvelope.Codes.Reason* constants.",
                nameof(reason)),
    };
}
