// WI-CHECKOUT-1 contracts — DR-CO-003 (Q-CO-5 answer + QA §5 state machine reply)
// State machine — accepts QA's proposed 7-state machine WITH ONE AMENDMENT:
// adds `action_required` state (3DS/SCA challenge) between `confirming` and terminal states.
//
// Polling: GET /api/checkout/{sessionId}/status is the canonical mirror.
// POST /api/checkout/{sessionId}/confirm returns 202 + Location header → poll endpoint.
// Mirrors cancel polling pattern (5s/60s/12-cap usePollingResource defaults preserved).
// Reuses WebhookEnvelopeEnumerationGuard.v2 Gates 1–8 pattern from DR-CANCEL-005.

namespace TravelAssistant.Checkout.Contracts;

public enum CheckoutState
{
    Cart,                   // items present, not yet checking out
    Shipping,               // shipping address entry
    Payment,                // payment method entry
    Review,                 // review totals + acknowledgement
    Confirming,             // POST /confirm accepted (202), polling /status
    ActionRequired,         // 3DS / SCA challenge in progress — client follows action url, then re-polls
    Confirmed,              // terminal happy — order created, cart cleared, OrderConfirmed event fired
    FailedRetryable,        // terminal-but-retryable (PROVIDER_UNAVAILABLE / GatewayTimeout) — inline retry CTA visible
    FailedTerminal          // terminal-no-retry (PAYMENT_DECLINED / FRAUD_SUSPECTED) — optional alternate-payment CTA
}

public static class CheckoutStateExtensions
{
    public static bool IsTerminal(this CheckoutState state) => state is
        CheckoutState.Confirmed or CheckoutState.FailedTerminal;

    public static bool IsPolling(this CheckoutState state) => state is
        CheckoutState.Confirming or CheckoutState.ActionRequired;

    public static bool AllowsRetry(this CheckoutState state) => state is
        CheckoutState.FailedRetryable;
}
