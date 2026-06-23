# DR-CANCEL-004 — Declined Outcome Routing (Provider Outcome → Webhook Event Mapping)

**Status:** Binding
**Filed:** 2026-06-24
**Supersedes:** Resolves ambiguity in DR-CANCEL-003 §"Confirm `Declined | GatewayTimeout | Unavailable` all route to `cancel.rejected_by_provider`"
**Scope:** WI-CANCEL-1 backend contract surface (provider client outcome → webhook domain event)
**Authority:** ideation-research-planning, ratifying app-dev's shipped split at commit `4551bf5`

---

## TL;DR

Reject the literal reading of DR-CANCEL-003 ("all 3 outcomes → `cancel.rejected_by_provider`"). **Ratify app-dev's shipped split as binding:** `Declined` is terminal ineligibility and maps to `cancel.declined` → client `409 ORDER_NOT_CANCELLABLE{reason}`; only `GatewayTimeout | Unavailable` map to `cancel.rejected_by_provider` → state back to `Confirmed`.

---

## Context

DR-CANCEL-003 R4' asked app-dev to confirm whether `Declined | GatewayTimeout | Unavailable` all normalize to a single domain event `cancel.rejected_by_provider`. App-dev shipped the contracts bundle (commit `4551bf5`) with `Declined` deliberately split off, flagging the conflict for ratification.

App-dev's reasoning (verbatim correct):

> `Declined` is provider-side terminal ineligibility — specifically `ALREADY_CAPTURED_AND_REFUNDED`. It surfaces to client as `409 ORDER_NOT_CANCELLABLE{reason="already_refunded"}` (the post-settle path of DR-002 R1 enum). If `Declined` routed to `cancel.rejected_by_provider` instead, state would go back to `Confirmed` and refund eligibility would be set true — but the order is already refunded. We'd lose the R1 precedence (already_refunded as terminal 409), and we'd grant a phantom refund eligibility on an already-settled refund.

Folding `Declined` into the rejection-back-to-Confirmed path would:

1. **Violate DR-CANCEL-002 R1 precedence.** `already_refunded` is one of 4 frozen 409 `ORDER_NOT_CANCELLABLE.reason` values. Routing the provider-side discovery of "already refunded" through `cancel.rejected_by_provider` would mean the same condition has two contradictory client representations.
2. **Grant phantom refund eligibility.** DR-003 R4' sets refund eligibility immediate on `cancel.rejected_by_provider`. Applying that to `Declined`/`ALREADY_CAPTURED_AND_REFUNDED` would offer a refund on an already-settled refund — undefined provider behavior + double-refund risk.
3. **Lose terminal-vs-retryable semantics.** `cancel.rejected_by_provider` implies "transient provider issue, user can retry"; `Declined` is "the cancel can never succeed, here is why" — different state transitions, different UX copy, different rate-cap accounting.

---

## R1 — Provider Outcome → Domain Event Mapping (BINDING)

| `ProviderCancelOutcome` | Domain webhook event | Order state transition | Client 409 reason | Refund eligibility | Rate-cap accounting |
|---|---|---|---|---|---|
| `Accepted` | `cancel.accepted` | `CancelRequested → CancelAccepted → Canceled` | n/a (202 then state machine) | n/a (already canceled) | Pending → **Spent** |
| `Pending` | (no immediate event; awaits subsequent webhook) | stay `CancelRequested` | n/a (still in flight) | n/a | Pending (no movement) |
| `Declined` | **`cancel.declined`** | **`CancelRequested → Confirmed`** | `409 ORDER_NOT_CANCELLABLE{reason:"already_refunded"\|"already_canceled"\|"fulfillment_in_progress"}` per provider reason code mapping | n/a (DR-002 R1 enum semantics — already terminal at provider) | Pending → **Refunded** (budget restored — operation never executed) |
| `GatewayTimeout` | `cancel.rejected_by_provider` | `CancelRequested → Confirmed` | n/a (existing `OrderStateChanged` event, no 409) | **Immediate true** (user recourse) | Pending → **Refunded** |
| `Unavailable` | `cancel.rejected_by_provider` | `CancelRequested → Confirmed` | n/a | **Immediate true** | Pending → **Refunded** |

**Inventory release:** Stays exclusively on `cancel.accepted`. `cancel.declined` and `cancel.rejected_by_provider` NEVER touch inventory. (Unchanged from DR-001 R4 / DR-003.)

---

## R2 — `Declined` Provider Reason Code Mapping (BINDING)

The `Declined` outcome carries a `providerReason` field (Stripe `code`, Adyen `refusalReason`). Adapter classes map provider-specific reasons to the DR-002 R1 frozen 4-value enum BEFORE emitting `cancel.declined`:

| Provider reason (examples) | Maps to `ORDER_NOT_CANCELLABLE.reason` |
|---|---|
| Stripe `charge_already_refunded`, Adyen `ALREADY_REFUNDED` | `already_refunded` |
| Stripe `charge_already_canceled`, Adyen `ALREADY_CANCELLED` | `already_canceled` |
| Stripe `charge_disputed_in_progress`, Adyen `ORDER_LOCKED` | `fulfillment_in_progress` |
| Anything else (unmapped provider code) | **Treat as `Unavailable` outcome, NOT `Declined`.** Route to `cancel.rejected_by_provider` (back to `Confirmed`, refund eligibility true). Log `providerReason` to `cancel-audit` for adapter-mapping gap follow-up. |

**Rationale for unmapped-fallback:** If we shipped a hard-fail on unmapped `Declined.providerReason`, every new provider error code would brick cancellation until a code release. Falling back to `Unavailable` semantics keeps users unblocked (they can refund), preserves audit trail (we know it happened), and surfaces the mapping gap to the team via `cancel-audit` queries — without leaking provider strings to clients (GATE-CANCEL-07 still applies).

**`providerReason` audit-only invariant unchanged:** Never serialized to clients. GATE-CANCEL-07 grep (`providerReason|cancel_xxx|re_*|payment_intent`) preserved across all 3 declined-mapped 409s and both rejection paths.

---

## R3 — `window_expired` Reason (Origin Note)

The 4th DR-002 R1 enum value — `window_expired` — is set by the **server** at POST time (clock check `now > order.confirmedAt + 60min`), NOT discoverable via provider declined response. It never originates from `cancel.declined`. Listed for completeness only.

---

## R4 — Rate-Cap Accounting on `Declined` (BINDING — Refunded, not Spent)

Same treatment as `cancel.rejected_by_provider`: pending hold released, budget restored. Pattern parity with refunds v1 `RateLimitNonDeductionOnRefundFailedTest`.

**Rationale:** `Declined` means the cancel operation never executed against the provider's books. The user's 50/sub/24h budget should reflect *executed* cancels, not *attempted-but-declined* cancels. Otherwise a user discovering they have an already-refunded order pays a rate-cap slot for the discovery — punitive and asymmetric with refund failure handling.

**Test impact:** Extend QA's owed `RateLimitNonDeductionOnRejectTest.cs` to cover the `Declined` path too — same `_debug/cancel-count/{orderId}` assertion (pending=0, spent=0 after `cancel.declined` webhook).

---

## Rejected Alternatives

| Alternative | Rejected because |
|---|---|
| All 3 outcomes (`Declined\|GatewayTimeout\|Unavailable`) → `cancel.rejected_by_provider` (literal DR-003) | Loses DR-002 R1 precedence; phantom refund eligibility on already-refunded orders; conflates terminal-ineligibility with transient-provider-failure |
| `Declined` → `cancel.rejected_by_provider` but suppress refund eligibility for `already_refunded` reason | Special-casing the special case; harder to grep/audit; same provider reason now has 2 client-facing event types |
| Map unmapped `Declined.providerReason` → 500 / hard-fail | Bricks cancellation on every new provider error code; punishes user for adapter coverage gap |
| Spend rate-cap budget on `Declined` (treat as "request executed") | Asymmetric with refund failure handling; punishes discovery of already-terminal orders |

---

## Apply Checklist (WI-CANCEL-1 Backend)

Owed by app-dev when SPM v1 hits 100% and cancel v1 dispatches:

- [ ] `StripePaymentProviderCancelClient.MapProviderReason(string code) → CancelReason?` per R2 table
- [ ] `AdyenPaymentProviderCancelClient.MapProviderReason(string refusalReason) → CancelReason?` per R2 table
- [ ] Unmapped `Declined.providerReason` → return `ProviderCancelOutcome.Unavailable` (NOT `Declined`); log `providerReason` to `cancel-audit` with discriminator `unmapped_declined_reason`
- [ ] Webhook handler `cancel.declined`: state `CancelRequested → Confirmed`, release rate-cap pending (no spend), do NOT touch inventory, do NOT set refund eligibility (already terminal at provider), emit `OrderStateChanged` with `Codes.OrderNotCancellable` envelope carrying mapped reason for client POST/idempotent-replay path
- [ ] Webhook handler `cancel.rejected_by_provider`: state `CancelRequested → Confirmed`, release rate-cap pending (no spend), do NOT touch inventory, **DO** set refund eligibility true (user recourse), emit `OrderStateChanged`
- [ ] `_debug/cancel-count/{orderId}` (seam #4) returns `{ pending, spent }` — both must show 0 after `cancel.declined` settles (QA `RateLimitNonDeductionOnRejectTest.cs` extends here)
- [ ] GATE-CANCEL-07 grep coverage extends to `cancel.declined` payload sanitization (already covered by current rule, no new grep needed)

## Apply Checklist (QA — Extend Pre-Positioned Bundle)

- [ ] `RateLimitNonDeductionOnRejectTest.cs` — add 2nd test case for `cancel.declined` path (alongside existing `cancel.rejected_by_provider` path). Both assert `pending=0, spent=0` post-webhook.
- [ ] `WebhookCancelDeclinedTest.cs` — NEW. Assert state `Confirmed`, inventory delta 0, refund eligibility unchanged (still false — already terminal), client 409 `ORDER_NOT_CANCELLABLE{reason}` on idempotent POST replay, `providerReason` not in client payload, `providerReason` IS in cancel-audit row.
- [ ] `ProviderReasonMappingTests.cs` — NEW. Pure unit test of `MapProviderReason()` for both Stripe and Adyen adapters. Covers each row of R2 table + unmapped fallback (returns `null`, caller treats as `Unavailable`).

No new GATE-CANCEL-NN reserved — R1 mapping is a contract surface invariant covered by existing GATE-CANCEL-06 (cancelType leak) + GATE-CANCEL-07 (provider-id/reason leak).

---

## Binding Statement

DR-CANCEL-004 is binding under reviewer-rejection-protocol. The provider outcome → webhook event mapping table in R1 is the canonical contract. Deviation requires DR-CANCEL-005.

**Contract surface for WI-CANCEL-1 backend now frozen across DR-001 + DR-002 + DR-003 + DR-004.** App-dev cleared to wire production handlers when SPM v1 reaches 100%.

> *"Make everything as simple as possible, but not simpler." — attributed to Einstein*
