# wi-checkout-1-bundle11-amendment — Fail-Open Mapper + Bundle 11 Debug Seams

App-dev response to QA bundle 11 (session d4a222e5, `files/checkout-spec-001-ratification-bundle/`). 2 additive files. Zero modification to existing shipped contracts.

## What's pinned

### 1. `CheckoutFailOpenMapperContract.cs` — fail-open default arm frozen

| Constant | Value | Why frozen |
|---|---|---|
| `UnmappedState` | `"failed_retryable"` | Unknown ≠ terminal. UX retry path must remain open. |
| `UnmappedReason` | `"provider_unknown"` | Stable wire string; client copy already keyed to it (refunds + cancel use identical naming). |
| `UnmappedRetryable` | `true` | Indefinite-hold prevented at inventory layer (90s TTL, no refresh on retry). |

`MapProviderReasonReference` provides the reference signature backend bundle's `CheckoutErrorEnvelope.Reasons.MapProviderReason` must match. **G6 grep gate enforces:** default arm not matching `(UnmappedState, UnmappedReason, UnmappedRetryable)` → build fails.

**Third use of the unmapped → retryable pattern.** Same shape as:
- `refund.failure_reason_unmapped` → `("refund_unknown", true)`
- `cancel.confirming.reason_unmapped` → `("provider_unknown", true)`
- `checkout.confirming.reason_unmapped` → `("provider_unknown", true)` ← this bundle

### 2. `CheckoutUnmappedReasonTelemetry` — SEC sentinel metric pinned

| Field | Value |
|---|---|
| `MetricName` | `"checkout.confirming.reason_unmapped"` |
| `RawProviderReasonField` | `"rawProviderReason"` (server-side, enum fidelity) |
| `CheckoutSessionIdField` | `"checkoutSessionId"` |
| `AttemptNumberField` | `"attemptNumber"` |
| `Severity` | `"SEC"` |

**SEC-CHK-008 R4 carve-out applies:** raw provider reason kept at full fidelity in telemetry path (Audit/Risk/Internal/SIEM keep enum precision). Coarsening happens at the envelope boundary (`MapProviderReason → UnmappedReason`), not the telemetry boundary. Pending final security-hardening confirmation on QA's pending Q (their bundle 11 §"One Q to security-hardening").

### 3. `CheckoutDebugSeamContractBundle11.cs` — 3 new debug seams

Amends commit `378604b` (`CheckoutDebugSeamContract`) — additive, no edits to existing fields.

| Seam | Purpose | Used by |
|---|---|---|
| `GET /_debug/inventory-reservation/by-session/{sessionId}` | Returns `{reservationId, ttlExpiresAt, status}` for the specific session | InventoryReservationLifecycleTest |
| `?_force_provider_reason={string}` on `POST /confirm` | Raw provider reason BEFORE `MapProviderReason` runs | UnmappedProviderReasonFailOpenTest |
| `?_force_provider_state={pending\|confirmed\|failed}` on `POST /confirm` | Pin provider response state | reservation lifecycle + race tests |

**Sibling, not replacement.** The existing `/_debug/inventory-reservation/{sku}` route stays — it returns aggregate reserved count for the SKU, used by reservation-race tests that need cross-session inventory visibility. The new `by-session` route returns the per-reservation row for lifecycle tests. Two different queries, two different routes, both useful.

**Distinct from `ForceReasonTestSeam`** (commit `317e476`). That seam's `?_force_reason={enum}` pins the ALREADY-COARSENED terminal-decline wire reason for SEC-CHK-008 R6 wire-equality tests. `?_force_provider_reason` here pins the RAW PROVIDER STRING before mapping runs, so the unmapped-path default arm is observable. Different layer, different test purpose, naming kept distinct on purpose.

## CHECKOUT_DEBUG env discipline (unchanged from 378604b)

All seams in this bundle inherit the existing env-gate pattern:
- `Environment.GetEnvironmentVariable("CHECKOUT_DEBUG")=="1"` read at request time (no `IOptions`, no static cache)
- Unset → 404 short-circuit (NOT 403 — 403 would confirm route exists = oracle)
- `?debug=1` / `X-Debug-Mode` header → 400 (unchanged from `ShouldReject400DebugEscapeHatch`)
- GATE-CO-06e env scan in `checkout-debug-env-scan.yml` (azure-infra bundle 9a) catches `CHECKOUT_DEBUG=1` past dev

## Ownership boundary (refunds v1b model — preserved)

| Surface | App-dev OWNS | QA CONSUMES | Review-deployment ASSERTS |
|---|---|---|---|
| Fail-open default arm | `UnmappedState`/`Reason`/`Retryable` constants + `MapProviderReasonReference` signature | `using static CheckoutFailOpenMapperContract` in tests | G6 grep: default arm matches |
| Unmapped telemetry | `CheckoutUnmappedReasonTelemetry.MetricName` + field names | Bind metric assertions to constants | Deployed metric name exists in App Insights schema |
| New debug seams | `Routes.InventoryReservationBySession` + `QuerySeams.ForceProviderReason`/`ForceProviderState` constants | `using static CheckoutDebugSeamContractBundle11` | GATE-CO-06e env scan + 404-not-403 canary |

Zero hand-rolled route strings in `tests/Checkout/`. Zero hand-rolled metric names. Zero hand-rolled reason literals. Drift gates from DR-CANCEL-005 (3e8df6b) pattern extend transitively.

## Apply order (unchanged from prior dispatch)

1. ✅ `wi-checkout-1-contracts/` (c80c3e4) — error envelope, state machine, idempotency, tax recalc
2. ✅ `wi-checkout-1-spec001-answers/` (bbc2faa) — ideation CHECKOUT-SPEC-001 reconciliation
3. ✅ `wi-checkout-1-security-hardening/` (317e476) — SEC-CHK-008 timing equalizer + coarsening + `?_force_reason` seam
4. ✅ `wi-checkout-1-debug-seams/` (378604b) — 8 `_debug/*` routes, env-gate, 404-not-403
5. ✅ `wi-checkout-1-3ds-telemetry-amendment/` (fd02ea5) — exp-design 3DS Qs Q1/Q2/Q3
6. ✅ **`wi-checkout-1-bundle11-amendment/`** (THIS) — fail-open mapper + 3 new debug seams + SEC sentinel
7. ⏳ `wi-checkout-1-mappers/` — Stripe + Adyen `IProviderDeclineReasonMapper` impls (ships after cancel v1 mappers reference accepted)
8. ⏳ `wi-checkout-1-backend/` — endpoints + repos + DI + middleware (ships after refunds v1 → SPM v1 → cancel v1 all hit 100%)

## QA bundle 11 unblocked

- **`UnmappedProviderReasonFailOpenTest.cs`** — pure reflection test against `CheckoutFailOpenMapperContract` constants + signature. Ships pre-backend. ✅
- **`UnauthenticatedConfirmReturns401Test.cs`** — needs backend bundle. ⏳ Queued.
- **`InventoryReservationLifecycleTest.cs`** — needs backend + new debug seams wired. Contract frozen today. ⏳ Queued.

## Dispatch order unchanged

refunds v1 → 100% → SPM v1 → 100% → cancel v1 → 100% → checkout v1 backend bundle. App-dev checkout queue: **empty**.
