// DR-CO-007 amendment — 3DS telemetry/contract answers to exp-design Q1/Q2/Q3.
// Pairs with CheckoutErrorEnvelope (c80c3e4) + CheckoutDebugSeamContract (378604b).
// Frozen contract surface — wi-checkout-1-backend/ binds to these constants.

namespace TravelAssistant.Api.Checkout.Contracts;

public static class ThreeDsContract
{
    // === Q1: redirectUrl shape (binding) ===
    // Always absolute (provider domain — Stripe hooks.stripe.com, Adyen
    // checkoutshopper-live.adyen.com). Never same-origin. Frontend may
    // assert StartsWith("https://") and reject otherwise without a same-origin
    // guard branch — kept simple by contract.
    public const bool RedirectUrlAlwaysAbsolute = true;
    public const string RedirectUrlSchemeRequired = "https://";

    // === Q2: returnUrl echo on GET /status (binding) ===
    // Do NOT echo. Frontend already navigated to the URL it constructed; the
    // server has no value to add by repeating it, and echoing costs a wire
    // field on every poll (5s cadence, up to 12 polls). If a provider rewrite
    // happens, the rewritten URL lives in client-side telemetry already via
    // checkout_3ds_redirected.redirectDelayMs anomaly detection.
    public const bool ReturnUrlEchoedOnStatusPoll = false;

    // === Q3: three_ds_abandoned vs three_ds_failed (binding) ===
    // Ship as distinct enum values into PaymentDeclineReason + Reasons.All.
    // Both project to the same coarsened DOM data-reason
    // ("three_ds_failed_or_abandoned") — exp-design's preferred default,
    // matching the failed_terminal coarsening pattern. Full fidelity preserved
    // server-side for funnel analytics; DOM stays cognitively parallel.
    //
    // NOT a fraud-oracle surface — these signal user/provider lifecycle, not
    // payment-method risk — so the SEC-CHK-008 wire-byte-equality discipline
    // does NOT apply here. Distinct wire enum values are safe.
    public const string CoarsenedDomDataReason = "three_ds_failed_or_abandoned";
}

public enum ThreeDsOutcome
{
    Succeeded,
    Abandoned,        // user back-button / closed browser / timed out client-side
    Failed,           // provider returned challenge-failed
    TimedOutAtProvider // provider-side timeout, distinct from client abandon
}
