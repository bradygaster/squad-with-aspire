// WI-CANCEL-1 provider reason mapper — Stripe adapter.
//
// Pure-function implementation of IProviderReasonMapper for Stripe failure codes.
// No I/O, no DI, no config — safe to instantiate as a singleton or static call site.
// Wired into StripePaymentProviderCancelClient in wi-cancel-1-backend/ once SPM v1 hits 100%.
//
// Mapping table is the SINGLE SOURCE OF TRUTH for Stripe → CancelIneligibilityReason.
// Per DR-CANCEL-004 R2 (binding): the table is closed. Any new Stripe code MUST
// arrive as a DR amendment + ratification — never silently extended in adapter code.
//
// QA enforcement: ProviderReasonMappingTests.cs reflects over this class against
// the frozen 4-value enum + the unmapped-returns-null contract. Drift fails CI.

using System.Collections.Frozen;
using TravelAssistant.Checkout.Cancellation;

namespace TravelAssistant.Checkout.Cancellation.Providers.Stripe;

public sealed class StripeProviderReasonMapper : IProviderReasonMapper
{
    // FrozenDictionary: zero-allocation lookup, immutable after construction.
    // Mapping table is exhaustive per DR-CANCEL-004 R2. Any code not in this
    // table returns null and the caller routes through the unmapped path
    // (cancel.failure_reason_unmapped counter + cancel-audit log + treat as
    // ProviderCancelOutcome.Unavailable). See IProviderReasonMapper.cs header.
    private static readonly FrozenDictionary<string, CancelIneligibilityReason> StripeMap =
        new Dictionary<string, CancelIneligibilityReason>(StringComparer.Ordinal)
        {
            ["charge_already_refunded"]     = CancelIneligibilityReason.AlreadyRefunded,
            ["charge_already_canceled"]     = CancelIneligibilityReason.AlreadyCanceled,
            ["charge_disputed_in_progress"] = CancelIneligibilityReason.FulfillmentInProgress,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public CancelIneligibilityReason? MapProviderReason(string providerCode)
    {
        // Case-sensitive Ordinal match. Stripe codes are lowercase snake_case
        // by API contract; a mismatched-case incoming code is an unmapped code
        // by definition and routes through the unmapped path. Do not normalize —
        // silent normalization would mask provider API drift from observability.
        if (string.IsNullOrEmpty(providerCode))
        {
            return null;
        }

        return StripeMap.TryGetValue(providerCode, out var reason)
            ? reason
            : null;
    }

    /// <summary>
    /// Read-only view of the mapping table for diagnostic + test surfaces.
    /// QA's ProviderReasonMappingTests.cs uses this to assert exhaustive
    /// coverage of expected codes without duplicating the table.
    /// </summary>
    public static IReadOnlyDictionary<string, CancelIneligibilityReason> MappingTable => StripeMap;
}
