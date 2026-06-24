# WI-CANCEL-1 — Provider Reason Mappers (Stripe + Adyen)

**Status:** Shipped (pre-impl, pure functions, no SPM/backend dependency)
**Bound by:** DR-CANCEL-004 R2 (mapping table is closed and binding)
**Consumes:** `wi-cancel-1-contracts/IProviderReasonMapper.cs` (DR-CANCEL-004)
**Consumed by:** QA `ProviderReasonMappingTests.cs` (drift guard, can run today),
                 production wiring in `wi-cancel-1-backend/` (when SPM v1 hits 100%)

## What ships here

Two pure-function `IProviderReasonMapper` implementations — one per supported
payment provider. No I/O, no DI graph, no config bindings. Safe to instantiate
as a singleton, call as `new XxxMapper().MapProviderReason(code)`, or wire
through DI in the backend bundle later.

| File | Adapter | Wire convention |
|------|---------|-----------------|
| `StripeProviderReasonMapper.cs` | `StripePaymentProviderCancelClient` | lowercase snake_case |
| `AdyenProviderReasonMapper.cs`  | `AdyenPaymentProviderCancelClient`  | UPPERCASE_SNAKE_CASE |

## Binding mapping table (DR-CANCEL-004 R2)

| Stripe code                       | Adyen code           | → mapped reason            |
|-----------------------------------|----------------------|----------------------------|
| `charge_already_refunded`         | `ALREADY_REFUNDED`   | `AlreadyRefunded`          |
| `charge_already_canceled`         | `ALREADY_CANCELLED`  | `AlreadyCanceled`          |
| `charge_disputed_in_progress`     | `ORDER_LOCKED`       | `FulfillmentInProgress`    |
| *(any other code, incl. null/"")* | *(any other code)*   | `null` ← unmapped          |

Tables are exhaustive and closed. Any new provider code MUST arrive as a
DR amendment + ratification — never silently extended in adapter code.

## Why ship now (pre-SPM-v1)

These mappers have **zero dependencies** outside the frozen contract surface:

- No Stripe/Adyen SDK reference
- No `IPaymentProviderCancelClient` reference
- No HTTP client, no DB, no config
- No SPM coupling

That makes them safe to author + unit-test ahead of the backend bundle. QA's
`ProviderReasonMappingTests.cs` drift guard (which reflects over the table
against the 4-value enum + asserts unmapped-returns-null + asserts the
read-only `MappingTable` view exposes the exact rows above) can run against
real code today instead of waiting for SPM v1 → wi-cancel-1-backend.

## Cross-cutting invariants preserved

1. **Case-sensitive Ordinal match per adapter.** No cross-normalization.
   Stripe is lowercase by contract; Adyen is uppercase by contract. A
   mismatched-case incoming code is an unmapped code BY DEFINITION and routes
   through the unmapped path. Silent normalization would mask provider API
   drift from observability.

2. **Null/empty in → null out, no exception.** Callers route null through
   the unmapped-responsibility chain (see `IProviderReasonMapper.cs` header):
   - Treat as `ProviderCancelOutcome.Unavailable` (NOT `Declined`)
   - Increment `cancel.failure_reason_unmapped` counter
   - Log raw `providerReason` to cancel-audit container with discriminator
     `unmapped_declined_reason`
   - **Never** serialize `providerReason` to client (GATE-CANCEL-07 grep guard)

3. **`window_expired` enum value intentionally absent from both tables.**
   It is server-side at POST time only — never originates from a provider
   webhook. Listed in `CancelErrorEnvelope.Codes.ReasonWindowExpired` for
   completeness of the 409 ORDER_NOT_CANCELLABLE{reason} surface.

4. **`FrozenDictionary` lookup.** Zero-allocation, immutable after class
   initialization. Static `MappingTable` view returned as `IReadOnlyDictionary`
   so QA tests can enumerate without acquiring write capability.

5. **`StringComparer.Ordinal`** on both the dictionary backing store and the
   constructor. Cultural-invariant. Required because provider codes are
   ASCII-only protocol identifiers, not human-readable strings.

## What still ships in `wi-cancel-1-backend/` (queued on SPM v1)

These two mapper classes are the ONLY production-shipping pieces of the
provider-reason surface. The backend bundle adds:

- `StripePaymentProviderCancelClient` (real Stripe.net wire-up — `payment_intent.cancel`)
- `AdyenPaymentProviderCancelClient` (real Adyen SDK wire-up — `/cancels`)
- DI registration: `services.AddSingleton<IProviderReasonMapper, StripeProviderReasonMapper>()`
  (provider selected by Aspire env/connection-string convention; multi-provider
  routing strategy is a wi-cancel-1-backend concern)
- Webhook `cancel.declined` handler that calls `MapProviderReason()` and
  routes mapped/unmapped per DR-CANCEL-002 + DR-CANCEL-003 + DR-CANCEL-004

## Apply order (within wi-cancel-1 vertical)

1. `wi-cancel-1-contracts/` (DR-001 + 002 + 003 + 004) — **SHIPPED**
2. `wi-cancel-1-mappers/` (this bundle) — **SHIPPED** (additive, no migration)
3. `wi-cancel-1-backend/` (endpoint, repo, DI, real provider clients,
   webhook handler, seams #2–#5) — **QUEUED on SPM v1 100%**
4. QA bundle `cancel-backend-suite/` — lands when (3) deploys
5. WI-CANCEL-6 infra ∥ WI-CANCEL-2 UX → WI-CANCEL-4 frontend → WI-CANCEL-7 rollout

## Ownership boundary (refunds v1b model preserved)

| Surface                              | Owns                | Consumes                                          |
|--------------------------------------|---------------------|---------------------------------------------------|
| `IProviderReasonMapper`              | app-dev (contract)  | QA (`using`), backend wiring (`using`)            |
| `CancelIneligibilityReason` enum     | app-dev (contract)  | QA reflection, mapper impls                       |
| `StripeProviderReasonMapper.MappingTable` | app-dev (impl) | QA exhaustive-coverage test                       |
| `AdyenProviderReasonMapper.MappingTable`  | app-dev (impl) | QA exhaustive-coverage test                       |
| Unmapped fallback behavior (caller)  | wi-cancel-1-backend | webhook handler, audit logger, metrics            |

QA's `ProviderReasonMappingTests.cs` MUST assert against `MappingTable` via
`using static`-style import — never duplicate the 3 rows in test code. Drift
between the table and DR-CANCEL-004 R2 is caught at the contract-amendment
review, not in the test suite.

## Files

- `StripeProviderReasonMapper.cs` — 3 mapped codes + unmapped null
- `AdyenProviderReasonMapper.cs`  — 3 mapped codes + unmapped null
- `README.md` (this file)
