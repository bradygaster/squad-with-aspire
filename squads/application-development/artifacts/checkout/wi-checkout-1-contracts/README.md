# WI-CHECKOUT-1 Contracts — DR-CO-001..006 (Bundle 1)

**Branch:** `tamir/squad-fixes` · **Owner:** application-development squad · **Date:** 2026-06-24

Answers QA's 5 owned questions from `files/checkout-flow-test-plan/README.md` (session 3ab6ab79) with concrete code, mirroring the cancel-v1 contract shipping pattern (DR-CANCEL-001..005, commits `bcec51f` → `3e8df6b`).

---

## Files (7)

| File | Decision Record | Purpose |
|---|---|---|
| `CheckoutErrorEnvelope.cs` | DR-CO-001 | Top-level `Codes.*` (10 frozen) + nested `Codes.Reason.*` (4 frozen) + `Reasons.All` + `Reasons.ForEnum()` projection. Single SOT. |
| `IPaymentProvider.cs` | DR-CO-002 | Abstract provider seam — hides Stripe `payment_intent` vs Adyen `/payments` flows. Mirrors `IPaymentProviderCancelClient`. |
| `IProviderDeclineReasonMapper.cs` | DR-CO-002 | Per-adapter raw-code → enum mapper. Stripe + Adyen impls ship in `wi-checkout-1-mappers/`. |
| `CheckoutState.cs` | DR-CO-003 | 9-state machine (QA's 7-state + `ActionRequired` + `FailedRetryable` split). Polling helpers. |
| `CheckoutWebhookEnvelope.cs` | DR-CO-004 | `Events.*` (3 frozen) + `FailureCodes.*` (4 frozen, `DECLINED` excluded as unmapped sentinel). |
| `IdempotencyContract.cs` | DR-CO-005 | `Idempotency-Key` header + per-session scope + 24h TTL + REQUEST_IN_FLIGHT semantics. |
| `TaxRecalcContract.cs` | DR-CO-006 | `ConfirmRequest.ExpectedTotalMinorUnits` echo. No silent recalc — drift → 409 `TAX_RECALCULATED` → forced re-review. |

---

## Answers to QA's 5 Owned Questions

### Q-CO-3 — Payment provider surface
**Decision:** Abstract `IPaymentProvider` with per-adapter impls (Stripe + Adyen). `IProviderDeclineReasonMapper` mirrors `IProviderReasonMapper` exactly. Stripe adapter owns snake_case keys, Adyen adapter owns UPPERCASE_SNAKE_CASE keys, **no cross-normalization** (silent normalization masks provider API drift — same rule as cancel mappers commit `06873f7`).

### Q-CO-4 — Error envelope
**Decision:** New `CheckoutErrorEnvelope` (not reuse of `CancelErrorEnvelope`). Domain shape differs (top-level codes are checkout-specific). **But it ships the same `Reasons.All` + `Reasons.ForEnum(TEnum)` projection helper from `3e8df6b`** so `tests/Checkout/` has zero hand-rolled `ToSnakeCase`. QA bundle 1's discipline gate §9 will pass on day one.

### Q-CO-5 — Polling vs synchronous
**Decision:** **Polling.** `POST /api/checkout/{id}/confirm` returns `202 Accepted` + `Location: /api/checkout/{id}/status`. `GET /status` is the canonical state mirror — reuse `usePollingResource` 5s/60s/12-cap defaults from cancel v1. Reuse `WebhookEnvelopeEnumerationGuard.v2` Gates 1–8 pattern from `DR-CANCEL-005` (gate 6 const-parity, gate 7 cardinality, gate 8 enum sweep through `Reasons.ForEnum`) against `CheckoutWebhookEnvelope` directly.

### Q-CO-6 — Idempotency key
**Decision:** Header `Idempotency-Key`, **required** on confirm endpoint, **scoped per-checkout-session-id** (not per-user, not per-cart). 24h cache TTL. Missing key = 400. Same key + same session + different body = 409 `IDEMPOTENCY_KEY_CONFLICT`. In-flight duplicate = 409 `REQUEST_IN_FLIGHT` with `retryAfterSeconds` (same envelope shape as `CancelErrorEnvelope.RequestInFlight`).

### Q-CO-10 — Tax recalculation timing
**Decision:** **No silent recalc on confirm.** Recalc on shipping entry + on review render + validated on confirm via `expectedTotalMinorUnits` echo field. Drift > 0 minor units → 409 `TAX_RECALCULATED` → forced re-review screen with explicit re-acknowledge. Adopts QA's strong recommendation verbatim.

---

## Bonus reply to QA §5 state machine

QA proposed: `cart → shipping → payment → review → confirming → confirmed | failed_retryable | failed_terminal`

**Accepted with one amendment:** adds `action_required` state between `confirming` and terminal for 3DS/SCA challenges. Client follows `actionRedirectUrl`, then re-polls `/status`. Without this state, terminal `failed_retryable` becomes the only landing pad for a 3DS challenge that's actually just "user needs to authenticate" — wrong semantics.

Final 9-state machine in `CheckoutState.cs` with `IsTerminal()` / `IsPolling()` / `AllowsRetry()` helpers for test consumption.

---

## Ownership boundary (refunds v1b / cancel v1 model preserved)

- **app-dev OWNS:** `Codes.*`, `Codes.Reason.*`, `Reasons.All`, `Reasons.ForEnum()`, `Events.*`, `FailureCodes.*`, `MapDeclineReason()` impls, `IdempotencyContract`, `TaxRecalcContract`.
- **QA CONSUMES** via `using static` + `Reasons.ForEnum()` projection. **Zero hand-rolled `ToSnakeCase` anywhere in `tests/Checkout/`.**
- **review-deployment ASSERTS** deployed `error.code` ∈ `Codes.*`, deployed `error.reason` ∈ `Reasons.All`, deployed webhook event names ∈ `Events.All`, deployed webhook failure codes ∈ `FailureCodes.All` exactly.

---

## Discipline gates (post-deploy)

```bash
# No hand-rolled projection in checkout test tree:
grep -rE 'ToSnakeCase|Regex.*[Rr]eason|\.ToLowerInvariant\(\).*Reason' tests/Checkout/   # empty
# No raw string literals for contract values:
grep -rE '"insufficient_funds"|"card_expired"|"fraud_suspected"|"do_not_honor"' tests/Checkout/  # empty
# No provider-id leak guard (mirrors GATE-CANCEL-07):
grep -rE 'pi_[A-Za-z0-9]{14,}|payment_intent|pspReference' dist/                          # empty
# Idempotency key REQUIRED on confirm:
grep -rE 'POST.*checkout.*confirm' dist/ | grep -v 'Idempotency-Key'                      # empty
```

---

## Apply order — bundle stack (planned, mirrors cancel v1 stacking)

1. **`wi-checkout-1-contracts/` (THIS bundle)** — seams + envelopes + state machine + idempotency + tax contracts. Zero runtime deps. Safe to ship pre-backend.
2. `wi-checkout-1-mappers/` — `StripeProviderDeclineReasonMapper` + `AdyenProviderDeclineReasonMapper` (`FrozenDictionary` impls, mirrors cancel mappers commit `06873f7`).
3. `wi-checkout-1-backend/` — endpoints, repos, DI, real Stripe/Adyen `IPaymentProvider` clients, webhook handlers, idempotency cache (Redis), session TTL. Ships when refunds v1 + SPM v1 + cancel v1 hit 100%.

**Dispatch order remains:** refunds v1 → 100% → SPM v1 → 100% → cancel v1 → 100% → checkout v1.

---

## Open questions back to ideation-research-planning

- **DR-CO-A:** 3DS/SCA challenge — does our PSP regulatory scope require it for all card transactions, or only EU/UK BIN ranges? Determines whether `ActionRequired` state is rare-path or common-path. (Frontend complexity differs.)
- **DR-CO-B:** Session TTL — 30min proposed (matches typical cart session). Confirm or amend.
- **DR-CO-C:** Cart-changed (price/inventory drift between review and confirm) — should server return 409 `CART_CHANGED` with current cart payload (so frontend can diff), or just 409 + force-redirect-to-review? Affects bundle 6 backend test shape.

Filing these as DR-CO-007..009 candidates if ideation responds.

---

## Audit trail

- Builds atop refunds v1b commit `7c5196c` (envelope pattern), cancel v1 commits `bcec51f` → `3e8df6b` (seam + envelope + projection patterns), cancel mappers `06873f7`.
- No new runtime deps. Pure types + interface declarations.
- Maintainer apply: `git apply` not required — these are new files, `git add squads/application-development/artifacts/checkout/wi-checkout-1-contracts/ && git commit`.
