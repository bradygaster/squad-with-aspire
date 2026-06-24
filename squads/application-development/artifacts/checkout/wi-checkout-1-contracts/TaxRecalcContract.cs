// WI-CHECKOUT-1 contracts — DR-CO-006 (Q-CO-10 answer)
// Tax recalculation timing — NO SILENT RECALC ON CONFIRM. Adopts QA's strong recommendation.
//
// Recalc points:
//   1. On shipping-address entry transition (cart → shipping). Tax computed from shipping postal code.
//      Result stored on session.
//   2. On review-screen render. Tax displayed prominently with disclaimer.
//      Result re-validated server-side (idempotent recompute) — if delta > 1¢, returns
//      409 TAX_RECALCULATED with previousTotal + newTotal. Frontend forces re-review screen,
//      re-acknowledge required before confirm becomes available again.
//   3. On confirm. Server validates `expectedTotal` request field against current computed total.
//      If mismatch, returns 409 TAX_RECALCULATED. NEVER silently adjusts.
//
// Confirm request body MUST include `expectedTotalMinorUnits` echoed from review.
// Server compares with currently-computed total. Drift → 409, NOT silent debit.

namespace TravelAssistant.Checkout.Contracts;

public sealed record ConfirmRequest(
    string CheckoutSessionId,
    string PaymentMethodToken,
    decimal ExpectedTotalMinorUnits,         // echoed from review screen — server validates
    string Currency);

public static class TaxRecalcContract
{
    /// <summary>
    /// Tolerance for "no drift" — totals within this delta are accepted as equal.
    /// Set to 0 minor units (1 cent) — any drift forces re-review.
    /// </summary>
    public const decimal DriftToleranceMinorUnits = 0m;

    public static bool RequiresReReview(decimal previousTotal, decimal currentTotal)
        => System.Math.Abs(previousTotal - currentTotal) > DriftToleranceMinorUnits;
}
