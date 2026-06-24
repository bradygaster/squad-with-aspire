// Pre-staged for wi-checkout-1-backend/src/TravelAssistant.Api/Checkout/Security/
// Closes SEC-CHK-008 GATE-CO-06c addendum: AppInsights / Log Analytics customProperty naming
// for full-fidelity internal terminal reasons. The _internal suffix signals downstream
// dashboards (AI-Workbench, Workbooks, exported Power BI) that the field is RBAC-scoped
// to risk/security/finance and MUST NOT be surfaced on product/engineering-broad dashboards.
//
// Pair this with TerminalReasonCoarsening.ToInternalAuditString (full enum fidelity, internal-only)
// and TerminalResponseTimingEqualizer (timing leak floor). Wire-facing telemetry continues to use
// TerminalReasonCoarsening.CoarsenedTerminalReason.DeclinedTerminal as the single coarsened constant.

namespace TravelAssistant.Api.Checkout.Security;

/// <summary>
/// Canonical names for terminal-reason telemetry fields. Wire-facing fields use the coarsened
/// reason. Internal fields carry the "_internal" suffix as the role-gating discipline marker.
/// </summary>
public static class TerminalReasonTelemetryNaming
{
    /// <summary>
    /// Customer-observable / product-dashboard-safe field. Value is always
    /// <see cref="TerminalReasonCoarsening.CoarsenedTerminalReason.DeclinedTerminal"/> ("declined_terminal").
    /// Safe to expose in product analytics, marketing dashboards, customer-facing tooling.
    /// </summary>
    public const string CoarsenedReasonField = "terminal_reason";

    /// <summary>
    /// Internal / RBAC-scoped field carrying full enum fidelity. The "_internal" suffix is
    /// the contract signal to downstream tooling: RBAC-restrict to risk/security/finance.
    /// Use <see cref="TerminalReasonCoarsening.ToInternalAuditString"/> to populate.
    /// </summary>
    public const string InternalReasonField = "terminal_reason_internal";

    /// <summary>
    /// Coarsened p50/p95/p99 timing metric name. Per-enum timing metric is forbidden
    /// (would re-introduce the oracle the equalizer closes). One metric, one reason value.
    /// </summary>
    public const string TimingMetricName = "checkout.terminal_response_timing.declined_terminal";

    /// <summary>
    /// PagerDuty alert threshold above the floor — exceeding this on any percentile
    /// signals a timing-leak regression (equalizer skipped, post-write delay slipped in,
    /// or background work moved onto the hot path).
    /// </summary>
    public const int TimingLeakRegressionThresholdMs = 200;
}
