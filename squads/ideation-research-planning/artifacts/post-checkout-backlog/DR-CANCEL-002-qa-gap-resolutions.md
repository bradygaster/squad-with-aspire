# DR-CANCEL-002 — Cancellation v1 QA Gap Resolutions

**Status:** BINDING (reviewer-rejection-protocol applies)
**Filed:** 2026-06-24
**Predecessor:** DR-CANCEL-001 (commit `fa97ade`)
**Trigger:** QA bundle pre-positioned at `squads/quality-testing/.squad/session-state/92a195a8-01ac-4f79-9b23-17e39326062e/files/cancel-v1-test-plan/`. 4 spec gaps surfaced before WI-CANCEL-1 dispatch. Ratifying now to avoid v1→v1b reshuffle (pattern proven on refunds v1).

---

## R1 — `ORDER_NOT_CANCELLABLE.reason` closed enum

**Decision:** ACCEPT QA's proposed 4-value enum and precedence, with one rename.

**Frozen enum** (only these strings; frontend maps reason→copy, never string-matches):
```
already_canceled | already_refunded | window_expired | fulfillment_in_progress
```

**Precedence (server evaluates in order, returns first match):**
1. `already_canceled`     ← terminal state wins
2. `already_refunded`     ← terminal state wins
3. `window_expired`       ← time-bound, deterministic
4. `fulfillment_in_progress` ← runtime state, last to evaluate

**Why this order:** Terminal states (1, 2) are immutable facts — never racy. Window (3) is a deterministic clock check. Fulfillment (4) is the only runtime-mutable state and is checked last so a user who hits the API while fulfillment flips never gets a stale "already_canceled" answer when the true blocker is fulfillment.

**Renames vs QA proposal:**
- `fulfillment_in_progress` (QA's name) is accepted as-is — it is more honest than the earlier draft's `already_fulfilled` because a partially-picked order is not yet fulfilled but is no longer cancelable.

**REJECTED:** Adding `not_confirmed` to the enum. A `Pending` order has never been confirmed, so the cancel endpoint returns `404 NOT_FOUND` (IDOR-safe — same response as "order does not exist"), not `409`. Cancel only applies to `Confirmed` and later states.

**Test obligation:** `CancelReasonEnumTests.cs` MUST assert exactly these 4 values, no more, no less, and precedence ordering with constructed multi-match fixtures.

---

## R2 — Rate caps

**Decision:** ACCEPT QA's proposal as-is.

**Frozen limits:**
- `POST /api/orders/{orderId}/cancel`: **50 / sub / 24h** AND **250 / IP / 24h**, both must hold.
- `Retry-After: 3600` on 429 (advisory header, not enforced server-side).
- `GET /api/orders/{orderId}/cancel/status`: **uncapped** (mirrors refunds GET).

**Asymmetry ladder (locked):**
| Vertical  | sub-cap / 24h | IP-cap / 24h |
|-----------|---------------|--------------|
| Checkout  | 1000          | (none — IP=NAT risk on initial buy) |
| Refunds   | 100           | (none — DR-REFUNDS-001 R4)          |
| **Cancel**| **50**        | **250**                              |

**Why IP-cap on cancel but not refunds:** Cancel has a 60min natural window cap that bounds blast radius per legitimate order. Adding IP-cap defends against a credential-stuffed cancel-storm targeting many orders within window without NAT-DOS-ing legitimate corporate-network users (250/IP is ~5 cancels per second of usable rate per egress IP, well above any single-user behavior).

**Rate key:** `H(sub:cancel-rate)` and `H(ip:cancel-rate)` — both written, both checked, 429 returned if either trips.

**Test obligation:** `cancel-storm.js` k6 script asserts both caps independently (one scenario per cap).

---

## R3 — Cancel-during-refund-in-flight

**Decision:** ACCEPT QA's proposal. The refund-in-flight state is **distinct** from `already_refunded`.

**Behavior:**
- Refund exists in state `Requested` or `ProviderAccepted` (not yet `Settled` or `Failed`)
- Cancel POST returns `409 REQUEST_IN_FLIGHT` (new error code, NOT a reason in the enum)
- `Retry-After: 30` (refund settles within seconds-to-minutes normally)
- Once refund settles to `Settled` → cancel returns `409 ORDER_NOT_CANCELLABLE{reason=already_refunded}`
- Once refund settles to `Failed` → cancel returns `200` and proceeds (order is back to refund-eligible-but-not-yet-refunded state)

**Why a separate error code:** `REQUEST_IN_FLIGHT` is a temporary, retryable condition. `ORDER_NOT_CANCELLABLE` reasons are terminal-or-deterministic. Conflating them would force the frontend to retry on every `ORDER_NOT_CANCELLABLE` (wrong) or never retry (worse — refund settling within 30s would be missed).

**Error envelope:**
```json
{ "code": "REQUEST_IN_FLIGHT", "operation": "refund", "retryAfterSeconds": 30 }
```
The `operation` field discriminates which in-flight request is blocking (future-proofs against, e.g., a CS-initiated action also being in flight). It is the only place `operation` may appear; cancel responses never leak `cancelType`.

**Test obligation:** `CancelIntegrationTests.cs` covers all 3 transitions (refund-pending→retry, refund-settled→409 already_refunded, refund-failed→200).

---

## R4 — Webhook `cancel.rejected_by_provider` path

**Decision:** REJECT folding into `cancel.accepted=false`. Provider rejection is a **separate webhook event**.

**Frozen webhook event vocabulary** (canonical, after adapter normalization):
| Event                          | Meaning                                                     | State transition                       |
|--------------------------------|-------------------------------------------------------------|----------------------------------------|
| `cancel.accepted`              | Provider confirmed cancel; inventory released exactly here  | `CancelRequested → CancelAccepted → Canceled` |
| `cancel.rejected_by_provider`  | Provider explicitly refused (e.g. capture already settled, dispute open) | `CancelRequested → CancelRejected`     |

**Why separate, not a payload field:**
- A boolean field invites mistakes (caller checks `if (event.type == "cancel.accepted")` and silently misses rejected cases).
- Separate event types let the webhook router fan to different handlers — rejection path triggers user notification + audit-log entry with provider reason code; acceptance path triggers inventory release.
- Inventory release MUST be idempotent on `cancel.accepted` (already in R4 of DR-CANCEL-001). Adding `cancel.rejected_by_provider` keeps that invariant clean — rejection never touches inventory.

**Provider-specific mapping (internal to `IPaymentProviderCancelClient` adapters; never leaks):**
- Stripe: `payment_intent.canceled` → `cancel.accepted`; `payment_intent.cancel` API 4xx → `cancel.rejected_by_provider`; `charge.dispute.created` mid-cancel → `cancel.rejected_by_provider` with `providerReason="dispute_open"`.
- Adyen: `/cancels` success notification → `cancel.accepted`; `/cancels` `refused` notification → `cancel.rejected_by_provider` with `providerReason` from Adyen's refusal reason.

**Client-facing GET `/cancel/status` states:** `Requested | Accepted | Rejected | Canceled` — `Rejected` is terminal-and-cancelable-again (user may retry, subject to rate cap and window). `providerReason` is **never** surfaced to client (SEC-CANCEL-001 extension — providers' refusal taxonomies are PII-adjacent and non-stable).

**New GATE-CANCEL-07** (grep): `grep -r 'providerReason\|cancel_xxx\|re_xxx\|payment_intent' dist/` must return zero matches in client-shipped bundles.

**Test obligation:** `CancelIntegrationTests.cs` covers `cancel.rejected_by_provider` path → state machine ends at `CancelRejected`, inventory NOT released, user can POST cancel again (subject to rate cap), audit-log entry written with redacted `providerReason`.

---

## Dev seams owed by app-dev WI-CANCEL-1

**Decision:** ACCEPT QA's proposed seam list verbatim. App-dev owns all of these; QA consumes; review-deployment asserts via grep.

1. `IPaymentProviderCancelClient` + `FakePaymentProviderCancelClient` (production seam + test fake).
2. `IOrderClock : TimeProvider` (window-elapsed determinism).
3. `SeedCancellableOrder(orderId, confirmedAt, status, ownerSub, items)` test helper.
4. Dev-only `_debug/cancel-count/{orderId}` endpoint, gated `ASPNETCORE_ENABLE_TEST_AUTH=1` (matches refunds pattern).
5. Dev-only `_debug/webhook-dispatch-count/{orderId}` endpoint, gated `ASPNETCORE_ENABLE_TEST_AUTH=1`.
6. `CancelErrorEnvelope.Codes` constants (single source of truth) + `NotCancellable(reason)` mapper helper.

**Ownership boundary (mirrors refunds v1b — proven clean):**
- App-dev OWNS `Codes` constants and the `NotCancellable(reason)` mapper.
- QA CONSUMES `Codes` constants in `CancelIntegrationTests.cs` and `CancelReasonEnumTests.cs` (no string duplication).
- Review-deployment ASSERTS via `GATE-CANCEL-06` (no `cancelType` leak) and `GATE-CANCEL-07` (no providerReason / provider id leak).

---

## Apply order (locked, unchanged from DR-CANCEL-001)

Refunds v1 → 100% → SPM v1 → 100% → **Cancel v1**:
1. WI-CANCEL-6 (infra: containers, flag, RBAC) — day-1 parallel with CANCEL-2.
2. WI-CANCEL-1 (backend, including all 6 dev seams above).
3. QA bundle drops in (CancelIntegrationTests, CancelReasonEnumTests, PreprodGate-CANCEL).
4. WI-CANCEL-2 (UX) ∥ WI-CANCEL-3 (QA) ∥ WI-CANCEL-5 (security).
5. WI-CANCEL-4 (frontend).
6. WI-CANCEL-7 (CI greps + rollout: internal → 1% → 10% → 100%).

---

## Binding clauses

- This DR is binding under reviewer-rejection-protocol.
- Deviation from R1–R4 or seam list requires **DR-CANCEL-003**.
- All `GATE-CANCEL-0*` numbers are reserved: 01–05 in DR-CANCEL-001, **06** (`cancelType` leak — DR-CANCEL-001 R4), **07** (provider id / providerReason leak — this DR R4).
- The 4 frozen enum strings in R1 and the 2 webhook event names in R4 are wire-contract — any change is a breaking-API change requiring a new vertical.

> *"In theory, theory and practice are the same. In practice, they are not." — Yogi Berra*
