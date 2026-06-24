// WI-CHECKOUT-1 contracts — DR-CO-001
// Mirrors CancelErrorEnvelope (DR-CANCEL-005) pattern exactly. Single source of truth
// for checkout error codes + reasons. Test trees consume via `using static`; no
// hand-rolled ToSnakeCase projection anywhere in tests/Checkout/.
//
// Ownership: app-dev OWNS Codes.* + Reasons.* + Reasons.ForEnum() projection.
//            QA CONSUMES via Reasons.ForEnum() (no regex anywhere).
//            review-deployment ASSERTS deployed error.code / error.reason ∈ Codes/Reasons exactly.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TravelAssistant.Checkout.Contracts;

public static class CheckoutErrorEnvelope
{
    // -------- Top-level error.code (10 codes, frozen) --------
    public static class Codes
    {
        public const string CartEmpty                = "CART_EMPTY";
        public const string CartChanged              = "CART_CHANGED";              // price/inventory drift between review and confirm
        public const string SessionExpired           = "SESSION_EXPIRED";           // checkout session TTL elapsed
        public const string PaymentDeclined          = "PAYMENT_DECLINED";          // terminal — provider said no
        public const string PaymentRequiresAction    = "PAYMENT_REQUIRES_ACTION";   // 3DS / SCA challenge needed
        public const string InventoryUnavailable     = "INVENTORY_UNAVAILABLE";     // race-lost during confirm
        public const string RequestInFlight          = "REQUEST_IN_FLIGHT";         // duplicate-submit guard (idempotency key already in use)
        public const string IdempotencyKeyConflict   = "IDEMPOTENCY_KEY_CONFLICT";  // same key, different request body
        public const string TaxRecalculated          = "TAX_RECALCULATED";          // forces re-review, NOT silent recalc on confirm
        public const string ProviderUnavailable      = "PROVIDER_UNAVAILABLE";      // retryable — gateway timeout/5xx

        // -------- Inner error.reason (4 values for PAYMENT_DECLINED, frozen) --------
        public static class Reason
        {
            public const string InsufficientFunds      = "insufficient_funds";
            public const string CardExpired            = "card_expired";
            public const string FraudSuspected         = "fraud_suspected";
            public const string DoNotHonor             = "do_not_honor";
        }
    }

    // -------- Reasons projection (mirrors DR-CANCEL-005 commit 3e8df6b shape) --------
    public static class Reasons
    {
        // Server precedence: insufficient_funds > card_expired > fraud_suspected > do_not_honor
        // Zero duplication — points at Codes.Reason.* constants directly.
        public static readonly IReadOnlyList<string> All = ImmutableArray.Create(
            Codes.Reason.InsufficientFunds,
            Codes.Reason.CardExpired,
            Codes.Reason.FraudSuspected,
            Codes.Reason.DoNotHonor);

        // Total function over PaymentDeclineReason. Throws on drift (loud-fail discipline).
        public static string ForEnum(PaymentDeclineReason reason) => reason switch
        {
            PaymentDeclineReason.InsufficientFunds => Codes.Reason.InsufficientFunds,
            PaymentDeclineReason.CardExpired       => Codes.Reason.CardExpired,
            PaymentDeclineReason.FraudSuspected    => Codes.Reason.FraudSuspected,
            PaymentDeclineReason.DoNotHonor        => Codes.Reason.DoNotHonor,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason,
                "Unmapped PaymentDeclineReason — add to Reasons.ForEnum AND Reasons.All AND Codes.Reason.*")
        };
    }

    // -------- Envelope shape mappers --------
    // Flat shape (refunds v1b / cancel sibling-not-nested convention preserved):
    //   { "error": { "code": "PAYMENT_DECLINED", "message": "..." }, "reason": "insufficient_funds" }

    public static object PaymentDeclined(PaymentDeclineReason reason, string message) => new
    {
        error = new { code = Codes.PaymentDeclined, message },
        reason = Reasons.ForEnum(reason)
    };

    public static object RequestInFlight(string operation, int retryAfterSeconds) => new
    {
        error = new { code = Codes.RequestInFlight, message = "A checkout request for this idempotency key is already in flight." },
        operation,            // ONLY place "operation" may appear in checkout responses (mirrors cancel discipline)
        retryAfterSeconds
    };

    public static object CartChanged(string message = "Cart contents changed since review. Please re-review.") => new
    {
        error = new { code = Codes.CartChanged, message }
    };

    public static object TaxRecalculated(decimal previousTotal, decimal newTotal) => new
    {
        error = new { code = Codes.TaxRecalculated, message = "Tax was recalculated. Please re-review the total." },
        previousTotal,
        newTotal
    };

    public static object Envelope(string code, string message) => new
    {
        error = new { code, message }
    };
}

// -------- Decline reason enum (4 values, frozen) --------
public enum PaymentDeclineReason
{
    InsufficientFunds,
    CardExpired,
    FraudSuspected,
    DoNotHonor
}
