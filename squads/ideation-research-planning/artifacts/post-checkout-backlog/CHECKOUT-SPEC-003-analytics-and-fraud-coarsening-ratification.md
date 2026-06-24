# CHECKOUT-SPEC-003 — Analytics Split + Fraud-Coarsening Ratification

**Status:** Binding under reviewer-rejection-protocol
**Filed:** 2026-06-24
**Author:** ideation-research-planning-squad
**Supersedes:** None. Amends ideation's Q-CO-9 single-event proposal from CHECKOUT-SPEC-001.
**Source:** experience-design-squad `checkout-state-copy-and-analytics-spec.md` (commit `31e42b1`)
**Branch:** `tamir/squad-fixes`

---

## Scope

Ratifies experience-design's three substantive amendments to ideation's CHECKOUT-SPEC-001 Q-CO-2 / Q-CO-9 proposal as binding contract surface. Flags 3 open questions to app-dev as pending DRs (DR-CO-NO-CHARGE-001, DR-CO-CART-DIFF-001, DR-CO-3DS-SURFACE-001) that will fire if app-dev's answers deviate from the spec's working assumptions.

---

## R1 — Analytics Event Split (BINDING)

**Rejected:** ideation's single `checkout_failed` event with full `reason` prop.

**Binding shape:** 3 split events at client surface:

| Event | Reason fidelity | Rationale |
|-------|----------------|-----------|
| `checkout_failed_retryable` | Full per-reason (`gateway_timeout` / `provider_unavailable` / `soft_decline` / `three_ds_required`) | Not security-sensitive. Retryable reasons are user-actionable. |
| `checkout_failed_terminal` | **Coarsened — `reason="declined_terminal"` ALWAYS** | Fraud-oracle prevention. See R2. |
| `checkout_confirm_rejected` | Full per-reason (`out_of_stock` / `price_changed` / `shipping_unavailable`) | Not security-sensitive. Inventory/pricing reasons are user-actionable. |

**Plus 5 non-failure events** (8 events total): `cart_started`, `checkout_step_entered{step}`, `checkout_confirm_clicked`, `checkout_confirmed`, `checkout.<state>.reason_unmapped` family.

**Server-side analytics MAY differentiate** the 4 terminal reasons (data warehouse, risk team, finance reconciliation). **Client telemetry MUST NOT** — schema enforcement, not per-call discretion.

**Bundle budget:** ≤2KB gz (event const map + safeTrack wrapper). Matches confirmation page + cancel modal pattern.

---

## R2 — Fraud-Coarsening Floor (BINDING)

`failed_terminal` MUST render identically across all 4 underlying reasons (`hard_decline` / `fraud_block` / `insufficient_funds_terminal` / `provider_rejected_permanent`) on **every user-observable surface**:

| Surface | Rule |
|---------|------|
| DOM copy (heading, sub-copy, CTA labels) | Identical strings across all 4 reasons. "Your payment couldn't be processed" family. |
| DOM `data-reason` attribute | `"declined_terminal"` always, never the underlying enum. |
| Client telemetry event | `reason="declined_terminal"` always. |
| Response envelope | Same fields, same shape. No discriminator. |
| CTA labels | Identical. No "Contact us" vs "Get help" tells. |

**NOT coarsened (security-hardening's call, captured for awareness):**
- Server-side response timing (attacker iterating cards could measure — fraud_block may settle faster/slower than hard_decline)
- Server-side analytics + order record (full reason fidelity preserved)
- HTTP status code (assumed identical; app-dev confirms in wire shape)

**Floor not ceiling:** security-hardening invited (via exp-design DM) to tighten — e.g., kill `insufficient_funds_terminal` differentiated sub-copy that currently says "different payment method", or mandate server-side timing equalization. Any tightening files as DR-CO-FRAUD-002.

**Discipline gate GATE-CO-06 (NEW):** Frontend grep
```bash
grep -rE '"(hard_decline|fraud_block|insufficient_funds_terminal|provider_rejected_permanent)"' src/checkout/ \
  | grep -v 'data-reason' | grep -v 'telemetry/server-side'
# Must be empty.
```

**Discipline gate GATE-CO-07 (NEW):** Telemetry call grep
```bash
grep -rE "telemetry\.track\(\s*['\"]checkout\." src/checkout/
# Every call must resolve to a const ref. No `checkout.${state}` composition.
```

Numbering reserves: 01-05 from SPEC-001, 06-07 from SPEC-003.

---

## R3 — Testid Contract Additions (BINDING)

3 new hooks for QA bundle 1 frontend (pages-under-test config) and bundle 7 (terminal-error-focus):

| testid | rule |
|--------|------|
| `checkout-status-heading[data-state]` | `data-state` ∈ {`confirming`, `failed_retryable`, `failed_terminal`, `confirm_rejected`}. Single-attribute assertion for state-machine landing surface. |
| `checkout-status-heading[data-reason]` | `data-reason` = mapped reason enum OR `"unmapped"`. **`failed_terminal` MUST emit `data-reason="declined_terminal"` always** (R2). |
| `checkout-retry-button` | Present only in `failed_retryable`. Absent in `failed_terminal`. Discipline gate: `expect(retryButton).toHaveCount(0)` in every `failed_terminal` test. |

QA bundle 1 frontend (pages-under-test config) and bundle 7 (analytics + terminal-error-focus) unblocked end-to-end on the exp-design side.

---

## Pending DRs — Open Questions to App-Dev

Exp-design DM'd app-dev 3 questions. Each becomes a binding DR when answered. Working assumptions for now:

### DR-CO-NO-CHARGE-001 (pending)
**Q:** Does the state machine guarantee zero authorization captured for ALL 4 retryable reasons including `soft_decline`?
**Working assumption (binding until refuted):** YES — every retryable reason exits with no held auth. Copy "Your card was not charged" is honest across all 4 reasons.
**If refuted (e.g., `soft_decline` holds auth that auto-releases in N days):** Exp-design rewrites copy; soft_decline becomes a distinct reason family in `failed_retryable.*` with truthful copy.

### DR-CO-CART-DIFF-001 (pending)
**Q:** On `CONFIRM_REJECTED.out_of_stock` / `price_changed`, does `GET /cart` reload return `cart.changes[]` so frontend can highlight what changed?
**Proposed shape:** `cart.changes: [{ lineItemId, kind: "removed"|"price_increased"|"price_decreased"|"quantity_reduced", oldValue?, newValue? }]`
**Working assumption (binding until refuted):** App-dev ships `cart.changes[]` in /cart reload after CONFIRM_REJECTED. Same envelope discipline as `TAX_RECALCULATED.changedFields`.
**If refuted:** Frontend recovery copy degrades to "something changed, look around" — exp-design rewrites §4 copy and flags as UX regression.

### DR-CO-3DS-SURFACE-001 (pending)
**Status:** PARTIALLY RESOLVED by CHECKOUT-SPEC-002 R1 (in-browser redirect only v1, `returnUrl=/checkout/{id}/review?resume=1`). Iframe and native sheets deferred.
**Remaining ambiguity:** What does the user see between "click Place Order" and "redirect happens"? Spec'd by SPEC-002 as `CheckoutState.ActionRequired` + `GET /status` returning `actionRequired: { type: "redirect", url, returnUrl }`. Frontend does `window.location.assign(url)`.
**Working assumption (binding):** `ActionRequired` state renders SPEC-002's redirect contract. Exp-design adds row to §3 `failed_retryable` table with copy "Verifying with your bank…" + late-poll hint at 10s + no cancel affordance. Locks on next exp-design pass.
**No new DR needed unless** app-dev or exp-design surfaces a third option (modal iframe overlay, etc.) — that path is already deferred per SPEC-002.

---

## QA Bundle Status

| Bundle | Status | Unblocked by |
|--------|--------|--------------|
| 1 backend (enumeration guard) | ✅ Go | SPEC-001 R1 + SPEC-002 contracts at c80c3e4 |
| 1 frontend (pages-under-test) | ✅ Go | SPEC-003 R3 (testid contract additions) |
| 2 (idempotency duplicate-submit) | ✅ Go | SPEC-002 R2 per-cart + 15min + JCS |
| 4 (race / retry / 3DS resume) | ✅ Go | SPEC-001 R3 + SPEC-002 R1 + idempotency |
| 7 (terminal-error-focus + analytics) | ✅ Go | SPEC-003 R1 (8-event split) + R2 (coarsening floor) + R3 (testids) |
| 8 (session expiry) | ⏳ Partial | DR-CO-008 proposed (30min ratified, refresh rules); ratify when bundle 8 author needs exact contract |
| Tax recalc | ✅ Go | SPEC-002 R3 reviewSnapshot 60s TTL |

---

## Apply Order — Unchanged

refunds v1 → 100% → SPM v1 → 100% → cancel v1 → 100% → checkout v1.

Test plan stack ships in parallel.

---

## Contract Surface Status

WI-CHECKOUT-1 contract surface now frozen across:
- DR-CO-001..006 (v2 on 005)
- CHECKOUT-SPEC-001 (test plan blockers — state machine, auth, inventory)
- CHECKOUT-SPEC-002 (3DS / idempotency / price / shipping / tax)
- **CHECKOUT-SPEC-003 (THIS — analytics split + fraud-coarsening floor + testid additions)**

3 pending DRs (DR-CO-NO-CHARGE-001, DR-CO-CART-DIFF-001) listed above. DR-CO-3DS-SURFACE-001 resolved by SPEC-002 R1; no new DR needed.

**Deviation from SPEC-003 requires CHECKOUT-SPEC-004.**

---

## Open Items — Next Session

- Formalize DR-CO-008 (session TTL: 30min frozen, refresh rules in {Cart, Shipping, Payment} only) when QA bundle 8 author needs exact contract
- File DR-CO-NO-CHARGE-001 + DR-CO-CART-DIFF-001 as binding when app-dev answers
- Security-hardening response on §3.2 fraud-coarsening floor — if they want stricter (identical bytes everywhere, server-side timing equalization, HTTP status confirmation), files as DR-CO-FRAUD-002
