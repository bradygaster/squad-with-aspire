// SPDX-License-Identifier: MIT
// WI-CHECKOUT-1 / SPEC-001 ratification bundle (QA bundle 11) — debug seam additions
//
// Amends CheckoutDebugSeamContract (commit 378604b) with:
//   1. Session-keyed inventory-reservation GET — returns specific reservation row
//      (sibling to existing sku-keyed GET which returns aggregate ledger). Different
//      semantics, both shipped. QA bundle 11 InventoryReservationLifecycleTest binds here.
//   2. Two query-string seams on POST /confirm — pin raw provider response shape
//      before fail-open mapper runs. QA bundle 11 UnmappedProviderReasonFailOpenTest
//      + lifecycle tests bind here.
//
// All env-gated on CHECKOUT_DEBUG=1 read at request time (no IOptions/static cache).
// Unset → 404-NOT-403 short-circuit. ?debug=1 / X-Debug-Mode → 400 (CheckoutDebugSeamContract
// ShouldReject400DebugEscapeHatch unchanged). G6 grep gate enforces.

#nullable enable

namespace TravelAssistant.Api.Checkout.Contracts;

public static class CheckoutDebugSeamContractBundle11
{
    public static class Routes
    {
        /// <summary>
        /// Session-keyed reservation lookup. Sibling to existing
        /// CheckoutDebugSeamContract.Routes.InventoryReservation ({sku}-keyed,
        /// returns aggregate reserved count). This one returns the SPECIFIC
        /// reservation row for the session: {reservationId, ttlExpiresAt, status}.
        /// QA InventoryReservationLifecycleTest binds here.
        /// </summary>
        public const string InventoryReservationBySession = "/_debug/inventory-reservation/by-session/{sessionId}";
    }

    public static class ResponseFields
    {
        // /_debug/inventory-reservation/by-session/{sessionId}
        public const string ReservationId = "reservationId";       // string — server-allocated reservation id
        public const string TtlExpiresAt = "ttlExpiresAt";         // ISO 8601 UTC — fixed at create, NO refresh on retry
        public const string Status = "status";                     // string — "active" | "expired" | "released" | "consumed"
    }

    public static class QuerySeams
    {
        /// <summary>
        /// On POST /api/checkout/{id}/confirm. Forces the provider client to
        /// return this raw reason string before Reasons.MapProviderReason runs.
        /// Used by UnmappedProviderReasonFailOpenTest to prove the default arm
        /// projects unknown reasons to ("failed_retryable", "provider_unknown", true).
        /// Distinct from ForceReasonTestSeam.ParamName="_force_reason" which
        /// pins the ALREADY-COARSENED terminal-decline wire reason.
        /// </summary>
        public const string ForceProviderReason = "_force_provider_reason";

        /// <summary>
        /// On POST /api/checkout/{id}/confirm. Pins provider response state.
        /// Allowed values frozen below; anything else → 400.
        /// Used by InventoryReservationLifecycleTest + reservation-race tests.
        /// </summary>
        public const string ForceProviderState = "_force_provider_state";

        public static class ForceProviderStateValues
        {
            public const string Pending = "pending";       // → ActionRequired or polling continuation
            public const string Confirmed = "confirmed";   // → Confirmed terminal
            public const string Failed = "failed";         // → FailedRetryable unless paired with ?_force_reason terminal value
        }
    }

    public static class StatusValues
    {
        public const string Active = "active";       // reservation held, TTL not yet elapsed
        public const string Expired = "expired";     // TTL elapsed, janitor not yet swept
        public const string Released = "released";   // explicit release on terminal failure
        public const string Consumed = "consumed";   // confirmed terminal → consumed into order
    }
}
