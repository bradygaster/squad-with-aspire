// WI-CANCEL-1 contract surface (frozen per DR-CANCEL-001 R4).
//
// Provider variance (Stripe payment_intent.cancel vs Adyen refund-for-auth-capture)
// is hidden ENTIRELY behind this adapter. Webhook normalizes provider events to a
// single domain event: `cancel.accepted`.
//
// SEC-CANCEL-001: `CancelType` (Void|Refund) MUST NOT leak to clients. Internal
// telemetry/audit only. GATE-CANCEL-06 enforces via grep on serialized response
// bodies.

namespace TravelAssistant.Checkout.Cancellation;

public interface IPaymentProviderCancelClient
{
    Task<ProviderCancelResult> CancelAsync(
        string providerPaymentIntentId,
        CancellationToken ct);
}

public sealed record ProviderCancelResult(
    ProviderCancelOutcome Outcome,
    CancelType CancelType,                  // INTERNAL ONLY — never serialize to client
    string? ProviderRefundId,               // INTERNAL ONLY (SEC-RFD-001 pattern: re_xxx never serialized)
    string? FailureCode,                    // mapped per webhook allowlist when Outcome=Declined
    string? ProviderRequestId);             // for audit trail

public enum ProviderCancelOutcome
{
    Accepted,        // → domain event cancel.accepted
    Pending,         // async; await webhook
    Declined,        // terminal; ineligible at provider (e.g., already captured + refunded)
    GatewayTimeout,  // retryable
    Unavailable      // retryable
}

public enum CancelType
{
    Void,    // pre-capture (Stripe payment_intent.cancel, Adyen technical cancel)
    Refund   // post-capture (Stripe refunds.create full, Adyen refund-for-payment)
}
