// Pre-staged for wi-checkout-1-backend/ — satisfies GATE-CO-06a/c/d (SEC-CHK-008 R2/R4/R5).
//
// Coarsens every failed_terminal response — regardless of underlying enum reason —
// to a byte-identical envelope. The 4 underlying enum values
// (HardDecline, FraudBlock, InsufficientFundsTerminal, ProviderRejectedPermanent)
// MUST NEVER appear in any user-observable surface: response body, headers, cookies,
// client telemetry, DOM data attributes, server-rendered HTML.
//
// Full enum fidelity is preserved in:
//   - Audit log (.squad/orchestration-log / SIEM)
//   - Risk/finance/security data warehouse feed (RBAC-scoped: risk/security/finance ROLE-ONLY)
//   - Application Insights customProperties.terminal_reason_internal (RBAC-scoped to engineering)
//
// PRODUCT/ENGINEERING DASHBOARDS, SEGMENT/AMPLITUDE/GA4 FEEDS, AND ANY CLIENT-SDK-VISIBLE
// SURFACE MUST RECEIVE ONLY "declined_terminal". Enforcement: GATE-CO-06c grep against dist/
// (mirrors GATE-CANCEL-07 — provider-id-leak guard).

using System.Collections.Frozen;

namespace TravelAssistant.Api.Checkout.Security;

/// <summary>
/// Internal-only enum. NEVER serialized to wire, NEVER projected to client telemetry,
/// NEVER emitted as a DOM data-reason attribute. Coarsened to <see cref="CoarsenedTerminalReason.DeclinedTerminal"/>
/// at every public boundary via <see cref="TerminalReasonCoarsening.ToWire"/>.
/// </summary>
public enum InternalTerminalReason
{
    HardDecline = 1,
    FraudBlock = 2,
    InsufficientFundsTerminal = 3,
    ProviderRejectedPermanent = 4,
}

/// <summary>
/// Public-facing reason set. Exactly one value. Build-time guarantee that no per-enum
/// distinction can leak through the wire — there's only one constant to emit.
/// </summary>
public static class CoarsenedTerminalReason
{
    public const string DeclinedTerminal = "declined_terminal";

    public static readonly IReadOnlyList<string> All = new[] { DeclinedTerminal };
}

public static class TerminalReasonCoarsening
{
    // Frozen at compile/init time. Every InternalTerminalReason MUST map to the single
    // public constant. Adding a new InternalTerminalReason without updating this dict
    // → KeyNotFoundException at runtime → caught by GATE-CO-06a wire-equality test.
    private static readonly FrozenDictionary<InternalTerminalReason, string> WireMap =
        new Dictionary<InternalTerminalReason, string>
        {
            [InternalTerminalReason.HardDecline] = CoarsenedTerminalReason.DeclinedTerminal,
            [InternalTerminalReason.FraudBlock] = CoarsenedTerminalReason.DeclinedTerminal,
            [InternalTerminalReason.InsufficientFundsTerminal] = CoarsenedTerminalReason.DeclinedTerminal,
            [InternalTerminalReason.ProviderRejectedPermanent] = CoarsenedTerminalReason.DeclinedTerminal,
        }.ToFrozenDictionary();

    public static string ToWire(InternalTerminalReason reason)
    {
        if (!WireMap.TryGetValue(reason, out var coarsened))
        {
            // Loud-fail-on-drift: a new InternalTerminalReason without WireMap entry
            // would silently leak its enum name to ToString(). We refuse instead.
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "InternalTerminalReason missing from WireMap. Add entry mapping to CoarsenedTerminalReason.DeclinedTerminal before deploy.");
        }
        return coarsened;
    }

    /// <summary>
    /// Internal SIEM / risk-feed projection. Returns full enum fidelity.
    /// CALLERS MUST GATE ON RBAC ROLE = risk|security|finance|engineering BEFORE INVOKING.
    /// Never invoke from a public endpoint handler or from a client-bound telemetry emitter.
    /// </summary>
    public static string ToInternalAuditString(InternalTerminalReason reason) => reason switch
    {
        InternalTerminalReason.HardDecline => "hard_decline",
        InternalTerminalReason.FraudBlock => "fraud_block",
        InternalTerminalReason.InsufficientFundsTerminal => "insufficient_funds_terminal",
        InternalTerminalReason.ProviderRejectedPermanent => "provider_rejected_permanent",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}

/// <summary>
/// Canonical wire shape for failed_terminal. Byte-identical regardless of underlying reason.
/// Status=200, no extra headers, no varying cookies, no providerReason/internalReason/debug fields.
/// Retryable=false constant — terminal is terminal.
/// </summary>
public sealed class FailedTerminalEnvelope
{
    public string State { get; init; } = "failed_terminal";
    public string Reason { get; init; } = CoarsenedTerminalReason.DeclinedTerminal;
    public bool Retryable { get; init; } = false;
}

/// <summary>
/// Retry-After constant for terminal-path 429s (GATE-CO-06d R5).
/// 60 seconds for BOTH sub-cap and IP-cap paths. No per-reason backoff differential.
/// </summary>
public static class TerminalRetryAfter
{
    public const int Seconds = 60;
}
