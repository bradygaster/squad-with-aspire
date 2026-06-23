# DR-CANCEL-003 — Cancel v1 Gap-4 Amendment (Webhook Rejected-by-Provider)

**Status:** Binding | **Supersedes:** DR-CANCEL-002 R4 only | **Date:** 2026-06-24
**Filed by:** ideation-research-planning-squad
**Trigger:** QA pre-positioned bundle `25175cd4-…/cancel-v1-dr001-followup/` proposed an alternative Gap-4 resolution that is cleaner than DR-CANCEL-002 R4. Reconciling before WI-CANCEL-1 backend impl starts (post-SPM v1) to avoid v1→v1b reshuffle.

DR-CANCEL-001 (R1–R4) and DR-CANCEL-002 (R1–R3 + dev seams) remain binding as written.
DR-CANCEL-002 **R4 is replaced in full** by this DR's R4'.

---

## R4' — Webhook `cancel.rejected_by_provider` (REPLACES DR-CANCEL-002 R4)

### Decision

When the payment provider rejects an in-flight cancel request (e.g. Stripe `payment_intent.cancel` returns `failed`, Adyen `cancellation` returns `[refused]`, charge already-captured-and-disputed paths):

1. **Domain event:** `cancel.rejected_by_provider` (server-side event for audit + state machine).
   - **Internal only** — NOT serialized to clients, NOT a new client-facing event type.
2. **Order state transition:** `CancelRequested → Confirmed` (back to the pre-cancel terminal state).
   - **NOT** a new `Rejected` state. There is no terminal-and-retryable state in the v1 state machine.
   - Rationale: a rejected cancel leaves the order in the same shippable state it was in before the request. Adding `Rejected` doubles the surface area of every consumer (frontend banners, history filters, refund eligibility, analytics funnels) for a transient runtime outcome.
3. **Client-facing event:** `OrderStateChanged` (existing event, R3 channel reused — no new email template, no new push payload shape).
   - Notification-squad's existing `OrderStateChanged` consumer fires the "cancel could not be completed" copy variant. Copy ownership: experience-design.
4. **Refund eligibility:** Order becomes refund-eligible **immediately** on rejection (user's recourse).
   - 24h refund window anchor remains `order.confirmedAt` per DR-REFUNDS-001 R1 — NOT reset by the failed cancel attempt.
5. **Inventory:** No release. Inventory release stays bound exclusively to `cancel.accepted` (DR-CANCEL-002 R4 invariant preserved).
6. **`providerReason` field:** Captured to `cancel-audit` container (immutable, 7yr retention). Server-side telemetry only.
   - **MUST NOT** appear in any client-bound HTTP response body, webhook payload, email, or push notification.
   - **GATE-CANCEL-07 (P0):** `grep -ri 'providerReason\|provider_reason\|cancel_xxx\|re_xxx\|payment_intent' dist/ deployed-response-fixtures/ → zero matches.` Asserted by review-deployment at every canary stage.
7. **Rate cap:** A `cancel.rejected_by_provider` outcome **does NOT decrement** the user's 50/sub/24h cancel budget. The user did not get the outcome they paid the rate-limit cost for.
   - Implementation: rate-limit deduction occurs in the webhook handler on `cancel.accepted` path only, not at POST `/cancel` time. POST returns 202 and the budget is held in a "pending" counter; webhook resolves to "spent" (accepted) or "refunded" (rejected).
   - Refunds v1 pattern parity — refunds rate-cap behaves identically on `refund.failed`.

### Rejected alternatives

- **DR-CANCEL-002 R4 (original):** New client-facing `Rejected` terminal-and-retryable state + new event type `cancel.rejected_by_provider` surfaced to clients. **Rejected:** doubles state-machine surface area, requires every downstream consumer (frontend, history, refunds, analytics, email templates) to handle a new terminal state for a transient provider outcome.
- **Fold into `cancel.accepted=false` payload field:** **Rejected** — violates "events are facts, not flags" principle, makes idempotency keying ambiguous, complicates webhook replay-storm dedup.
- **Reset refund window on rejected cancel:** **Rejected** — gameable (user could cancel-fail-cancel-fail to extend refund window indefinitely).

### What changes from DR-CANCEL-002 R4

| Field | DR-CANCEL-002 R4 (superseded) | DR-CANCEL-003 R4' (binding) |
|---|---|---|
| Client-facing state on rejection | `Rejected` (new terminal-retryable) | `Confirmed` (pre-cancel state restored) |
| Client-facing event | New `cancel.rejected_by_provider` | Existing `OrderStateChanged` |
| Refund eligibility on rejection | Implicit (Rejected is terminal) | Explicit + immediate |
| `providerReason` non-leak gate (GATE-CANCEL-07) | Same — preserved | Same — preserved |
| Inventory release binding | `cancel.accepted` only | `cancel.accepted` only (unchanged) |
| Rate-cap deduction on rejection | Unspecified | Refunded (not spent) |

### Test bundle deltas (QA, no architectural rework)

QA's pre-positioned bundle at `25175cd4-…/cancel-v1-dr001-followup/` already encodes R4' (their proposal matched this DR before it was filed). Confirming bundle is correct as-shipped:

- `WebhookCancelRejectedByProviderTest.cs` — asserts `OrderStateChanged` fires with `state=Confirmed`, refund-eligible=true, `providerReason` in audit not response.
- `PreprodGate-CANCEL.cs` GATE-CANCEL-07 — provider-id/`providerReason` leak grep.
- `PreprodGate-CANCEL-RFD-coexistence.cs` — refund + cancel rate-limit counter independence.
- `RateLimitNonDeductionOnRejectTest.cs` **(new, needed):** Assert 50/sub/24h budget is restored after `cancel.rejected_by_provider` webhook. Pattern from refunds `RateLimitNonDeductionOnRefundFailedTest.cs`.

### Apply order (unchanged)

Refunds v1 → 100% → SPM v1 → 100% → cancel v1 (WI-CANCEL-1 backend with DR-001 + DR-002 + DR-003 R4').

### Binding

Under reviewer-rejection-protocol. Any deviation from R4' requires DR-CANCEL-004.

> *"Make it as simple as possible, but not simpler." — Einstein (paraphrased)*
