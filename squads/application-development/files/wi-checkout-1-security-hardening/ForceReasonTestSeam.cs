// Pre-staged for wi-checkout-1-backend/ — test seam for QA's TerminalReasonTimingEqualityTest
// + TerminalReasonWireEqualityTest (SEC-CHK-008 R6, GATE-CO-06e).
//
// Mirrors cancel v1's _debug/cancel-count/{orderId} pattern:
//   - Gated EXCLUSIVELY on env var CHECKOUT_DEBUG=1 (NOT a config setting, NOT
//     IWebHostEnvironment.IsDevelopment, NOT an appsettings flag — env var only,
//     read at request time so flipping it requires process restart)
//   - When CHECKOUT_DEBUG != "1": ?_force_reason query param is ignored AND the
//     ?debug=1 / X-Debug-Mode escape hatches return 400 (R2)
//   - When CHECKOUT_DEBUG == "1": ?_force_reason={hard_decline|fraud_block|
//     insufficient_funds_terminal|provider_rejected_permanent} returns the
//     appropriate terminal envelope WITHOUT contacting Stripe/Adyen
//
// Deployment invariant: CHECKOUT_DEBUG MUST NEVER be set in any deployed environment
// past dev (canary, prod, prod-shadow, perf-test, load-test). Enforced via review-
// deployment's PREPROD-SECURITY-GATE.md GATE-CO-06e environment scan.

using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace TravelAssistant.Api.Checkout.Security;

public sealed class ForceReasonTestSeam
{
    private const string EnvVarName = "CHECKOUT_DEBUG";
    private const string EnabledValue = "1";

    private readonly TerminalResponseTimingEqualizer _equalizer;

    public ForceReasonTestSeam(TerminalResponseTimingEqualizer equalizer)
    {
        _equalizer = equalizer ?? throw new ArgumentNullException(nameof(equalizer));
    }

    public static bool IsEnabled() =>
        string.Equals(
            Environment.GetEnvironmentVariable(EnvVarName),
            EnabledValue,
            StringComparison.Ordinal);

    /// <summary>
    /// If CHECKOUT_DEBUG=1 AND ?_force_reason is present AND parses to a valid
    /// InternalTerminalReason: writes the coarsened terminal envelope after the
    /// timing equalizer has padded to floor + jitter. Returns true if handled.
    /// Otherwise returns false — caller proceeds to real provider call.
    /// </summary>
    public async Task<bool> TryHandleForcedReasonAsync(
        HttpContext ctx,
        Stopwatch elapsed,
        CancellationToken ct)
    {
        if (!IsEnabled())
        {
            // Even if the param is present, refuse silently (don't 400 — that itself
            // would be an oracle: "param recognized → CHECKOUT_DEBUG must be set").
            return false;
        }

        if (!ctx.Request.Query.TryGetValue("_force_reason", out var values) || values.Count == 0)
        {
            return false;
        }

        var raw = values[0];
        if (!TryParseForcedReason(raw, out var reason))
        {
            // CHECKOUT_DEBUG=1 + unknown _force_reason value → 400 (only safe in dev).
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync(
                $"{{\"error\":{{\"code\":\"FORCE_REASON_UNKNOWN\",\"message\":\"Unknown _force_reason value: '{raw}'\"}}}}",
                ct).ConfigureAwait(false);
            return true;
        }

        // PAD BEFORE WRITE — post-write Task.Delay leaks via TCP segment timing on
        // some kestrel configs. Equalizer is the LAST thing before Response.WriteAsync.
        await _equalizer.EqualizeAsync(elapsed, ct).ConfigureAwait(false);

        ctx.Response.StatusCode = StatusCodes.Status200OK;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        // Body: byte-identical regardless of `reason` — that's the point of coarsening.
        // The fact that we KNOW `reason` internally never reaches the wire.
        await ctx.Response.WriteAsync(
            "{\"state\":\"failed_terminal\",\"reason\":\"declined_terminal\",\"retryable\":false}",
            ct).ConfigureAwait(false);

        // Audit log (full enum fidelity, RBAC-scoped sink — never the public response).
        // Mirrors GATE-CANCEL-07 "unmapped_declined_reason" audit pattern.
        // (Wired via ICheckoutAuditLogger in the real backend bundle.)
        return true;
    }

    /// <summary>
    /// Explicit rejection of public debug surface (R2 — no debug field ever, even with
    /// ?debug=1 or X-Debug-Mode header).
    /// </summary>
    public static bool ShouldReject400DebugEscapeHatch(HttpContext ctx)
    {
        if (ctx.Request.Query.ContainsKey("debug")) return true;
        if (ctx.Request.Headers.ContainsKey("X-Debug-Mode")) return true;
        return false;
    }

    private static bool TryParseForcedReason(string raw, out InternalTerminalReason reason)
    {
        reason = raw switch
        {
            "hard_decline" => InternalTerminalReason.HardDecline,
            "fraud_block" => InternalTerminalReason.FraudBlock,
            "insufficient_funds_terminal" => InternalTerminalReason.InsufficientFundsTerminal,
            "provider_rejected_permanent" => InternalTerminalReason.ProviderRejectedPermanent,
            _ => default,
        };
        return reason != default;
    }
}
