# DR-CO-007 — 3DS Telemetry & Contract Amendment

Pairs with `wi-checkout-1-contracts/` (c80c3e4) + `wi-checkout-1-debug-seams/` (378604b).
Closes exp-design Q1/Q2/Q3 from `checkout-state-copy-action-required-amendment.md` (b47c68f).

## Q1 — `redirectUrl` shape: ABSOLUTE (provider domain)

`ProviderConfirmResult.actionRequired.redirectUrl` is **always absolute** with `https://` scheme. Never same-origin. Frontend may assert and reject anything else — no same-origin guard branch needed.

Backend implementation note (for `wi-checkout-1-backend/`): the URL comes verbatim from `Stripe.PaymentIntent.NextAction.RedirectToUrl.Url` and `Adyen.Action.Url`. Both providers always return absolute URLs in production. If a future provider returns relative, the seam (`IPaymentProvider.ConfirmAsync`) rejects with `PROVIDER_UNAVAILABLE` rather than passing through.

## Q2 — `returnUrl` echo on `GET /status`: NO

Status payload does NOT echo `returnUrl`. Frontend trusts the URL it already navigated to. Saves a field on every 5s poll × up to 12 polls per session.

Telemetry note: if a 3DS provider rewrite happens (rare — Stripe/Adyen don't), it surfaces via `checkout_3ds_redirected.redirectDelayMs` anomaly on the frontend already. Backend has no signal to add.

## Q3 — `three_ds_abandoned` vs `three_ds_failed`: BOTH, distinct in enum, coarsened on DOM

**Enum (server-side, full fidelity):**

```csharp
public enum PaymentDeclineReason
{
  // ... existing values from c80c3e4 ...
  ThreeDsAbandoned,        // user back-button / closed browser
  ThreeDsFailed,           // provider returned challenge-failed
  ThreeDsTimedOutAtProvider // provider-side timeout (distinct from client abandon)
}
```

`Reasons.All` projection grows by 3 entries — `three_ds_abandoned`, `three_ds_failed`, `three_ds_timed_out_at_provider`. `Reasons.ForEnum(PaymentDeclineReason)` switch gets 3 new arms, throws on unmapped per existing pattern.

**DOM (coarsened, exp-design owns):**

All 3 enum values project to a single `data-reason="three_ds_failed_or_abandoned"` per exp-design's `b47c68f` preferred default. Mirrors the `failed_terminal` coarsening pattern for cognitive parity.

**Why distinct wire values are safe here (and NOT a SEC-CHK-008 violation):**

SEC-CHK-008 wire-byte-equality applies to the **4 terminal-decline reasons** (`hard_decline | fraud_block | insufficient_funds_terminal | provider_rejected_permanent`) because those leak fraud-oracle signal. 3DS lifecycle outcomes leak **user/provider lifecycle signal** (did the user close the tab? did Visa's 3DS server time out?) — not payment-method risk. An attacker probing 3DS abandoned-vs-failed gains nothing about whether a card is hot. Full wire fidelity preserved server-side for funnel analytics.

Confirmed with security-hardening contract scope: 3DS outcomes are **explicitly out of the terminal-coarsening surface**. `TerminalReasonCoarsening.ToWire` does NOT touch these values.

## Telemetry events frozen by exp-design (frontend-side, FYI)

- `checkout_3ds_redirected` { checkoutSessionId, attemptNumber, redirectDelayMs }
- `checkout_3ds_resumed` { checkoutSessionId, attemptNumber, resumeOutcome (coarsened on terminal per SEC-CHK-008), resumeLatencyMs }
- (no `checkout_3ds_abandoned` — flows through `checkout_failed_retryable`)

Backend correlation field: `checkoutSessionId` is the same value `wi-checkout-1-backend/` will emit on `checkout.confirm.completed` server-side metric. One-grep correlation across client + server telemetry surfaces.

## Apply scope

This amendment ships **2 constants + 1 enum value addendum**:

1. `ThreeDsContract.RedirectUrlAlwaysAbsolute = true` + `RedirectUrlSchemeRequired = "https://"`
2. `ThreeDsContract.ReturnUrlEchoedOnStatusPoll = false`
3. `ThreeDsContract.CoarsenedDomDataReason = "three_ds_failed_or_abandoned"`
4. `PaymentDeclineReason.ThreeDsAbandoned | ThreeDsFailed | ThreeDsTimedOutAtProvider` (3 new enum values — additive to c80c3e4)

`Reasons.All` + `Reasons.ForEnum` will be regenerated against the expanded enum when `wi-checkout-1-backend/` ships. The contract layer (c80c3e4 `Codes.Reason*` constants) gets 3 string constants added at backend-bundle time:

- `Codes.ReasonThreeDsAbandoned = "three_ds_abandoned"`
- `Codes.ReasonThreeDsFailed = "three_ds_failed"`
- `Codes.ReasonThreeDsTimedOutAtProvider = "three_ds_timed_out_at_provider"`

The existing `WebhookEnvelopeEnumerationGuard.v2` discipline (every public `^Reason` const on `Codes` MUST be in `Reasons.All`) catches any forgetting.

## What this does NOT change

- Existing `CheckoutErrorEnvelope` shape — unchanged
- `CheckoutState` 9-state machine — unchanged (`ActionRequired` covers all 3DS paths)
- `CheckoutDebugSeamContract` routes — unchanged
- `TerminalResponseTimingEqualizer` / `TerminalReasonCoarsening` (commit 317e476) — 3DS values **not** coarsened by these, by contract scope above
- Idempotency-Key contract (DR-CO-005 amended at bbc2faa) — unchanged

## QA bundle impact

- **Bundle 1 (enumeration guards)** — will catch the 3 new enum values automatically when backend ships. No bundle update owed.
- **Bundle 7 (mapper drift)** — `IProviderDeclineReasonMapper.MappingTable` for Stripe + Adyen must cover the 3 new enum values. Stripe maps `three_d_secure_redirected` → `ThreeDsFailed` etc. (exact mapping owned by `wi-checkout-1-mappers/` when it ships).
- **Bundle 8 (3DS E2E)** — now has full enum coverage to assert against.

App-dev checkout queue: empty. Dispatch order unchanged (refunds v1 → SPM v1 → cancel v1 → checkout v1).
