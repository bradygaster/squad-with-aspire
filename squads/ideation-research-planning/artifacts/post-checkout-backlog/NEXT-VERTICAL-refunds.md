# Next Vertical: Refunds (Self-Service, Full Only — v1)

**Filed by:** ideation-research-planning-squad
**Date:** 2026-06-24
**Depends on:** Order History (shipped/in-flight) + Checkout (shipped)
**Repo:** tamirdresher/travel-assistant (EMU blocks `gh issue create` — this file IS the issue substitute)
**Branch target:** `tamir/squad-fixes`

---

## Scope (v1 — ruthlessly minimal)

**IN:**
- User-initiated **full refund** of a single completed order (no partial amounts).
- Eligibility window: **24h** from `order.confirmedAt`, status ∈ {`Confirmed`} only.
- Single payment method per order (refund goes back to the original card/wallet via provider — no manual selection).
- Provider: Stripe primary, Adyen backup (mirrors checkout).
- Idempotent refund creation (reuse checkout's `RedisIdempotencyStore`).
- Webhook-driven state machine: `Requested → ProviderAccepted → Settled | Failed`.

**OUT (explicitly v2+):**
Partial refunds. Multi-item line-level refunds. Refunds past 24h (becomes manual support ticket flow). Refunds on subscriptions/recurring. Goodwill/courtesy credits. Refund to alternate payment method. Tax recalculation. Inventory restock signaling. Chargebacks/disputes. Email notification (covered by existing order-status email infra).

---

## Work Items

### WI-REFUND-1 — Backend API + State Machine (application-development-squad)

**Endpoint:** `POST /api/orders/{orderId}/refund`

- **AuthZ:** JWT required. `userId == sub` filter (reuse IDOR pattern from order-history WI-HIST-1). Return **404 not 403** on mismatch (no enumeration oracle).
- **Idempotency:** `Idempotency-Key` header required. Reuse `RedisIdempotencyStore` with derived cache key `H(sub:refund:orderId:key)`. Body-hash (JCS+NFC, RFC 8785) — 422 on mismatch.
- **Eligibility check (must all pass, else `409 Conflict` with `{ error: "order_not_refundable", reason: <enum> }`):**
  - `order.status == "Confirmed"` → else `reason: "not_confirmed"` (covers `Pending`, `Failed`, etc.)
  - `order.status != "Canceled"` → else `reason: "canceled"` (user already credited via cancel path; do NOT double-credit)
  - `now - order.confirmedAt <= 24h` → else `reason: "window_expired"`
  - No existing refund row for `orderId` with status ∈ {`Requested`, `ProviderAccepted`, `Settled`} → else `reason: "already_refunded"`
- **Window anchor:** `order.confirmedAt` (the moment the order state machine transitions to `Confirmed`). NOT `createdAt` (cart-creation, pre-payment) and NOT `paidAt` (drifts with webhook lag — we'd punish users for our async). `confirmedAt` is user-visible on the confirmation page and what support sees — deterministic.
- **`reason` enum (frozen for v1):** `not_confirmed | canceled | window_expired | already_refunded`. Frontend renders distinct copy per code; never string-match the `error` field.
- **Provider refundId non-exposure (SEC-RFD-001):** Provider IDs (`re_xxx` from Stripe, etc.) are NEVER serialized to client responses. Our `refundId` (ULID, opaque) is the only client-visible identifier. The provider mapping lives in the `refunds` container for admin/support tooling and webhook correlation only. Asserted by `RefundsPreprodGateTests.GATE-RFD-06`.
- **State transitions** (write to `refunds` container, partition `/orderId`):
  - `Requested` (on POST, before provider call)
  - `ProviderAccepted` (on provider 2xx with provider refund id)
  - `Settled` (on `refund.succeeded` webhook)
  - `Failed` (on provider 4xx/5xx after 3 retries OR `refund.failed` webhook) — refund row marked terminal, user may retry → new POST creates new row
- **Money:** integer minor units, copy `amount`+`currency` from order (no client input).
- **Webhook handler:** extend `/webhooks/payments` — dedup on `provider_event_id` (reuse existing). Map `refund.succeeded`/`refund.failed`/`refund.updated`.
- **Response:** `202 Accepted` with `{ refundId, status: "Requested", estimatedSettlementDays: 5-10 }`. Client polls via WI-REFUND-2.
- **SLO:** P95 < 400ms (provider call dominates), P99 < 1200ms.
- **Telemetry:** `refund.requested`, `refund.provider_accepted`, `refund.settled`, `refund.failed` (+ reason code).

### WI-REFUND-2 — Refund Status Endpoint (application-development-squad)

`GET /api/orders/{orderId}/refunds/{refundId}` — IDOR-safe, ETag, `Cache-Control: max-age=5`. Mirrors order-status polling pattern. Frontend polls 5s with exp backoff cap 60s, stops on terminal states (`Settled`, `Failed`).

### WI-REFUND-3 — UX Spec (experience-design-squad → owns spec, app-dev implements)

- **Entry:** "Request refund" button on Order History row + Order Confirmation page, **only visible when eligible** (server returns `eligibleActions: ["refund"]` on order detail — drives button visibility, server is source of truth, never trust client clock for window check).
- **Confirmation modal:** plain-language refund terms, 5-10 business day disclosure, full-amount-only callout, "Cancel" + "Confirm refund" (Confirm is **not** the default focus — destructive-action guard).
- **States:** Pending (spinner + "Processing your refund, this can take a moment"), Success (✅ + "Refunded $X to card ending Y. May take 5-10 business days to appear."), Failed (❌ + reason + "Try again" + support link).
- **Accessibility:** Modal trap focus, ESC closes (treated as Cancel), success/failure announced via `aria-live="polite"`. Color-blind-safe status (icon + text, never color alone).
- **Bundle:** ≤4KB gzipped delta on order-history page. No new route — modal + inline status only.

### WI-REFUND-4 — QA (quality-testing-squad)

- Contract tests: eligibility matrix (status × age × existing-refund) × expected status codes.
- IDOR matrix: caller ≠ owner returns **404 not 403**, no timing oracle (±10ms tolerance, reuse order-history harness).
- Idempotency tests: same key+body → same 202; same key+different body → 422; different key+same order → 409 `REFUND_ALREADY_EXISTS`.
- Webhook dedup: replay `refund.succeeded` 8× in 10s → exactly one terminal transition (extend `webhook-replay-storm.js`).
- E2E Playwright: happy path (eligible order → modal → confirm → poll → Settled), failure path (provider declines → Failed → retry produces new refund row).
- No load test required for v1 (expected < 1 RPS in prod for first 90 days; revisit at 10 RPS).
- Manual a11y: NVDA + VoiceOver scripts for modal + status announcements.

### WI-REFUND-5 — Security Review (security-hardening-squad)

- **IDOR review** (same depth as order-history) — confirm 404-not-403 across all 4 endpoints (POST refund, GET refund, list refunds, webhook).
- **Race conditions:** double-click on Confirm modal → second POST must hit 409 `{ error: "order_not_refundable", reason: "already_refunded" }` (Redis idempotency + DB unique constraint on `(orderId, status NOT IN terminal)`).
- **Webhook auth:** verify HMAC signature (Stripe `whsec_` + Adyen HMAC) — already enforced in checkout webhook handler, confirm refund event types are in allowlist.
- **PII in telemetry:** assert refund events log `orderId` + `refundId` only, never PAN/last4/email/provider refund id.
- **Rate limit:** 100 refund POSTs per `sub` per 24h (NOT symmetric with checkout's 1000 — refunds are 10-50x rarer per legitimate user; tighter cap = smaller blast radius on credential stuffing). 429 with `Retry-After` on breach. Asserted by `GATE-RFD-05`.
- **Audit log:** every refund POST writes immutable row to `refund-audit` container with `{userId, orderId, refundId, ip, ua, ts}` — retention 7 years (financial compliance baseline).
- **Out of PCI scope:** refund flow never touches PAN — provider handles, we only see provider refund id. SAQ-A scope preserved.

### WI-REFUND-6 — Infra (azure-infrastructure-squad)

- New Cosmos container `refunds`, partition `/orderId`, composite index `(orderId asc, createdAt desc)`. RU baseline 400 autoscale → 4000 (matches order container).
- New Cosmos container `refund-audit`, partition `/userId`, immutable (deny PATCH/DELETE via RBAC custom role). RU 400 fixed.
- App Config flag `refunds.enabled` (default **false**, dark-launched per-tenant).
- No new secrets (Stripe/Adyen keys already in Key Vault from checkout).
- Cost delta: **+$28/mo** (2 containers @ ~$14/mo each at baseline RU). Running total $586 + $28 = **$614/mo**.

### WI-REFUND-7 — CI + Release (review-deployment-squad)

- Extend `.github/workflows/checkout-ci.yml` contract-greps:
  - Assert `RedisIdempotencyStore` used in refund path (no `InMemoryIdempotencyStore`).
  - Assert IDOR 404-not-403 pattern present in `RefundEndpoints.cs`.
  - Assert webhook handler maps `refund.succeeded` and `refund.failed`.
  - Assert audit-log write present (grep for `refund-audit` container reference).
- Flag-gated rollout: enable for **internal tenant only** for 7 days → 1% real users → 10% → 100%. **No canary infra-gate** (read-mostly, low traffic, reversible via flag flip in <60s).
- Rollback: flip `refunds.enabled=false`. In-flight refunds at provider continue to settle via webhooks regardless of flag (correct behavior — never strand a user's money).
- PR template addendum: refund-specific checklist (eligibility check, idempotency, audit log, IDOR 404, flag default false).

---

## Dependencies & Critical Path

```
WI-REFUND-6 (infra: containers + flag) ──┐
                                          ├──> WI-REFUND-1 (API + state machine)
                                          │         │
                                          │         ├──> WI-REFUND-2 (status GET)
                                          │         │         │
                                          │         │         └──> WI-REFUND-3 (UX) ──> WI-REFUND-4 (QA)
                                          │         │
                                          │         └──> WI-REFUND-5 (security review)
                                          │
                                          └──> WI-REFUND-7 (CI greps + flag rollout)
```

**Day-1 parallel:** WI-REFUND-3 (UX spec), WI-REFUND-6 (infra Bicep).
**Blocking:** WI-REFUND-1 blocks 2/3/4/5. WI-REFUND-5 sign-off blocks flag enable beyond internal tenant.

---

## Acceptance Criteria (Go-Live Gate)

1. All 7 WIs merged to `tamir/squad-fixes`.
2. Contract greps green in CI.
3. Security WI-REFUND-5 sign-off recorded in `security-hardening` artifacts.
4. QA E2E + IDOR + idempotency + webhook-dedup suites green.
5. `refunds.enabled=true` for internal tenant for 7 calendar days with **zero** P0/P1 incidents and **zero** refund rows stuck in `Requested` > 30min (alert wired).
6. Audit log writing verified end-to-end (sample query returns expected row shape for last test refund).

---

## Open Questions Deferred to v2 (do not block v1)

- Partial refunds (line-item granularity) — needs new UX, eligibility re-think, and provider partial-refund API study.
- Goodwill credits (refund to wallet/credit instead of card) — needs ledger.
- Auto-refund on cancellation policies — needs policy engine.
- Customer-service-initiated refunds (CS console) — separate vertical.

---

**Status:** SPEC FILED — awaiting squad dispatch when capacity opens. No squad action required this turn.


---

## Changelog

### 2026-06-24 — Spec clarifications signed (resolves QA spec gaps)

QA (Hockney) surfaced 3 gaps + 1 cap-rationale question while writing the test bundle. All resolved and patched above:

1. **Window anchor → order.confirmedAt** (not `createdAt`, not `paidAt`). Deterministic, user-visible, immune to webhook lag.
2. **Refund-after-cancel → 409 Conflict with `reason: "canceled"`** (not 410 Gone — order still exists, semantics are state-conflict).
3. **Provider refundId never serialized to clients** (SEC-RFD-001). Our ULID `refundId` is the only client-visible identifier. Asserted by GATE-RFD-06.
4. **Rate limit 100/sub/24h** (not symmetric with checkout's 1000 — refunds are rare; tighter cap = smaller credential-stuffing blast radius). Asserted by GATE-RFD-05.

**Eligibility response shape changed:** old per-code 422 (`REFUND_INELIGIBLE_STATUS` etc.) collapsed into single 409 with a frozen `reason` enum: `not_confirmed | canceled | window_expired | already_refunded`. Frontend renders distinct copy per code; never string-match the `error` field.

App-dev: WI-REFUND-1 unblocked. Apply order ratified — WI-6 → WI-1 (with QA's 6 seams) → QA bundle drops → WI-2/3/5 parallel → WI-4 → WI-7.
