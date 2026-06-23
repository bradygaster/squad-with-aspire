# NEXT VERTICAL — Order Cancellation v1

**Status:** Queued (apply after Saved Payment Methods v1 ships)
**Owner:** ideation-research-planning-squad
**Date filed:** 2026-06-24
**Branch target:** `tamir/squad-fixes`

> "Trust the design. Test the behavior." — and give it a clean exit.

---

## Why this vertical, why now

Refunds v1 already references cancellation as a terminal state (`reason="canceled"` is in the frozen `409 NotEligible` enum per DR-REFUNDS-001 R2), but there is no surface today that *produces* that state from user action. Right now an order can only become `canceled` via webhook from the payment provider (auth-only-then-void path), which means the user has no UI affordance to abort an order in the window between `confirmed` and `fulfilled`.

Three things resolve cleanly when we ship this:

1. **Refunds v1 R1 anchor (`order.confirmedAt`) becomes complete.** Users currently confirm → wait → refund. After cancel they confirm → cancel (no money movement) → done. Refunds is for already-captured payments; cancellation is for the pre-capture / pre-fulfillment window.
2. **Saved Payment Methods v1 (queued next) does not need to model "cancel-with-saved-method" specially** — cancellation is payment-method-agnostic.
3. Support tickets in the current `tamir/squad-fixes` branch reference "I clicked confirm by accident" with no path to self-serve. CS-initiated cancel is out of scope (deferred to v2 per checkout out-of-scope list); user-initiated is the natural v1.

---

## Scope — what's IN v1

- **User-initiated cancel only**, from order confirmation page or order history (HIST-7 deep link).
- **Single window:** order must be in state `confirmed` AND `fulfillmentStatus != Fulfilled` AND within 60 minutes of `confirmedAt`.
- **Full cancel only** — no line-item cancel, no partial.
- **No money movement at our layer.** Payment provider receives a `void` request if auth-not-captured, OR a `full refund` request if already captured. Money-movement path is provider's responsibility; we record the intent and reconcile via webhook.
- **State machine:** `Confirmed → CancelRequested → CancelAccepted → Canceled` (terminal) | `CancelRejected` (terminal, e.g., already fulfilled at provider).
- **Idempotency** reuses `RedisIdempotencyStore` from checkout; key = `H(sub:cancel:orderId:Idempotency-Key)` + JCS body hash, TTL 15min. Same R1/R2/R3 from WI-1 binding.
- **Inventory** released on `CancelAccepted` (not on `CancelRequested`) to avoid double-release if provider rejects.

## Scope — what's OUT (v2+)

| Deferred | Why |
|----------|-----|
| Partial / line-item cancel | Same reason refunds v1 is full-only — UX + invariant complexity |
| CS-initiated cancel | Admin tool, separate auth scope, audit needs |
| Cancel after fulfillment (returns) | This is the returns vertical, not cancel |
| Cancel reason capture | Out-of-scope per checkout punt list; analytics-only would tempt scope creep |
| Goodwill credit on rejected cancel | Compensation policy, not a flow |
| Subscription cancel | Subscriptions are out-of-scope at the product level |
| Cancel during pending payment (3DS in-flight) | 3DS is out-of-scope; assume v1 only sees terminal `confirmed` orders |
| Notification email on cancel | Already covered by existing order-event consumer (no new code) |

---

## Work items

### WI-CANCEL-1 — Backend cancel endpoints + state machine
**Squad:** application-development-squad
**Estimate:** L

- `POST /api/orders/{orderId}/cancel`
  - Auth: bearer `sub` must match `order.userId` (IDOR-safe **404**, not 403, per refunds pattern).
  - Body: `{ "reason": "user_requested" }` (only enum value in v1; field reserved for v2 expansion).
  - Headers: `Idempotency-Key` required (400 if missing).
  - Eligibility check returns **409** with frozen reason enum:
    - `not_confirmed` — order state != confirmed
    - `already_canceled` — terminal cancel state
    - `already_refunded` — refund exists (cancel-after-refund is nonsense)
    - `window_expired` — > 60min since `order.confirmedAt`
    - `already_fulfilled` — `fulfillmentStatus == Fulfilled`
  - Success: **202 Accepted** + body `{ orderId, cancelId (ULID), status: "CancelRequested", requestedAt }`.
- `GET /api/orders/{orderId}/cancel/status` — 5s poll, cap 60s, terminal stop on `Canceled` / `CancelRejected`. ETag + `max-age=2`.
- Cosmos container `cancels`, partition `/orderId`, point-read on `(orderId, cancelId)`.
- Provider call via `IPaymentProvider.RequestCancelAsync(orderId, captureState)` — branches void vs refund inside the provider adapter; backend code path is identical.
- Webhook handler extension: `cancel.accepted` / `cancel.rejected` events transition state and (on accepted) release inventory hold via existing `IInventoryHold.ReleaseAsync`.
- SLO: P95 < 350ms for POST (provider call is async — we ack 202 immediately).

**AC:**
1. POST returns 202 with `cancelId` in < 350ms P95.
2. Duplicate POST with same `Idempotency-Key` + same body returns 202 with same `cancelId`. Different body → 422.
3. All 5 eligibility codes covered by integration tests.
4. IDOR: requesting `userId=A`'s order with `userId=B`'s bearer returns 404 with constant-time ±10ms (reuse refunds timing seam).
5. Webhook replay of `cancel.accepted` is idempotent on `provider_event_id` (reuse webhook dedup map).
6. Inventory released exactly once on first `cancel.accepted` event.
7. Provider `cancel_xxx` / `re_xxx` ID never serialized to client (extend SEC-RFD-001 → SEC-CANCEL-001).

---

### WI-CANCEL-2 — UX spec
**Squad:** experience-design-squad
**Estimate:** M

- Cancel CTA appears on confirmation page AND order history row, **only when server returns `eligibleActions: ["cancel"]`** (button absent, not disabled — same pattern as refunds button).
- Confirmation modal: non-default focus on "Keep order" (Cancel-the-cancel), Confirm cancel is the destructive action.
- Modal copy must NOT promise timing ("you'll get your money back in X days" — that's the provider's call).
- Window countdown ("You can cancel for the next 47 minutes") shown when remaining < 15min; hidden otherwise to avoid implying urgency.
- Live region (`aria-live="polite"`) for `CancelRequested → Canceled` transition.
- `role="alert"` + focus shift on `CancelRejected` with mapped copy per 5 enum codes.
- Telemetry events: `cancel.modal_opened`, `cancel.confirmed`, `cancel.modal_dismissed`, `cancel.terminal_state` (props: `outcome` = canceled | rejected, `reason_code` if rejected).
- Bundle budget: ≤ 5KB gzipped (similar to refund modal, slightly larger for countdown widget).
- Reuse `usePollingResource` hook generalized in WI-REFUND-4 — no new polling infra.

**AC:**
1. Design tokens match refunds modal (visual consistency).
2. Reduced-motion variant validated.
3. axe rules pass on `aria-allowed-attr`, `aria-required-children`, `color-contrast`, `focus-order-semantics`.
4. NVDA + VoiceOver manual scripts: happy path, rejected, window-expired-just-now (race), Escape, browser-Back.
5. Countdown widget does NOT cause re-render storm (1Hz tick max, paused when modal closed).

---

### WI-CANCEL-3 — QA bundle
**Squad:** quality-testing-squad
**Estimate:** L

- Contract tests for POST + GET + webhook handler (Xunit + WebApplicationFactory).
- IDOR matrix (3×3: own/other/nonexistent × confirmed/canceled/fulfilled) — all return 404 with timing within ±10ms.
- Idempotency tests: replay same body → same cancelId; different body → 422.
- Webhook replay storm extension (8 retries/10s, 10→50 RPS, 5min) — cancel.accepted dedup > 99.9%.
- Race conditions: double-click POST → only one cancelId, second returns 409 with `already_canceled` (after first lands) OR returns same cancelId (if within idempotency window).
- E2E: confirmation page → cancel → poll → terminal.
- Manual a11y scripts (5) for screen-reader regression.
- 5 P0 preprod gates (GATE-CANCEL-01..05):
  - 01: IDOR 404 with constant-time
  - 02: idempotency body-hashed + 422 on mismatch
  - 03: webhook dedup on provider_event_id
  - 04: inventory released exactly once
  - 05: provider IDs never in client payload (`grep -r 'cancel_\|re_' dist/`)

---

### WI-CANCEL-4 — Frontend impl
**Squad:** application-development-squad (UI implementation)
**Estimate:** M

- Modal component per UX spec, wired to QA's `data-testid` contract.
- `useCancelOrder` mutation hook (mirrors `useRefundOrder` pattern).
- Telemetry bridge reuses `window.telemetry.track`.
- Bundle ≤ 5KB gzipped (CI check).

---

### WI-CANCEL-5 — Security review
**Squad:** security-hardening-squad
**Estimate:** S

- IDOR matrix walkthrough.
- Idempotency cache-key derivation: `H(sub:cancel:orderId:Idempotency-Key)` (NOT cross-tenant — same R2 binding as WI-1).
- Rate limit: **50/sub/24h** (asymmetric: cancellations are even rarer than refunds; legitimate user cancels once per order, abuse pattern is enumeration). Per-sub only, no per-IP (NAT-DOS guard).
- Audit log: every POST → `cancel-audit` Cosmos container, immutable RBAC, 7yr retention (mirrors refunds).
- New checklist items:
  - **SEC-CANCEL-001:** Provider cancel/refund ID never serialized to clients.
  - **SEC-CANCEL-002:** Cancel cannot be initiated against an order that has any refund record (race window with refunds).
  - **SEC-CANCEL-003:** Window check uses `order.confirmedAt` (server time), never client-supplied timestamp.

---

### WI-CANCEL-6 — Infra
**Squad:** azure-infrastructure-squad
**Estimate:** S

- 2 Cosmos containers: `cancels` (`/orderId`), `cancel-audit` (immutable RBAC, 7yr).
- Composite index: `(orderId asc, requestedAt desc)` on `cancels`.
- Feature flag: `order_cancellation_v1_enabled`, default false.
- Cost delta: +$22/mo (2 containers, low write volume — cancels are << checkouts) → running total $684/mo on baseline.
- No new managed identity, no new private endpoint. Reuses checkout's Redis for idempotency.

---

### WI-CANCEL-7 — CI gates + flag rollout
**Squad:** review-deployment-squad
**Estimate:** S

- Contract greps in `.github/workflows/checkout-ci.yml`:
  - `RedisIdempotencyStore` usage in cancel endpoint
  - IDOR-404 path
  - Webhook handler registers `cancel.accepted` / `cancel.rejected`
  - Audit log write on every POST
- No canary infra-gate (read-write but money-neutral at our layer; flag-gated).
- Rollout: internal tenant → 1% → 10% → 100% via flag, each stage min 48h zero P0/P1.
- Rollback: < 60s flag flip. In-flight cancels in `CancelRequested` settle regardless (provider has the request).

---

## Critical path

```
WI-CANCEL-6 (infra)
   ↓
WI-CANCEL-1 (backend + state machine)
   ↓
WI-CANCEL-3 (QA bundle) ∥ WI-CANCEL-2 (UX spec)
                              ↓
                          WI-CANCEL-4 (frontend impl)
                              ↓
                          WI-CANCEL-5 (security review)
                              ↓
                          WI-CANCEL-7 (CI + rollout)
```

**Day-1 parallel:** WI-CANCEL-2 (UX), WI-CANCEL-6 (infra).

---

## Open questions (file as DR before WI-CANCEL-1 starts)

1. **Window length:** 60min proposed. CS data should confirm — if 95th percentile of "I want to cancel" tickets land within 30min, tighten to 30min and reduce abuse surface.
2. **Cancel + immediate re-order:** Does cancel return the items to the cart, or just empty the order? **Proposed default:** empty. Re-order is a separate user action. Avoids accidental double-charge if user thinks they only abandoned.
3. **Email notification:** Reuses existing order-event consumer. Confirm the consumer subscribes to `Canceled` state — if not, add a 1-line subscription (no new email template needed; reuses `order.statusChanged` template).
4. **Provider variance:** Stripe supports `payment_intent.cancel`; Adyen requires `/payments/{id}/cancels` OR `/payments/{id}/refunds` depending on capture state. Provider adapter must branch internally; backend caller sees one API.

File answers as `DR-CANCEL-001-spec-resolutions.md` before WI-CANCEL-1 dispatch.

---

## Go-live gate

- 48h internal tenant: zero P0/P1, zero stuck `CancelRequested` > 10min, zero double-inventory-release.
- 7-day at 100%: webhook dedup > 99.9%, no audit log gaps, no IDOR alerts.

---

## Apply order (after SPM v1 ships and refunds v1 hits 100%)

1. WI-CANCEL-6 (infra)
2. DR-CANCEL-001 (resolve open questions)
3. WI-CANCEL-1 (backend bundle)
4. WI-CANCEL-3 (QA bundle, pinned to backend contract)
5. WI-CANCEL-2 (UX spec, parallel from step 1)
6. WI-CANCEL-4 (frontend impl)
7. WI-CANCEL-5 (security review)
8. WI-CANCEL-7 (CI + rollout)

---

**EMU still blocks gh issue creation in `tamirdresher/travel-assistant`.** This spec is the issue substitute, committed to repo for maintainer apply.

> "The best feature is the one users can undo." — anon
