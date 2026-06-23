# WI-CANCEL-1 Contracts — DR-CANCEL-003 Reconciliation

**Status:** Additive reconciliation over DR-002 amendment (commit `6cfcddd`). Supersedes R4 state-machine + rate-cap claims only. R1 (enum), R2 (rate-cap *values* 50/sub + 250/IP), R3 (REQUEST_IN_FLIGHT), and all 6 dev seams are unchanged. Source of truth: `squads/ideation-research-planning/artifacts/post-checkout-backlog/DR-CANCEL-003-gap4-amendment.md` (commit `7fca086`).

**No production wiring yet.** Endpoint/repo/DI + rate-cap-pending-counter still land in `wi-cancel-1-backend/` after SPM v1 hits 100%.

## What changed since DR-002 amendment

| Surface | DR-002 (now stale) | DR-003 R4' (binding) |
|---|---|---|
| Order state on rejection | `CancelRequested → CancelRejected` (new terminal-and-retryable state) | `CancelRequested → Confirmed` (NO new state — back to where we started) |
| Client-facing event on rejection | (implied) new event type | Reuse existing `OrderStateChanged` (no new event, no new email template) |
| Refund eligibility after rejection | (unspecified) | Set true **immediately**; 24h window anchor remains `order.confirmedAt`, NOT reset |
| Rate-cap budget on rejection | (implied) spent | **Refunded** — POST holds in pending counter, webhook resolves: `accepted` → spent; `rejected_by_provider` → released |
| Inventory release | `cancel.accepted` only | `cancel.accepted` only (unchanged) |
| `providerReason` handling | audit container only, GATE-CANCEL-07 grep | unchanged |

## State machine (binding, replaces DR-002 §R4 state diagram)

```
Confirmed
    │
    │ POST /api/orders/{id}/cancel  (writes pending counter H(sub:cancel-rate-pending))
    ▼
CancelRequested
    │
    ├── webhook cancel.accepted          → CancelAccepted → Canceled
    │                                      (move pending → H(sub:cancel-rate-spent),
    │                                       release inventory, refund eligibility=false)
    │
    └── webhook cancel.rejected_by_provider → Confirmed
                                           (release from pending — budget restored,
                                            refund eligibility=true,
                                            OrderStateChanged emitted,
                                            providerReason → cancel-audit only,
                                            NO inventory release,
                                            NO refund window reset)
```

States: **Confirmed | CancelRequested | CancelAccepted | Canceled**. Four states, no fifth.

## Rate-cap split needed in WI-CANCEL-1 backend (owed seam refinement)

WI-CANCEL-1 backend MUST implement two-counter rate cap (mirrors refunds v1 rate-cap-refund-on-failed pattern):

| Counter | Key | Write site | Resolution |
|---|---|---|---|
| Pending | `H(sub:cancel-rate-pending)` | POST `/cancel` accepted into `CancelRequested` | Resolved by webhook |
| Spent | `H(sub:cancel-rate-spent)` | Webhook `cancel.accepted` handler | Terminal |
| Released | (decrement pending) | Webhook `cancel.rejected_by_provider` handler | Budget restored |
| IP pending | `H(ip:cancel-rate-pending)` | POST `/cancel` accepted | Same lifecycle |
| IP spent | `H(ip:cancel-rate-spent)` | Webhook `cancel.accepted` handler | Terminal |

429 trips if `pending + spent ≥ 50` (sub) OR `pending + spent ≥ 250` (IP). `Retry-After: 3600`.

**Compatibility:** Contracts bundle (`IPaymentProviderCancelClient` + `FakePaymentProviderCancelClient` + `ProviderCancelResult`) is unchanged. The `ProviderCancelOutcome.Rejected` enum value remains valid — it's an INTERNAL adapter outcome that the webhook normalization layer maps to the `cancel.rejected_by_provider` domain event. `Declined | GatewayTimeout | Unavailable` also route through the same webhook path per DR-003 reconfirmation.

## Confirmation owed to ideation (per their DR-003 ask)

> *"confirm `Declined|GatewayTimeout|Unavailable` outcomes all route to webhook normalization → `cancel.rejected_by_provider` domain event."*

**Confirmed with one nuance to preserve refunds v1 parity:**

| `ProviderCancelOutcome` | Domain event | Client-facing outcome |
|---|---|---|
| `Accepted` | `cancel.accepted` | 200 + state → CancelAccepted → Canceled |
| `Pending` | (none — await webhook callback) | 202 Accepted, state stays CancelRequested |
| `Declined` (terminal: `ALREADY_CAPTURED_AND_REFUNDED`) | `cancel.declined` + allowlisted `failureCode` | 409 ORDER_NOT_CANCELLABLE `{already_refunded}` after settle, OR refund-path eligibility |
| `Rejected` (runtime refusal) | `cancel.rejected_by_provider` | OrderStateChanged → Confirmed, refund eligibility=true |
| `GatewayTimeout` | `cancel.rejected_by_provider` | Same as Rejected (transient → user-initiated retry) |
| `Unavailable` | `cancel.rejected_by_provider` | Same as Rejected (transient → user-initiated retry) |

**Rationale for keeping `Declined` distinct:** Declined surfaces as `ORDER_NOT_CANCELLABLE{already_refunded}` (terminal client-facing 409), not as `OrderStateChanged → Confirmed`. The webhook MUST distinguish so the state machine sends the user to the correct error path. Folding Declined into `rejected_by_provider` would lose the `already_refunded` reason-code precedence (DR-002 R1 enum).

If ideation wants strict 1:1 (`Declined → rejected_by_provider` too), file DR-CANCEL-004 — but the current split preserves the R1 enum semantics without contradiction.

## What's superseded

The DR-002 amendment README (`README-DR-CANCEL-002-amendment.md` §R4) is partially stale on:
- "CancelRequested → CancelRejected (terminal-and-retryable, subject to rate cap + window)" — **replaced** by DR-003 R4' state machine above.
- Implicit rate-cap-spent-on-rejection — **replaced** by pending/spent split above.

DR-002 R4 facts that **remain valid:**
- `cancel.rejected_by_provider` is a distinct domain event (not folded into `cancel.accepted=false` or `cancel.declined`).
- `providerReason` audit container only, GATE-CANCEL-07 grep enforced.
- Adapter normalization table (Stripe `payment_intent.cancel` 4xx, Adyen `/cancels` refused → `cancel.rejected_by_provider`).
- Inventory release on `cancel.accepted` only.

## QA bundle delta (one new test owed beyond DR-002 deltas)

Per QA's DR-003 acknowledgment, QA owes `RateLimitNonDeductionOnRejectTest.cs` — pattern from refunds `RateLimitNonDeductionOnRefundFailedTest.cs`. Asserts 50/sub/24h budget is **restored** after `cancel.rejected_by_provider` webhook. Implementation seam = the pending/spent split documented above; WI-CANCEL-1 backend exposes it via `_debug/cancel-count/{orderId}` (seam #4) which MUST return both pending and spent counts so the test can assert restoration.

## Apply order (unchanged)

1. `wi-cancel-1-contracts/` (this bundle — DR-001 contracts + DR-002 amendment + DR-003 reconciliation, all contracts-only)
2. `wi-cancel-1-backend/` (endpoint, repo, DI, real `StripePaymentProviderCancelClient`, seams #2–#5, two-counter rate cap) — TBD post SPM v1 100%
3. `wi-cancel-1-frontend/` (experience-design owns) — TBD

## Contract surface — now frozen

DR-001 (state machine, window, asymmetric rate) + DR-002 (enum, rate-cap *values*, REQUEST_IN_FLIGHT, dev seams, GATE-06) + DR-003 (R4' rejection path, rate-cap pending/spent split, GATE-07 reconfirmed). No further contract changes anticipated for v1; out-of-scope v2 list (partial cancel, cancel+re-order, customer reason capture, goodwill credit, history page) routes back to planning.

App-dev cancellation queue: **contracts complete** (DR-001+002+003 fully covered). Backend impl blocked on SPM v1 100%.
