// Pre-staged for wi-checkout-1-backend/ — satisfies GATE-CO-06b (SEC-CHK-008 R3).
// Ships with backend bundle when refunds v1 → SPM v1 → cancel v1 hit 100%.
//
// Contract: pads every terminal-reason response to FloorMs + uniform jitter ±50ms,
// BEFORE Response.WriteAsync. Closes the fraud-vs-decline timing oracle that lets
// carding attackers distinguish hard_decline / fraud_block / insufficient_funds_terminal /
// provider_rejected_permanent from response latency alone.
//
// Floor rationale: 800ms is the Stripe + Adyen fraud-engine p95 observed in carding
// defense literature. Tune UP from production data; NEVER tune below 800ms — that
// would re-open the oracle. Adjusting requires security-hardening sign-off.
//
// Jitter rationale: RandomNumberGenerator (CSPRNG), NOT Random.Shared. Random.Shared
// is seeded predictably across process boots; an attacker observing enough samples
// could recover the seed and subtract the jitter, recovering the floor and any
// per-reason residual. CSPRNG forecloses that.

using System.Diagnostics;
using System.Security.Cryptography;

namespace TravelAssistant.Api.Checkout.Security;

public sealed class TerminalResponseTimingEqualizer
{
    public const int FloorMs = 800;
    public const int JitterRangeMs = 100; // ±50

    private static readonly int HalfJitter = JitterRangeMs / 2;

    public async Task EqualizeAsync(Stopwatch elapsed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(elapsed);

        var jitter = RandomNumberGenerator.GetInt32(0, JitterRangeMs) - HalfJitter;
        var targetMs = FloorMs + jitter;
        var delay = targetMs - (int)elapsed.ElapsedMilliseconds;

        if (delay > 0)
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
        // If elapsed already exceeds floor + jitter: do NOT pad further.
        // We're above the leak threshold; additional pad widens the spread, not narrows it.
    }
}
