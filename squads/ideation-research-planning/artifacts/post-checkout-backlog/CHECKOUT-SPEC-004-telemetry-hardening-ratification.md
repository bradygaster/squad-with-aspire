# CHECKOUT-SPEC-004 — Telemetry call-site literal hardening + DR-CO-FRAUD-002 closure

**Status:** BINDING under reviewer-rejection-protocol
**Date:** 2026-06-24
**Branch:** `tamir/squad-fixes`
**Author:** ideation-research-planning-squad
**Supersedes:** Nothing. Amends CHECKOUT-SPEC-003 R2 (tightening, not replacing).
**Closes:** DR-CO-FRAUD-002 (flagged in SPEC-003, resolved by exp-design re-cut `99b2cc3`).

---

## Trigger

Exp-design's re-cut at `99b2cc3` (`squads/experience-design/artifacts/checkout/checkout-state-copy-amendment-sec-chk-008.md`) implemented SEC-CHK-008 R1 verbatim AND added a defense-in-depth telemetry rule beyond what SPEC-003 R2 mandated. Security squad's SEC-CHK-008 verdict on the re-cut is implicit approval (R1 fully satisfied, "insufficient_funds_terminal different payment method" tell eliminated).

Two pending items remain:
1. The hardened §5.2 telemetry rule (literal constant at `safeTrack` call site, NOT `response.reason` forwarding) needs formal ratification so QA can enforce it as a CI gate.
2. DR-CO-FRAUD-002 (security's flag on the differentiated insufficient-funds copy) needs explicit closure on the record so future agents don't re-open it.

---

## R1 — Telemetry call-site literal rule (BINDING)

Ratified from exp-design re-cut §5.2 verbatim. **Extends and tightens CHECKOUT-SPEC-003 R2**, does not replace.

### Rule

For the `checkout_failed_terminal` event:

- The `reason` field value MUST be the hardcoded string literal `'declined_terminal'` at the `safeTrack(...)` call site.
- The value MUST NOT be sourced from `response.reason`, `payload.reason`, `data.reason`, `envelope.reason`, `res.reason`, or any other server-response derivative — even though SPEC-003 R2 guarantees the wire only ever emits `"declined_terminal"` for terminal reasons.

### Rationale

Defense in depth. SPEC-003 R2 protects against the wire leaking a per-enum reason. The call-site literal rule protects against a future server regression (wire mistakenly emits raw enum) from silently bleeding into client telemetry. Two independent invariants must both fail before a fraud-oracle leak reaches analytics. Mirrors the pattern from SPEC-003 R1's analytics split: schema-layer enforcement is cheaper to verify than per-call discretion.

### Enforcement gate (CI, blocking)

Renumbered as **GATE-CO-08** (extends SPEC-003's 06/07 series).

```bash
# GATE-CO-08a — no response.reason forwarding into terminal telemetry call sites:
grep -rE "safeTrack\(\s*['\"]checkout_failed_terminal['\"]" src/checkout/ -A 5 \
  | grep -E "reason:\s*(response|payload|data|envelope|res)\."
# MUST return empty.

# GATE-CO-08b — the 4 underlying terminal enum strings appear nowhere client-side:
grep -rE "['\"](hard_decline|fraud_block|insufficient_funds_terminal|provider_rejected_permanent)['\"]" src/checkout/
# MUST return empty.
# Permitted exceptions: QA's `?_force_reason=` test seam helper file (single allowlisted path,
# documented in exp-design amendment §6) — NEVER inside src/checkout/ production code.
```

Both gates run as build-break in CI on any PR touching `src/checkout/**` or `tests/Checkout/**`. Owner: QA bundle 7 (frontend telemetry tests) — already in their queue.

### Boundary

This rule applies to **`checkout_failed_terminal`** only. The other 7 analytics events (per SPEC-003 R1) are unchanged:
- `checkout_failed_retryable` — full per-reason fidelity preserved (no coarsening, no call-site literal rule)
- `checkout_confirm_rejected` — full per-reason fidelity preserved (not security-sensitive)
- All 6 non-failure events — unchanged

---

## R2 — testid contract additions (BINDING)

Ratified from exp-design re-cut §6 verbatim. **Replaces** SPEC-003 R3's `checkout-retry-button absent in failed_terminal` rule with a stricter positive contract.

| testid | Surface | Bytes/href contract |
|---|---|---|
| `checkout-return-to-cart-button` | `failed_terminal` primary CTA | Label `"Try again"`, href `/cart`. Replaces the prior `checkout-retry-button` rendering in this state. |
| `checkout-get-help-link` | `failed_terminal` AND `failed_retryable` secondary CTA | Label `"Get help"`, href `/help/payments`. **Identical label and href across both states** — no "Contact us" vs "Why was this declined" branching surface. |
| `data-reason="declined_terminal"` | DOM attribute on `checkout-status-heading` in `failed_terminal` | Literal constant string. NEVER the underlying enum. |
| `checkout-retry-button` | `failed_retryable` ONLY | Absent in `failed_terminal`. QA's bundle 7 `expect(retryButton).toHaveCount(0)` assertion still holds. |

### QA bundle ownership

The fraud-oracle byte-equality test exp-design pre-authored in their §6 (`failed_terminal — fraud-oracle byte equality (SEC-CHK-008 R1)`) is owed by **QA bundle 6** (Playwright sweep via `?_force_reason=` across all 4 reasons, asserts byte-identical DOM snapshot). Mirrors security's R6 server-side timing test pattern at the DOM layer. Pre-authored stub in exp-design amendment §6.

---

## R3 — DR-CO-FRAUD-002 closure (BINDING)

**Status: RESOLVED — no spec needed.**

SPEC-003 R2 had flagged the `insufficient_funds_terminal` sub-copy ("different payment method" tell) as a potential per-reason byte-leak surface, deferred to security-hardening squad as DR-CO-FRAUD-002 if they wanted strict equalization.

The exp-design re-cut at `99b2cc3` eliminated the differentiated copy entirely (R1 single row, all 4 reasons byte-identical), implementing the strict equalization preemptively. The sub-copy now reads:

> "We weren't able to complete your order. Please review your payment details or try a different payment method."

This single string is shared across all 4 terminal reasons including `insufficient_funds_terminal`. The phrase "different payment method" is now generic guidance rather than an `insufficient_funds_terminal`-specific tell.

**Outcome:** DR-CO-FRAUD-002 is closed without filing. The accepted UX regression (no insufficient-funds-specific copy) is on record in exp-design's amendment §"Lost". Security may still differentiate server-side per SPEC-003 R2 / SEC-CHK-008 R4 (full enum fidelity preserved in risk-scoped data warehouse + SIEM). Re-opening requires a new DR with a fresh trigger.

---

## R4 — What is NOT in scope (boundary preservation)

- **R3 timing equalization (SEC-CHK-008 R3)** — server-side, app-dev owns, QA bundle 9 verifies. Not affected by SPEC-004.
- **R4 SIEM/dashboard access scope-down (SEC-CHK-008 R4)** — security + app-dev coordinate. Full enum fidelity preserved in scope-restricted surfaces. Not affected.
- **R5 Retry-After + T13 weight equality (SEC-CHK-008 R5)** — app-dev owns. Not affected.
- **DR-CO-NO-CHARGE-001** — working assumption (zero-auth across all 4 retryable) held until app-dev refutes. Not formalized this turn.
- **DR-CO-CART-DIFF-001** — working assumption (app-dev ships `cart.changes[]` on /cart reload post-CONFIRM_REJECTED) held until app-dev confirms. Not formalized this turn.
- **DR-CO-007/008/009** — non-binding direction in SPEC-002 still holds. No new triggers this turn.

---

## Apply order (unchanged)

1. refunds v1 → 100%
2. SPM v1 → 100%
3. cancel v1 → 100%
4. checkout v1 (`wi-checkout-1-mappers/` → `wi-checkout-1-backend/`)

Checkout contract surface for **WI-CHECKOUT-1** now frozen across:
- DR-CO-001..006 (v2 on 005)
- CHECKOUT-SPEC-001 (Q-CO-1/7/8 — state machine, auth, inventory)
- CHECKOUT-SPEC-002 (Q-CO-3/4/5/6/10 — 3DS, idempotency, price/shipping/tax)
- CHECKOUT-SPEC-003 (Q-CO-2/9 analytics + fraud-coarsening floor + testid contract)
- **CHECKOUT-SPEC-004 (telemetry call-site literal hardening + DR-CO-FRAUD-002 closure)**

Deviation requires CHECKOUT-SPEC-005.

---

## References

- Exp-design re-cut: `squads/experience-design/artifacts/checkout/checkout-state-copy-amendment-sec-chk-008.md` (commit `99b2cc3`)
- SEC-CHK-008 spec (security DM to exp-design + app-dev, commit context `4de8ec0`)
- CHECKOUT-SPEC-003 R2/R3 (commit `4de8ec0`)
