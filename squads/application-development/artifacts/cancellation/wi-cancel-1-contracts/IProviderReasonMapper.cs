// WI-CANCEL-1 provider reason mapping surface — DR-CANCEL-004 R2 (binding).
//
// Per-adapter implementations live in `wi-cancel-1-backend/` (production wire-up
// is gated on SPM v1 100%). This file freezes the contract so QA's
// `ProviderReasonMappingTests.cs` pure-unit drift guard can be authored ahead
// of impl — the test reflects over `MapProviderReason()` against the closed
// `CancelIneligibilityReason` enum (4 values, defined in CancelErrorEnvelope.cs).
//
// Two implementations owed in wi-cancel-1-backend/:
//   - StripeProviderReasonMapper (StripePaymentProviderCancelClient)
//   - AdyenProviderReasonMapper  (AdyenPaymentProviderCancelClient)
//
// Mapping table (DR-CANCEL-004 R2 binding):
//
//   Stripe code                       | Adyen code           | → mapped reason
//   ──────────────────────────────────┼──────────────────────┼─────────────────────────
//   charge_already_refunded           | ALREADY_REFUNDED     | already_refunded
//   charge_already_canceled           | ALREADY_CANCELLED    | already_canceled
//   charge_disputed_in_progress       | ORDER_LOCKED         | fulfillment_in_progress
//   (any other code)                  | (any other code)     | null  ← unmapped
//
// `window_expired` is the 4th enum value but is server-side at POST time only —
// it NEVER originates from a provider webhook. Listed in CancelErrorEnvelope.Codes
// for completeness of the 409 ORDER_NOT_CANCELLABLE{reason} surface.
//
// UNMAPPED RESPONSIBILITY (caller, not mapper):
//   1. Treat as `ProviderCancelOutcome.Unavailable` (NOT `Declined`).
//      Routes to `cancel.rejected_by_provider` → state back to Confirmed →
//      refund eligibility true → rate-cap refunded. Per DR-004 R2: don't
//      brick cancellation on adapter coverage gaps.
//   2. Increment `cancel.failure_reason_unmapped` counter.
//   3. Log `providerReason` to cancel-audit container with discriminator
//      `unmapped_declined_reason`. NEVER serialize to client (GATE-CANCEL-07).

namespace TravelAssistant.Checkout.Cancellation;

public interface IProviderReasonMapper
{
    /// <summary>
    /// Maps a provider-specific failure code to the closed
    /// <see cref="CancelIneligibilityReason"/> enum.
    /// </summary>
    /// <param name="providerCode">Raw provider code (e.g., Stripe
    /// <c>charge_already_refunded</c>, Adyen <c>ALREADY_REFUNDED</c>).</param>
    /// <returns>Mapped reason, or <c>null</c> if the code is not in the
    /// per-adapter mapping table. Callers MUST handle null per
    /// "UNMAPPED RESPONSIBILITY" in the file header.</returns>
    CancelIneligibilityReason? MapProviderReason(string providerCode);
}

/// <summary>
/// Closed 4-value enum mirroring <c>CancelErrorEnvelope.Codes.Reason*</c>
/// snake_case constants. Frozen per DR-CANCEL-002 R1. Precedence (most-specific
/// wins, applied server-side at POST time, NOT in webhook path):
/// already_canceled &gt; already_refunded &gt; window_expired &gt; fulfillment_in_progress.
/// </summary>
public enum CancelIneligibilityReason
{
    AlreadyCanceled,          // "already_canceled"
    AlreadyRefunded,          // "already_refunded"
    WindowExpired,            // "window_expired"            — server-side only, never webhook
    FulfillmentInProgress     // "fulfillment_in_progress"
}
