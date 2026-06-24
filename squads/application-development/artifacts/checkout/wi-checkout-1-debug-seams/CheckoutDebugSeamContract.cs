// WI-CHECKOUT-1 debug seam contract
// Pre-staged for QA bundles 3+4+5 (idempotency / reservation race / tax recalc)
// and exp-design CONFIRM_REJECTED cart-diff bundle 4 seed.
//
// ALL endpoints below MUST be gated on `Environment.GetEnvironmentVariable("CHECKOUT_DEBUG") == "1"`
// READ AT REQUEST TIME (not IOptions, not static cache). Flipping requires process restart.
// In staging/prod, env var MUST be unset; review-deployment GATE-CO-06e scans for it.
// When unset, every endpoint below returns 404 (NOT 403 — 403 confirms the route exists, which is its own oracle).
//
// Mirrors cancel v1 `_debug/cancel-count/{orderId}` pattern (commit 3e8df6b).

namespace TravelAssistant.Api.Checkout.Debug;

/// <summary>
/// Frozen route + response shape contract for checkout debug seams.
/// QA test fixtures bind to these constants — changes here are build-breaking by design.
/// </summary>
public static class CheckoutDebugSeamContract
{
    /// <summary>Env-var gate. Read at request time, never cached.</summary>
    public const string EnvVarName = "CHECKOUT_DEBUG";
    public const string EnvVarEnabledValue = "1";

    public static class Routes
    {
        // QA bundle 4 — InventoryReservationLifecycleTest + CheckoutReservationRaceTest
        public const string InventoryReservation = "/_debug/inventory-reservation/{sku}";
        public const string InventoryLedger = "/_debug/inventory-ledger/{sku}";
        public const string InventoryReservationExpire = "/_debug/inventory-reservation/{sessionId}/expire";
        public const string InventoryReservationJanitorRun = "/_debug/inventory-reservation/janitor/run";

        // QA bundle 3 — CheckoutIdempotencyTest (provider-side call counting for replay assertion)
        public const string ProviderCallCount = "/_debug/provider-call-count/{checkoutSessionId}";

        // QA bundle 5 — CheckoutTaxRecalcTest (force review snapshot age past TTL)
        public const string ReviewSnapshot = "/_debug/review-snapshot/{checkoutSessionId}";
        public const string ReviewSnapshotExpire = "/_debug/review-snapshot/{checkoutSessionId}/expire";

        // QA bundle 1 (cancel v1 parity, kept for symmetry across surfaces — already shipped)
        // NOT a checkout route; included for cross-reference only.
        // public const string CancelCount = "/_debug/cancel-count/{orderId}";

        // exp-design EXP-CHECKOUT-001 bundle 4 — CONFIRM_REJECTED cart-diff seed
        // Seeds `changes[]` on the cart without requiring a full failed-confirm chain.
        public const string CartChangesSeed = "/_debug/cart-changes/{cartId}";

        // Provider artificial-delay knob — bundle 3 in-flight-duplicate test (sub-second windowing)
        // PATCH the value; reset on next request OR via DELETE on the same route.
        public const string ProviderDelay = "/_debug/provider-delay/{checkoutSessionId}";
    }

    /// <summary>Response field names — frozen for QA serializer binding.</summary>
    public static class ResponseFields
    {
        // /_debug/inventory-reservation/{sku}
        public const string Reserved = "reserved";                   // int — total reserved units
        public const string Holders = "holders";                     // array of { orderId, expiresAt }

        // /_debug/inventory-ledger/{sku}
        public const string AcquireCount = "acquireCount";           // int
        public const string ConvertToSaleCount = "convertToSaleCount"; // int
        public const string ReleaseCount = "releaseCount";           // int

        // /_debug/provider-call-count/{checkoutSessionId}
        public const string CallCount = "callCount";                 // int — provider invocations for this session

        // /_debug/review-snapshot/{checkoutSessionId}
        public const string ComputedAt = "computedAt";               // ISO 8601 UTC
        public const string TtlSeconds = "ttlSeconds";               // int — default 60 per DR-CO-006
        public const string Total = "total";                         // long minor units
        public const string TaxTotal = "taxTotal";
        public const string ShippingTotal = "shippingTotal";

        // /_debug/cart-changes/{cartId} (POST request body — also used in response)
        // Body shape matches public `GET /api/cart/{cartId}` `changes[]` contract from DR-CO-EXPDESIGN-ANSWERS-001 Q2.
        public const string Changes = "changes";                     // array of CartChange (see CartChangeContract.cs)
    }
}
