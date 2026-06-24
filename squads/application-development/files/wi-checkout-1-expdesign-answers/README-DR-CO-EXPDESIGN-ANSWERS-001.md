# DR-CO-EXPDESIGN-ANSWERS-001 — answers to exp-design's 3 open Qs on checkout state copy

**Bundle:** wi-checkout-1-expdesign-answers/
**Branch:** tamir/squad-fixes
**Referenced by:** exp-design `checkout-state-copy-and-analytics-spec.md` §7 Q1/Q2/Q3
**Status:** Binding. Unblocks final copy lock for `confirming` + `failed_retryable` + `three_ds_required` rows.

---

## Q1 — `failed_retryable` no-charge invariant per reason

**Question:** Every `failed_retryable` copy variant says "Your card was not charged." Is that true across all 4 retryable reasons — `gateway_timeout`, `provider_unavailable`, `soft_decline`, `three_ds_required` — or can `soft_decline` ship with a held auth that auto-releases in N days?

### Truth table (binding)

| Reason | Auth state when user sees `failed_retryable` | "Your card was not charged" is... |
|---|---|---|
| `gateway_timeout` | NEVER held at our end. Provider may have an in-flight auth which their idempotency layer reconciles on retry (we send the SAME idempotency key per DR-CO-005 v2 → provider collapses retry onto original txn → no double-auth). | **TRUE.** |
| `provider_unavailable` | NEVER held. Pre-flight failed (circuit breaker open, DNS error, 5xx burst). Provider call never executed. | **TRUE.** |
| `soft_decline` | NEVER held. "Soft decline" by definition = issuer did NOT authorize. Provider returns no held auth on a decline response. | **TRUE.** |
| `three_ds_required` | NEVER held. 3DS is pre-authorization. Auth request is sent ONLY after 3DS clears. | **TRUE.** |

### State-machine guarantee

A held auth can ONLY exist in `Confirmed` state. Auth capture is two-phase:
1. **3DS check** (if BIN requires) — `ActionRequired` → user authenticates → resume.
2. **Auth request** — sent ONLY after 3DS clears (or skipped for non-SCA BINs).
3. Provider response → `Confirmed` | `failed_retryable.*` | `failed_terminal.*` | `confirm_rejected.*`.

Any response prior to step 3 returning `Confirmed` has BY DEFINITION not held funds.

### Gateway-timeout edge case (only path with provider-side ambiguity)

Mitigation: same idempotency key on retry (per-cart, 15min TTL, JCS body hash — DR-CO-005 v2). Provider's idempotency layer collapses second request onto first — returns original auth result (if completed) or queues alongside (if still in flight). Net: at most one auth on user's statement, resolved within provider's reconciliation window (<30s for the call; un-captured auths follow issuer's standard hold release, typically 7 days but THIS path captures-and-releases within the same minute via provider idempotency).

**Verdict: copy stays as exp-design wrote it. "Your card was not charged" is accurate across all 4 retryable reasons. No revision needed.**

---

## Q2 — `CONFIRM_REJECTED` cart-diff contract

**Question:** When user lands on `/cart` after `out_of_stock` / `price_changed` / `shipping_unavailable`, does the server return `cart.changes[]` on cart reload so frontend can highlight what changed?

### Answer: YES. Contract below.

**Endpoint:** `GET /api/cart/{cartId}` — extended to include `changes[]` when the cart was reconciled due to a `CONFIRM_REJECTED` event in the last 60s.

```json
{
  "cartId": "crt_abc123",
  "lineItems": [...current authoritative state...],
  "totalMinorUnits": 1234567,
  "currency": "USD",
  "changes": [
    { "lineItemId": "li_xyz789", "kind": "removed", "reason": "out_of_stock" },
    { "lineItemId": "li_pqr456", "kind": "price_increased",
      "oldPriceMinorUnits": 9999, "newPriceMinorUnits": 11999, "reason": "price_changed" },
    { "lineItemId": "li_mno321", "kind": "quantity_reduced",
      "oldQuantity": 3, "newQuantity": 1, "reason": "out_of_stock" },
    { "lineItemId": "li_ghi654", "kind": "shipping_unavailable", "reason": "shipping_unavailable" }
  ]
}
```

### `kind` enum (frozen, mirrors `CheckoutErrorEnvelope.Reasons` discipline)

| Kind | When emitted | Extra fields |
|---|---|---|
| `removed` | Item no longer purchasable (out of stock no partial fulfillment OR shipping unavailable to destination OR seller delisted) | none |
| `price_increased` | New price > old | `oldPriceMinorUnits`, `newPriceMinorUnits` |
| `price_decreased` | New price < old | `oldPriceMinorUnits`, `newPriceMinorUnits` |
| `quantity_reduced` | User wanted N, only M < N available | `oldQuantity`, `newQuantity` |
| `shipping_unavailable` | In stock but can't ship to current address (item stays in cart, marked unshippable for current addr) | none |

### Lifecycle

- `changes[]` populated by cart service on `CONFIRM_REJECTED` event reception.
- Persists for **60 seconds** (same TTL as `reviewSnapshot` — divergence-bound consistency).
- Clears on any user-initiated cart mutation (add/remove/qty change) OR 60s expiry.
- Mutation-clears-changes is intentional: once user has acted on the diff, re-showing yesterday's diff misleads.

### Routing contract

- After `POST /api/checkout/{id}/confirm` returns `409 CONFIRM_REJECTED`, client navigates to `/cart` (canonical destination per CHECKOUT-SPEC-002 R3 + exp-design §4). Very next `GET /api/cart/{cartId}` MUST return `changes[]` populated.
- Direct navigation to `/cart` (back-button, bookmark) without recent `CONFIRM_REJECTED` → `changes` is `[]` or omitted.

### Reason strings

Mirrors `CheckoutErrorEnvelope.Codes.Reason*` allowlist exactly: `out_of_stock` | `price_changed` | `shipping_unavailable`. Single source of truth: `CartChangeReasons.All` (frozen `IReadOnlyList<string>`) + `CartChangeReasons.ForKind(CartChangeKind)` projection. **No string literals in QA tests** — same `using static` + `ForEnum` discipline as `CancelErrorEnvelope.Reasons.ForEnum` (DR-CANCEL-005). `CartChangeContract.cs` ships with `wi-checkout-1-backend/` bundle.

### a11y recommendation (your call)

Server returns data; surface treatment is exp-design's lane. Recommendation: inline visual highlight per row + ONE consolidated polite live-region announcement on cart mount summarizing count + kinds ("3 items changed: 1 removed, 2 price increased"). Don't move focus — user landed here intentionally.

---

## Q3 — 3DS challenge surface

**Question:** When `confirming` resolves to `three_ds_required`, what's the surface? (a) inline iframe in `/checkout/review`, (b) full-page redirect to provider, (c) modal iframe overlay?

### Answer: (b) — full-page redirect. Already shipped at CHECKOUT-SPEC-002 (commit bbc2faa). NO iframe, NO modal.

Re-stating verbatim for locking convenience:

- `GET /api/checkout/{id}/status` returns `state: "ActionRequired"` + `actionRequired: { type: "redirect", url, returnUrl: "/checkout/{id}/review?resume=1" }`.
- Client: `window.location.assign(actionRequired.url)` — **full browser navigation**.
- Provider hosts 3DS UI on its own origin (Stripe `hooks.stripe.com`; Adyen `live.adyen.com`).
- Provider redirects back to `returnUrl` with provider query (e.g., Stripe: `?payment_intent=pi_xxx&payment_intent_client_secret=...`).
- `/checkout/{id}/review?resume=1` handler reads query, calls provider's resume API server-side, transitions state machine: `ActionRequired → Confirming → {Confirmed|FailedRetryable|FailedTerminal|ConfirmRejected}`.
- Next `GET /status` poll returns terminal state. Same poll loop as non-3DS path.

### Why redirect-only v1

- **a11y:** provider 3DS UIs are already a11y-audited for SCA (PSD2). Iframe-embed shifts a11y onto us + requires CSP `frame-src` carve-outs that open iframe-bridge attack surface (SEC-CHK-006 scope).
- **Browser autofill:** OTP autofill (SMS, authenticators) often blocks iframe contexts. Full-page navigation works universally.
- **Single E2E shape:** QA bundle 4 `3DSResumeHappyPathTest` tests one shape across Stripe + Adyen.
- **Focus/return semantics:** page-level focus reset on `/review?resume=1` mount uses your existing focus-policy §1 rule. Iframe focus is its own quagmire.

### Copy recommendation for `failed_retryable.three_ds_required`

| Field | Value |
|---|---|
| Heading | "Verify with your bank to complete payment" |
| Sub-copy | "Your bank requires extra verification. You'll be redirected to verify, then returned here to finish your order." |
| Primary CTA | "Verify with my bank" → `window.location.assign(actionRequired.url)` |
| Secondary CTA | "Get help" → `/help/payments` (consistency with terminal recovery) |

`three_ds_required` is technically a `failed_retryable` reason, but its UX is "go do this thing, come back." Treat the CTA as the primary action (verify), not "retry."

### Deferred to DR-CHECKOUT-3DS-002 (post-v1)

- Adyen Components / Stripe Elements iframe (lower-friction, partial-page UX)
- Apple Pay / Google Pay native sheets (platform-native SCA)
- Inline 3DS for low-risk BINs (no-SCA frictionless flow)

v1 ships redirect-only across all BINs.

---

## Summary

| Q | Answer | Your action |
|---|---|---|
| Q1 no-charge invariant | TRUE across all 4 retryable reasons. State machine + idempotency guarantee no double-auth. | Lock current copy. No revision. |
| Q2 cart-diff contract | YES. `GET /api/cart/{cartId}` returns `changes[]` for 60s after `CONFIRM_REJECTED`. 5 `kind` values. `CartChangeContract.cs` ships with backend bundle. | Spec surface treatment (inline highlight + 1 polite announcement recommended). Lock §4. |
| Q3 3DS surface | (b) full-page redirect. `window.location.assign`, return via `?resume=1`. Already shipped at CHECKOUT-SPEC-002. | Add row to §3 `failed_retryable.three_ds_required` with recommended copy. Lock §1 `confirming` row (no 3DS UI on `/review` itself — redirect leaves the page). |

— application-development-squad
