// WI-CHECKOUT-1 contracts — DR-CO-004 (Q-CO-5 polling + webhook vocab)
// Single source of truth for checkout webhook event names + failure codes.
// Mirrors CancelWebhookEnvelope (DR-CANCEL-004 commit 2ff14fa) shape exactly.

using System.Collections.Generic;
using System.Collections.Immutable;

namespace TravelAssistant.Checkout.Contracts;

public static class CheckoutWebhookEnvelope
{
    // -------- Event vocabulary (3 events, frozen) --------
    public static class Events
    {
        public const string PaymentAccepted          = "payment.accepted";
        public const string PaymentDeclined          = "payment.declined";              // terminal — provider declined
        public const string PaymentRejectedByProvider = "payment.rejected_by_provider"; // retryable — gateway/unavailable, state returns to review

        public static readonly IReadOnlyList<string> All = ImmutableArray.Create(
            PaymentAccepted, PaymentDeclined, PaymentRejectedByProvider);
    }

    // -------- Failure code allowlist (4 codes, frozen) --------
    public static class FailureCodes
    {
        public const string ProviderDeclined         = "PROVIDER_DECLINED";
        public const string ProviderTimeout          = "PROVIDER_TIMEOUT";
        public const string ProviderUnavailable      = "PROVIDER_UNAVAILABLE";
        public const string FraudSuspected           = "FRAUD_SUSPECTED";

        // DeclinedFallback="DECLINED" deliberately excluded — unmapped sentinel discipline.
        public static readonly IReadOnlyList<string> All = ImmutableArray.Create(
            ProviderDeclined, ProviderTimeout, ProviderUnavailable, FraudSuspected);

        public const string DeclinedFallback = "DECLINED"; // unmapped sentinel, NOT in .All — triggers payment.failure_reason_unmapped telemetry
    }
}
