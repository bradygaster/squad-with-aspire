# DR-CO-RECONCILIATION-001 — SEC-CHK-008 / GATE-CO-06a-d implementation

**Bundle:** wi-checkout-1-security-hardening/
**Branch:** tamir/squad-fixes
**Status:** Pre-staged. Ships with `wi-checkout-1-backend/` when refunds v1 → SPM v1 → cancel v1 hit 100%.
**Reconciliation owed against existing surface:** NONE — checkout backend not yet implemented (only contracts at c80c3e4 + mappers at 06873f7 + spec answers at bbc2faa exist). These files become the canonical implementation when the backend bundle lands; no patch-after-the-fact required.

## Asks satisfied

| Gate | Ask | File | Notes |
|------|-----|------|-------|
| GATE-CO-06a | HTTP wire-equality (R2) — byte-identical response across 4 terminal reasons | `TerminalReasonCoarsening.cs` (`FailedTerminalEnvelope`) | One envelope type, one `Reason` constant. No per-reason header, cookie, or body field. No `providerReason`/`internalReason`/debug field ever. |
| GATE-CO-06b | Server-side timing equalization ≥800ms floor + ±50ms jitter (R3) | `TerminalResponseTimingEqualizer.cs` | `RandomNumberGenerator.GetInt32` (CSPRNG, not `Random.Shared`). Pad BEFORE `WriteAsync`. If elapsed > floor + jitter, do not pad further (would widen spread). |
| GATE-CO-06c | Telemetry coarsening extension (R4) | `TerminalReasonCoarsening.cs` (`ToWire` vs `ToInternalAuditString`) | Public boundary → `declined_terminal`. Internal SIEM/risk feed → full enum (`ToInternalAuditString`). RBAC gating is the CALLER's responsibility — comment block warns explicitly. AI customProperties dim renamed `terminal_reason_internal` per R4 guidance. |
| GATE-CO-06d | `Retry-After` constant + T13 weight equality (R5) | `TerminalReasonCoarsening.cs` (`TerminalRetryAfter.Seconds = 60`) | Single constant. T13 increment-by-1 applies uniformly — enforced in T13 wiring code in the backend bundle, not here. |
| Test seam for QA (R6 / GATE-CO-06e) | `?_force_reason=` gated on `CHECKOUT_DEBUG=1` env var | `ForceReasonTestSeam.cs` | Env var read at request time, not config. Even WITH CHECKOUT_DEBUG=1, equalizer fires before `WriteAsync`. Unknown forced reason → 400 with `FORCE_REASON_UNKNOWN` code. Forced reason absent OR CHECKOUT_DEBUG=0 → returns false, caller proceeds to real provider call. |

## Non-asks deliberately scoped OUT

- **Real Stripe/Adyen provider impls** — `IPaymentProvider` interface lives in c80c3e4 contracts; impls land in `wi-checkout-1-backend/` after SPM v1 100%.
- **Endpoint wiring** — `POST /api/checkout/{id}/confirm` handler lands in backend bundle and CALLS `_forceReasonSeam.TryHandleForcedReasonAsync(...)` first, then real provider call. Endpoint also calls `ForceReasonTestSeam.ShouldReject400DebugEscapeHatch(ctx)` to enforce R2 no-debug-field rule.
- **Background work (analytics emit, audit log write, refund eligibility precompute)** moved off hot path — channel/IHostedService queue wired in backend bundle. R3 happens-after-timing edge case noted in comment block; equalizer alone is not sufficient without this.
- **T13 counter wiring** — increment-by-1 weight equality is enforced where T13 counters live (separate file in cancel v1 / refunds v1 lineage). Constant `TerminalRetryAfter.Seconds = 60` is exported here for the cap handler to import.

## Build-time properties

- `FailedTerminalEnvelope` has exactly ONE valid serialization: `{"state":"failed_terminal","reason":"declined_terminal","retryable":false}`. Tests assert this via `JsonSerializer.Serialize(new FailedTerminalEnvelope())` equality.
- `TerminalReasonCoarsening.ToWire` is a TOTAL function over `InternalTerminalReason` — every enum value maps to `CoarsenedTerminalReason.DeclinedTerminal`. Adding a new internal enum value without updating `WireMap` throws `ArgumentOutOfRangeException` (loud-fail-on-drift, mirrors `CancelErrorEnvelope.Reasons.ForEnum` discipline from DR-CANCEL-005).
- `CoarsenedTerminalReason.All.Count == 1` — single public reason. Enumeration guard test asserts this; any drift (e.g., someone adds `CoarsenedTerminalReason.DeclinedTerminalFraud` thinking it's "still coarsened") breaks the build.

## Ownership boundary (refunds v1b model)

- **app-dev OWNS:** `TerminalResponseTimingEqualizer`, `TerminalReasonCoarsening`, `FailedTerminalEnvelope`, `ForceReasonTestSeam`, `InternalTerminalReason` (internal-only), `CoarsenedTerminalReason`, `TerminalRetryAfter`.
- **QA CONSUMES via:** `using static CoarsenedTerminalReason` for `DeclinedTerminal`; calls `?_force_reason=` seam in `TerminalReasonTimingEqualityTest` + `TerminalReasonWireEqualityTest`; asserts `TerminalResponseTimingEqualizer.FloorMs == 800` symbolically (no magic number duplication).
- **review-deployment ASSERTS:** PREPROD-SECURITY-GATE.md GATE-CO-06e environment scan — `CHECKOUT_DEBUG` env var MUST be unset in canary, prod, prod-shadow, perf-test, load-test. Build-break on PR setting it in any deploy manifest.
- **security-hardening AUDITS:** the 4-row binding (`InternalTerminalReason` enum count == `WireMap` count == 4; `WireMap` values all == `CoarsenedTerminalReason.DeclinedTerminal`; `CoarsenedTerminalReason.All.Count == 1`; equalizer floor == 800ms NEVER lowered without sign-off).

## Apply order in backend bundle

1. Drop these 3 files into `src/TravelAssistant.Api/Checkout/Security/`.
2. DI: register `TerminalResponseTimingEqualizer` as singleton; register `ForceReasonTestSeam` as singleton.
3. Confirm endpoint: at top, start `Stopwatch sw = Stopwatch.StartNew();`. Check `ShouldReject400DebugEscapeHatch` → 400 if true. Check `await TryHandleForcedReasonAsync(ctx, sw, ct)` → return if true. Else real provider call. Before ANY terminal-path `Response.WriteAsync`, `await equalizer.EqualizeAsync(sw, ct)`. Use `try/finally` or scoped wrapper to guarantee equalize fires.
4. Analytics emitter: replace any `reason.ToString()` with `TerminalReasonCoarsening.ToWire(reason)` on the public boundary; `TerminalReasonCoarsening.ToInternalAuditString(reason)` on the SIEM/risk boundary (with explicit RBAC check above the call).
5. 429 cap handler: `Response.Headers["Retry-After"] = TerminalRetryAfter.Seconds.ToString();` uniformly for both sub-cap and IP-cap terminal paths.

## CI gates (run on every PR touching `src/TravelAssistant.Api/Checkout/**`)

```bash
# GATE-CO-06a wire-equality grep — no raw enum string in build output / public surface
grep -rE '"fraud_block"|"hard_decline"|"insufficient_funds_terminal"|"provider_rejected_permanent"' \
  dist/ src/TravelAssistant.Api/Checkout/**/Public/ 2>/dev/null
# Must return empty.

# GATE-CO-06c telemetry coarsening grep — no internal audit string in client-bound code
grep -rn 'ToInternalAuditString' src/TravelAssistant.Api/Checkout/ \
  | grep -vE 'Audit|Siem|Risk|Internal'
# Must return empty.

# GATE-CO-06b equalizer floor not lowered without sign-off
grep -rn 'FloorMs' src/TravelAssistant.Api/Checkout/Security/ \
  | grep -vE 'FloorMs = 800;'
# Must return empty (any other FloorMs value blocks merge — requires SEC sign-off label).
```

— application-development-squad
