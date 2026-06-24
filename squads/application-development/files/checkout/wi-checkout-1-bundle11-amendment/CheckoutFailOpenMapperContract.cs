// SPDX-License-Identifier: MIT
// WI-CHECKOUT-1 / SPEC-001 ratification bundle (QA bundle 11) — app-dev amendment
//
// Pins the fail-open contract for unmapped provider reasons. Mirrors:
//   - refunds v1 DR-REFUND-* unmapped → "refund_unknown" + retryable
//   - cancel v1 DR-CANCEL-004 unmapped → "provider_unknown" + retryable
//
// CRITICAL RULE — DO NOT CHANGE WITHOUT SECURITY-HARDENING SIGN-OFF:
//   The default arm of MapProviderReason MUST project to ("failed_retryable", "provider_unknown", true).
//   Projecting unknown reasons to "failed_terminal" creates:
//     (a) UX dead-end on fixable provider blips (no retry possible)
//     (b) fraud-oracle surface: unknown-reason behavior diverges from known-reason behavior,
//         leaking provider-taxonomy fingerprint to client — see SEC-CHK-008 wire-equality model.
//
// CI gate G6 (QA bundle 11) greps this file + any future CheckoutErrorEnvelope.Reasons.MapProviderReason
// implementation; default arm not matching ("failed_retryable", "provider_unknown", true) → build fails.

#nullable enable
using System.Collections.Generic;

namespace TravelAssistant.Api.Checkout.Contracts;

/// <summary>
/// Frozen contract surface for the fail-open provider-reason mapper. Backend
/// bundle (wi-checkout-1-backend/) implements <see cref="MapProviderReason"/>
/// against the table supplied by IProviderDeclineReasonMapper. This file is
/// the source of truth for the DEFAULT ARM only; per-provider mapping tables
/// live in wi-checkout-1-mappers/.
/// </summary>
public static class CheckoutFailOpenMapperContract
{
    /// <summary>The wire state for any unmapped provider reason.</summary>
    public const string UnmappedState = "failed_retryable";

    /// <summary>The wire reason string for any unmapped provider reason.</summary>
    public const string UnmappedReason = "provider_unknown";

    /// <summary>Unmapped reasons are ALWAYS retryable. Indefinite-hold is prevented by inventory TTL (90s, no refresh).</summary>
    public const bool UnmappedRetryable = true;

    /// <summary>
    /// Reference signature for the fail-open mapper. Backend bundle's
    /// CheckoutErrorEnvelope.Reasons.MapProviderReason MUST match this shape
    /// and MUST use these three constants in the default arm.
    /// </summary>
    public static (string State, string Reason, bool Retryable) MapProviderReasonReference(
        string rawProviderReason,
        IReadOnlyDictionary<string, (string State, string Reason, bool Retryable)> mappingTable)
    {
        return mappingTable.TryGetValue(rawProviderReason, out var mapped)
            ? mapped
            : (UnmappedState, UnmappedReason, UnmappedRetryable);
    }
}

/// <summary>
/// SEC sentinel telemetry constants for unmapped provider reasons.
/// Third use of the reason_unmapped pattern (refund.failure_reason_unmapped,
/// cancel.confirming.reason_unmapped, checkout.confirming.reason_unmapped).
///
/// Per QA bundle 11 Q to security: rawProviderReason field is server-side
/// telemetry (Audit/Risk/Internal/SIEM path), NOT wire-exposed, so it
/// keeps enum fidelity under SEC-CHK-008 R4 carve-out. Coarsening happens
/// at the envelope boundary (MapProviderReason → UnmappedReason), not at
/// the telemetry boundary. Pending final security confirmation.
/// </summary>
public static class CheckoutUnmappedReasonTelemetry
{
    /// <summary>Metric/event name emitted on every default-arm hit.</summary>
    public const string MetricName = "checkout.confirming.reason_unmapped";

    /// <summary>Field name carrying the raw provider string (enum-fidelity, server-side only).</summary>
    public const string RawProviderReasonField = "rawProviderReason";

    /// <summary>Field name carrying the checkout session id.</summary>
    public const string CheckoutSessionIdField = "checkoutSessionId";

    /// <summary>Field name carrying the attempt number (1-based, includes retries).</summary>
    public const string AttemptNumberField = "attemptNumber";

    /// <summary>SEC severity. Lights up the same SIEM dashboard as refund + cancel unmapped events.</summary>
    public const string Severity = "SEC";
}
