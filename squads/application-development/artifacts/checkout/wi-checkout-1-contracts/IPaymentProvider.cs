// WI-CHECKOUT-1 contracts — DR-CO-002 (Q-CO-3 answer)
// Provider surface: abstract IPaymentProvider with per-adapter impls (Stripe, Adyen).
// Mirrors IPaymentProviderCancelClient (commit bcec51f) + IProviderReasonMapper (commit 2ff14fa) shape exactly.
//
// Hides Stripe payment_intent semantics vs Adyen /payments authorisation flow behind single seam.
// Webhook layer normalizes provider events to single PaymentConfirmOutcome vocabulary.

using System.Threading;
using System.Threading.Tasks;

namespace TravelAssistant.Checkout.Contracts;

public interface IPaymentProvider
{
    Task<ProviderConfirmResult> ConfirmAsync(
        ProviderConfirmRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ProviderConfirmRequest(
    string CheckoutSessionId,
    string IdempotencyKey,                  // forwarded to provider's idempotency header (Stripe Idempotency-Key / Adyen idempotency key)
    decimal AmountMinorUnits,
    string Currency,
    string PaymentMethodToken,              // Stripe payment_method id / Adyen encrypted card data
    string CustomerId);

public sealed record ProviderConfirmResult(
    PaymentConfirmOutcome Outcome,
    string? ProviderTransactionId,          // pi_xxx / Adyen pspReference — server-side only, GATE-CO-04 grep guard
    string? ProviderRequestId,              // for support tickets / audit container only, never client-leak
    PaymentDeclineReason? DeclineReason,    // populated only when Outcome == Declined
    string? RawProviderFailureCode,         // raw provider code (e.g. "insufficient_funds" / "REFUSED") — audit only, MUST go through IProviderDeclineReasonMapper
    bool RequiresAction,                    // 3DS / SCA challenge — when true, Outcome == ActionRequired
    string? ActionRedirectUrl);             // populated only when RequiresAction == true

public enum PaymentConfirmOutcome
{
    Accepted,           // happy path — confirmed
    Declined,           // terminal — provider declined; map RawProviderFailureCode → DeclineReason; if unmapped, fallback PROVIDER_DECLINED unmapped sentinel
    ActionRequired,     // 3DS / SCA — client follows ActionRedirectUrl, then re-polls
    GatewayTimeout,     // retryable — normalizes to PROVIDER_UNAVAILABLE
    Unavailable,        // retryable — provider 5xx / circuit open
    Rejected            // runtime refusal mid-confirm (e.g. fraud_signal_late) — normalizes to payment.rejected, state goes back to review (analog of cancel DR-CANCEL-003 Rejected)
}
