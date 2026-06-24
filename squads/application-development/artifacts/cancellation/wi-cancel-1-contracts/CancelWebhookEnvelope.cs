// WI-CANCEL-1 webhook contract surface — DR-CANCEL-004 R1 (binding).
//
// Closed 4-value FailureCode allowlist matching `RefundErrorEnvelope.Codes`
// shape exactly. Drift detection: QA's `EnumerationGuard` reflects over
// `CancelWebhookEnvelope.FailureCodes.All` — adding a code without updating
// the test surface breaks the build on every PR (<50ms, pure unit).
//
// `DeclinedFallback = "DECLINED"` is the unmapped-sentinel and MUST NOT
// appear in `.All`. QA asserts this separately — drift breaks build first.
//
// Webhook event vocabulary (DR-CANCEL-004 R1 binding mapping table):
//
//   ProviderCancelOutcome → Domain event           → State transition       → Refund eligibility → Rate-cap
//   ─────────────────────────────────────────────────────────────────────────────────────────────────────────
//   Accepted              → cancel.accepted        → CancelAccepted→Canceled → n/a                → Spent
//   Pending               → (none — await webhook) → stay CancelRequested    → n/a                → hold
//   Declined              → cancel.declined        → → Confirmed             → NOT set            → Refunded
//   GatewayTimeout        → cancel.rejected_by_provider → → Confirmed        → set true           → Refunded
//   Unavailable           → cancel.rejected_by_provider → → Confirmed        → set true           → Refunded
//
// SEC-CANCEL-001 / GATE-CANCEL-07: `providerReason` and `providerRawCode`
// NEVER serialized to client. cancel-audit container only.

namespace TravelAssistant.Checkout.Cancellation;

public static class CancelWebhookEnvelope
{
    /// <summary>
    /// Closed allowlist of failure codes that MAY appear in `cancel.declined` and
    /// `cancel.rejected_by_provider` webhook payloads. Mirrors
    /// <c>RefundErrorEnvelope.Codes</c> ownership model: app-dev owns the
    /// constants, QA + review-deployment consume via <c>using static</c>, never
    /// duplicate strings anywhere.
    /// </summary>
    public static class FailureCodes
    {
        public const string ProviderDeclined           = "PROVIDER_DECLINED";
        public const string ProviderTimeout            = "PROVIDER_TIMEOUT";
        public const string ProviderUnavailable        = "PROVIDER_UNAVAILABLE";
        public const string AlreadyCapturedAndRefunded = "ALREADY_CAPTURED_AND_REFUNDED";

        /// <summary>
        /// Unmapped-sentinel. Returned when an adapter encounters a provider
        /// failure code it does not recognise. NOT in <c>.All</c> — QA's
        /// drift guard asserts this. Emitting this code MUST also:
        ///   1. Increment <c>cancel.failure_reason_unmapped</c> counter.
        ///   2. Log raw provider code to cancel-audit (NEVER client body).
        /// Routed to <c>cancel.rejected_by_provider</c> per DR-004 R2 (treat
        /// unmapped as <c>Unavailable</c> outcome — don't brick cancellation
        /// on adapter coverage gaps).
        /// </summary>
        public const string DeclinedFallback           = "DECLINED";

        /// <summary>
        /// Closed allowlist for drift detection. <c>DeclinedFallback</c>
        /// intentionally excluded — see field doc-comment.
        /// </summary>
        public static readonly string[] All =
        {
            ProviderDeclined,
            ProviderTimeout,
            ProviderUnavailable,
            AlreadyCapturedAndRefunded
        };
    }

    /// <summary>
    /// Domain webhook event names. Single source of truth — handlers,
    /// dispatcher, telemetry, and QA fixtures all reference these constants.
    /// </summary>
    public static class Events
    {
        public const string CancelAccepted           = "cancel.accepted";
        public const string CancelDeclined           = "cancel.declined";              // DR-CANCEL-004 R1
        public const string CancelRejectedByProvider = "cancel.rejected_by_provider";  // DR-CANCEL-003 R4'
    }
}
