# DR-REFUNDS-001: Refunds v1 Spec Resolutions (Locked)

**Status:** Accepted — binding on all squads
**Date:** 2026-06-24
**Author:** ideation-research-planning-squad
**Supersedes:** Ambiguous clauses in `NEXT-VERTICAL-refunds.md` (pre-`cc08d34`)
**Related:** `NEXT-VERTICAL-refunds.md` (commit `cc08d34`), QA bundle `RefundsPreprodGateTests.cs` (GATE-RFD-01..06), UX spec `wi-refund-3-ux-spec.md` (commit `4c84355`)

---

## Why this record exists

Three spec gaps + one cap question were raised by quality-testing during gate review. Resolutions were made in chat (`cc08d34` patched the spec body) but chat context is volatile. This DR is the durable, citable source of truth. Downstream PRs MUST reference this DR number when implementing the affected behavior.

---

## R1 — Refund window anchor = `order.confirmedAt`

**Decision:** The 24-hour refund window starts at `order.confirmedAt`, the timestamp recorded when the order state machine transitions to `Confirmed`.

**Rejected alternatives:**
- `createdAt` — cart creation, pre-payment. Window could expire before payment even succeeds.
- `paidAt` — drifts with provider webhook lag. Punishes users for our async pipeline.
- `now() - 24h` server eval per request — non-deterministic, untestable.

**Implementation contract:**
- `refundEligibilityWindow.startsAt == order.confirmedAt` (assertion in `RefundWindowTests.WindowAnchor_Is_ConfirmedAt`).
- `confirmedAt` is the same field rendered on the confirmation page and surfaced in support tooling — single source of truth.
- Clock skew: server is authoritative. Client never computes eligibility.

**Test gate:** `GATE-RFD-02` (window anchor).

---

## R2 — Refund-after-cancel returns 409 Conflict with `reason` enum

**Decision:** Ineligibility responses collapse into a single HTTP 409 with a frozen `reason` enum. Per-code 422 responses (`REFUND_INELIGIBLE_STATUS`, `REFUND_WINDOW_EXPIRED`, `REFUND_ALREADY_EXISTS`) are removed.

**Response shape:**
```json
{
  "error": "order_not_refundable",
  "reason": "canceled | already_refunded | not_confirmed | window_expired"
}
```

**Rejected alternatives:**
- 410 Gone — implies resource is permanently unavailable. Order still exists and is queryable. Wrong semantics.
- Per-code 422 — frontend would string-match error codes across a sprawling enum. Reason field collapses this into one stable contract.

**Implementation contract:**
- `reason` enum is frozen. Adding a value requires a new DR.
- Frontend renders distinct copy per `reason` via a mapping table — NEVER string-matches the `error` field.
- 409 only fires for terminal ineligibility. Eligible orders return `eligibleActions: ["refund"]` on `GET /api/orders/{id}` (R1 contract).

**Test gate:** `GATE-RFD-03` (eligibility response shape).

---

## R3 — Provider `refundId` (e.g. `re_xxx`) NEVER serialized to clients

**Decision:** The provider-issued refund identifier is server-internal only. Our ULID `refundId` is the sole client-visible identifier.

**Security ID:** `SEC-RFD-001` (added to refunds security checklist).

**Rationale:**
1. Leaks provider identity. Today Stripe, tomorrow Adyen — client contracts must be provider-agnostic.
2. Provider IDs are PII-adjacent in some jurisdictions.
3. Support tooling reads the mapping from the admin API server-side; clients have no need.

**Implementation contract:**
- `re_` prefix MUST NOT appear in any client-facing response body, header, or logged DOM payload.
- `grep -r 're_' dist/` returns empty (verified by `GATE-RFD-06` and QA's `RefundModal.spec.ts`).
- Internal `refunds` Cosmos container stores `providerRefundId` for reconciliation; never projected into the public API.

**Test gates:** `GATE-RFD-06` (provider ID non-exposure), `SEC-RFD-001` (security checklist).

---

## R4 — Per-sub rate limit = 100 refunds / 24h (asymmetric with checkout's 1000)

**Decision:** Refunds use a 100-request-per-subject-per-24h rate limit, deliberately tighter than checkout's 1000.

**Rejected alternatives:**
- 1000 (symmetric with checkout) — checkout is high-volume retry-prone; refunds are low-volume deliberate. Symmetry is a false economy.
- 10 — too tight; legitimate operators (support agents acting on behalf) and edge cases (bulk corrections) would hit the wall.

**Rationale:**
- Realistic legitimate use: most users 0 lifetime refunds; power user maybe 5/year. 100/24h = ~20x headroom on the worst legitimate case.
- Credential-stuffing blast radius is proportional to cap. Tighter cap = smaller financial damage per compromised account before lockout.
- Refund abuse is more financially damaging per request than checkout abuse (money flows out, not in).

**Implementation contract:**
- 429 response on cap breach. `Retry-After` header set to seconds-until-window-reset.
- Rate limit key = `H(sub:refund-rate)`. Bound to authenticated subject; no per-IP fallback (would let one user DOS via shared NAT).
- Applies to `POST /api/orders/{orderId}/refund` only. `GET /api/refunds/{id}` is uncapped (status polling).

**Test gate:** `GATE-RFD-04` (rate limit enforcement).

---

## Cross-cutting

- **All 4 resolutions are committed** in `NEXT-VERTICAL-refunds.md` at `cc08d34`.
- **QA bundle aligned:** `RefundsPreprodGateTests.cs` GATE-RFD-01..06 assert these behaviors. `RefundIntegrationTests.cs` covers happy + boundary cases.
- **App-dev WI-REFUND-1 bundle** (commit `5d9b891`) implements R1, R2, R3. R4 (rate limit middleware) is part of WI-REFUND-5 (security squad).
- **UX spec** (`4c84355`) already wired to R2's `reason` enum and R3's non-exposure rule (see `RefundModal.spec.ts` § failure-code mapping + provider-ID grep).

## Authority

This DR is binding. If implementation reveals a fact that invalidates a resolution, file a follow-up DR (DR-REFUNDS-002) — do not silently deviate. The reviewer rejection protocol applies: any PR that ships behavior contradicting a resolution here is rejected, and the original author is locked out per `squad.agent.md` rules.

— ideation-research-planning-squad

> "A specification that everyone obeys is worth more than ten that everyone interprets." — Edsger Dijkstra (paraphrased)
