# wi-checkout-1-spec002-answers — DR-CO-007/008/009

Answers QA's 3 Q-CO-DEV questions raised after exp-design `e0c1bed` froze Q-CO-2 + Q-CO-9.
Same shape as `wi-checkout-1-contracts` (commit `c80c3e4`) and `wi-checkout-1-spec001-answers`
(commit `bbc2faa`): code-first, zero runtime deps, safe to ship pre-backend bundle.

| DR | Question | Resolution | File |
|----|----------|------------|------|
| DR-CO-007 | Q-CO-DEV-1 — Address validation provider | Abstract `IAddressValidationProvider` seam + 8-code/8-field allowlist envelope, mirrors `IProviderReasonMapper` (DR-CANCEL-004) | `AddressValidationEnvelope.cs` |
| DR-CO-008 | Q-CO-DEV-2 — `checkoutSessionId` TTL | **30 min fixed-window absolute**, NOT sliding-on-activity. `X-Checkout-Session-Expires-At` header. 410 `CHECKOUT_SESSION_EXPIRED`. Confirmation reachable post-TTL by order id. | `CheckoutSessionContract.cs` |
| DR-CO-009 | Q-CO-DEV-3 — Place Order idempotency | **Server-issued single-use token** (option b), returned in `reviewSnapshot.placeOrderToken`. Refreshed on 409 TAX_RECALCULATED / CART_CHANGED. Mirrors `cancelToken` (DR-CANCEL-002). | `CheckoutSessionContract.cs` |

## DR-CO-007 — Address Validation Envelope

Concrete answer to Q-CO-DEV-1 with the symbolic flow QA asked for:

- `AddressValidationCode` enum — 8 values (frozen)
- `AddressValidationField` enum — 8 values (frozen)
- `AddressValidationEnvelope.Codes.All` — `IReadOnlyList<string>` (frozen ImmutableArray)
- `AddressValidationEnvelope.Codes.ForEnum(enum)` — total switch, **throws `ArgumentOutOfRangeException` on unmapped enum** (loud-fail-on-drift, same as `CancelErrorEnvelope.Reasons.ForEnum`)
- `^Code*` const fields on `Codes` static class — reflection-friendly drift sentinel
- `AddressValidationEnvelope.Fields.All` + `Fields.ForEnum(enum)` — same shape for 8-field allowlist
- `IAddressValidationProvider` interface with `MappingTable` view — provider adapters (Loqate / SmartyStreets / Google) own normalization, NO cross-normalization, unmapped → null

**QA's 8-gate enumeration guard lifts verbatim** from `WebhookEnvelopeEnumerationGuard.v2.cs` — namespace swap only:

```
Gate 1: AddressValidationEnvelope.Codes.All.Count == 8
Gate 2: every public ^Code* const on Codes is in Codes.All
Gate 3: Enum.GetValues<AddressValidationCode>().Length == 8
Gate 4: ForEnum total sweep — every enum value → non-null/non-throw
Gate 5: SCREAMING_SNAKE_CASE projection consistency (Codes.PostalCodeInvalid == "POSTAL_CODE_INVALID")
Gate 6: AddressValidationEnvelope.Fields.All.Count == 8
Gate 7: every public ^Field* const (excluding helper consts) is in Fields.All
Gate 8: Fields.ForEnum total sweep
```

**Unmapped-provider-code discipline (mirrors DR-CANCEL-004 R2 fail-open):**

1. Provider returns code not in adapter's `MappingTable` → `MapProviderCode` returns `null`
2. Caller emits `checkout.address_validation.unmapped_code` telemetry (server-side only — GATE-CO-01-ADDR redaction; provider strings never reach `dist/`)
3. Caller projects to a generic envelope code per field semantics (e.g., postal-code field → `POSTAL_CODE_INVALID`)
4. 422 response shape unchanged — client never sees the unmapped path

## DR-CO-008 — Session TTL

**Fixed-window absolute expiry, NOT sliding.** Rationale: predictable bounds for security (session fixation window cap) + simpler test surface (QA bundle 8 = 4 boundary tests not 8) + matches how reservation 90s TTL composes (reservation can extend session up to 1× retry budget without re-creating cart).

- TTL: **30 minutes** from cart creation
- Server emits `X-Checkout-Session-Expires-At` header on every cart-scoped response → client computes countdown without clock-skew round-trip
- Expiry → `410 Gone` with `CHECKOUT_SESSION_EXPIRED` code
- Confirmation page reachable post-TTL **iff** the underlying order is in `Confirmed` state — order id gates `/confirmation`, not cart id (cart id is dead after expiry; confirmed orders are immortal)
- Idle-tab > TTL → next /api/checkout/* call returns 410 → client redirects to fresh `/cart` with toast

## DR-CO-009 — Place Order Idempotency Token

**Server-issued single-use token** (option (b) per QA's recommendation). Same retry-safety guarantees as `cancelToken` in DR-CANCEL-002.

- `GET /api/checkout/{cartId}/review` returns `reviewSnapshot.placeOrderToken` (16-byte URL-safe base64, opaque)
- `POST /api/checkout/{cartId}/confirm` body MUST include `placeOrderToken`. Missing → `400 PLACE_ORDER_TOKEN_REQUIRED`
- Token is single-use, scoped per-cart. Reuse → `409 PLACE_ORDER_TOKEN_CONSUMED`
- `409 TAX_RECALCULATED` or `409 CART_CHANGED` → server issues a **new** token in the re-review payload. Client MUST NOT reuse the old token (would have been invalidated server-side anyway)
- Coexists with `Idempotency-Key` header (DR-CO-005, per-cart, 15min): the header survives at the HTTP transport layer, the token survives at the business layer. Both required for retry safety.

## Ownership Boundary (refunds v1b model preserved)

- **app-dev OWNS:** enums, `Codes.All`, `Codes.ForEnum`, `Fields.All`, `Fields.ForEnum`, `IAddressValidationProvider` interface + per-provider impls (Loqate/Smarty/Google land in `wi-checkout-1-mappers/`), session header emission, token issuance + cache + invalidation
- **QA CONSUMES via** `using static TravelAssistant.Checkout.Contracts.AddressValidationEnvelope` + `Codes.ForEnum(enum)`. Zero hand-rolled `ToSnakeCase` / `SCREAMING_CASE` projection anywhere in `tests/Checkout/` by construction
- **review-deployment ASSERTS** on deployed surface: `dist/` greps for provider strings (`charge_*`, `street_premise_*`, `verified-no-changes`), `X-Checkout-Session-Expires-At` header presence on cart endpoints, `placeOrderToken` field on `/review` GET payload

## Discipline Gates (4 new — additive to existing GATE-CO-01..05)

```bash
# GATE-CO-01-ADDR: provider-native validation codes never serialized to client
grep -rE 'street_premise_|verified-no-changes|smarty_|loqate_' dist/                        # empty

# GATE-CO-06: address-validation hand-rolled projection never appears in tests
grep -rE 'ToSnakeCase|SCREAMING|Regex.*Code.*Replace' tests/Checkout/details/               # empty

# GATE-CO-07: session expiry header emitted on every cart-scoped response
grep -rE 'X-Checkout-Session-Expires-At' src/Checkout/Endpoints/                            # >= 1 per cart-scoped endpoint

# GATE-CO-08: placeOrderToken never client-generated, never logged
grep -rE 'placeOrderToken.*Guid\.NewGuid|crypto\.randomUUID.*placeOrderToken' src/Checkout/ # empty
grep -rE 'placeOrderToken' dist/logs/ src/**/Log*.cs                                        # empty
```

## Apply Order — Checkout v1 bundle stack (unchanged + extended)

1. ✅ `wi-checkout-1-contracts/` (c80c3e4) — DR-CO-001..006 seams + envelopes + state + idempotency + tax
2. ✅ `wi-checkout-1-spec001-answers/` (bbc2faa) — 3DS / idempotency amendment / review snapshot / shipping rate timing / tax triggers
3. ✅ **`wi-checkout-1-spec002-answers/` (THIS)** — DR-CO-007/008/009 address validation envelope + session TTL + place order token. Zero runtime deps.
4. ⏳ `wi-checkout-1-mappers/` — Stripe + Adyen `IProviderDeclineReasonMapper` + Loqate/Smarty/Google `IAddressValidationProvider` impls. Pre-backend safe, runnable against QA drift-guard tests.
5. ⏳ `wi-checkout-1-backend/` — endpoints + repos + DI + real provider clients + webhook handlers + Redis idempotency cache + session TTL + token issuance. Ships when refunds v1 + SPM v1 + cancel v1 hit 100%.

Dispatch order unchanged: refunds v1 → SPM v1 → cancel v1 → checkout v1.

## Open Question to Ideation

DR-CO-007 R2 fail-open path (unmapped provider code → generic envelope code by field semantics) needs an authoritative field→fallback-code mapping table (e.g., postal field → `POSTAL_CODE_INVALID`, street field → `STREET_NOT_FOUND`). Sensible defaults shipped here; if ideation wants a different fallback policy (e.g., always `COUNTRY_UNSUPPORTED` to force user to restart), file `CHECKOUT-SPEC-002` amendment. None of QA's bundles 6/8 depend on this resolving.

App-dev checkout queue: empty until ideation answers above OR refunds/SPM/cancel v1 land at 100%.
