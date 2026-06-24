// WI-CHECKOUT-1 contracts — DR-CO-002 companion (Q-CO-3 answer)
// Per-adapter raw-provider-code → PaymentDeclineReason mapper.
// Mirrors IProviderReasonMapper (cancel commit 2ff14fa) + Stripe/Adyen impl pair (commit 06873f7) exactly.
//
// Per-adapter impls (StripeProviderDeclineReasonMapper, AdyenProviderDeclineReasonMapper)
// ship in wi-checkout-1-mappers/ bundle alongside this seam.
// Each owns its provider's case convention — Stripe snake_case, Adyen UPPERCASE_SNAKE_CASE.
// No cross-normalization — silent normalization masks provider API drift.

using System.Collections.Generic;

namespace TravelAssistant.Checkout.Contracts;

public interface IProviderDeclineReasonMapper
{
    /// <summary>
    /// Maps a raw provider failure code to a PaymentDeclineReason enum value.
    /// Returns null when the code is unmapped (caller MUST treat as PROVIDER_DECLINED unmapped sentinel,
    /// increment payment.failure_reason_unmapped telemetry, log raw code to payment-audit container ONLY).
    /// </summary>
    PaymentDeclineReason? MapDeclineReason(string? providerCode);

    /// <summary>
    /// Exposes the underlying mapping table as read-only view for QA exhaustive-coverage tests.
    /// Same pattern as IProviderReasonMapper.MappingTable (cancel commit 06873f7).
    /// </summary>
    IReadOnlyDictionary<string, PaymentDeclineReason> MappingTable { get; }
}
