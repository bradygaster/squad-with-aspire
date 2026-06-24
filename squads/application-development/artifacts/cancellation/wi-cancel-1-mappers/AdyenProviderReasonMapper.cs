// WI-CANCEL-1 provider reason mapper — Adyen adapter.
//
// Pure-function implementation of IProviderReasonMapper for Adyen failure codes.
// No I/O, no DI, no config — safe to instantiate as a singleton or static call site.
// Wired into AdyenPaymentProviderCancelClient in wi-cancel-1-backend/ once SPM v1 hits 100%.
//
// Mapping table is the SINGLE SOURCE OF TRUTH for Adyen → CancelIneligibilityReason.
// Per DR-CANCEL-004 R2 (binding): the table is closed. Any new Adyen code MUST
// arrive as a DR amendment + ratification — never silently extended in adapter code.
//
// Adyen-specific note: Adyen failure codes arrive UPPERCASE (e.g., ALREADY_REFUNDED)
// in the /cancels and notification webhook payloads. Stripe codes arrive lowercase.
// We do NOT cross-normalize — each adapter owns its own provider's case convention.

using System.Collections.Frozen;
using TravelAssistant.Checkout.Cancellation;

namespace TravelAssistant.Checkout.Cancellation.Providers.Adyen;

public sealed class AdyenProviderReasonMapper : IProviderReasonMapper
{
    // FrozenDictionary: zero-allocation lookup, immutable after construction.
    // Mapping table is exhaustive per DR-CANCEL-004 R2. Any code not in this
    // table returns null and the caller routes through the unmapped path
    // (cancel.failure_reason_unmapped counter + cancel-audit log + treat as
    // ProviderCancelOutcome.Unavailable). See IProviderReasonMapper.cs header.
    private static readonly FrozenDictionary<string, CancelIneligibilityReason> AdyenMap =
        new Dictionary<string, CancelIneligibilityReason>(StringComparer.Ordinal)
        {
            ["ALREADY_REFUNDED"]  = CancelIneligibilityReason.AlreadyRefunded,
            ["ALREADY_CANCELLED"] = CancelIneligibilityReason.AlreadyCanceled,
            ["ORDER_LOCKED"]      = CancelIneligibilityReason.FulfillmentInProgress,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public CancelIneligibilityReason? MapProviderReason(string providerCode)
    {
        // Case-sensitive Ordinal match. Adyen codes are UPPERCASE SNAKE_CASE
        // by API contract; a mismatched-case incoming code is an unmapped code
        // by definition. Adyen has historically shipped both ALREADY_CANCELLED
        // (British spelling) and never ALREADY_CANCELED (US spelling) — we map
        // the exact wire value. If Adyen ever ships a US-spelling variant it
        // arrives as a DR amendment + new table row, not as a fuzzy match here.
        if (string.IsNullOrEmpty(providerCode))
        {
            return null;
        }

        return AdyenMap.TryGetValue(providerCode, out var reason)
            ? reason
            : null;
    }

    /// <summary>
    /// Read-only view of the mapping table for diagnostic + test surfaces.
    /// QA's ProviderReasonMappingTests.cs uses this to assert exhaustive
    /// coverage of expected codes without duplicating the table.
    /// </summary>
    public static IReadOnlyDictionary<string, CancelIneligibilityReason> MappingTable => AdyenMap;
}
