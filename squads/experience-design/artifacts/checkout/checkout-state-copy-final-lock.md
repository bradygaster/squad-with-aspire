# checkout-state-copy-final-lock.md

**Status:** FROZEN — final lock on exp-design contract surface for WI-CHECKOUT-1.
**Date:** 2026-06-24
**Author:** experience-design squad
**Inputs:** app-dev DR-CO-EXPDESIGN-ANSWERS-001 (commit `317e476`), CHECKOUT-SPEC-002/003/004, parent specs (e0c1bed, 31e42b1, 99b2cc3, b47c68f).
**Scope:** Closes my open Q1/Q2/Q3 to app-dev. Adds `failed_retryable.three_ds_required` row, specifies CONFIRM_REJECTED cart-diff surface treatment, and confirms `confirming` row has no 3DS rendering (redirect leaves page).

---

## §1 — `confirming` row, final lock

No change from `checkout-state-copy-and-analytics-spec.md` (31e42b1). Re-affirmed in light of Q3 answer (3DS = full-page redirect via SPEC-002 R1):

- `confirming` never renders any 3DS UI on `/review`. When `ActionRequired` arrives in the poll, the client calls `window.location.assign(actionRequired.url)` and the user is gone from `/review`. No nested challenge surface. No iframe. No modal.
- The only intermediate surface between `confirming` and a terminal state is `ActionRequired` (see `checkout-state-copy-action-required-amendment.md`, b47c68f), and that surface owns its own copy + telemetry rows.
- Late-poll hint at 10s remains as spec'd.

---

## §3.x — NEW ROW: `failed_retryable.three_ds_required`

App-dev Q3 confirmed: 3DS is full-page redirect (already specified at CHECKOUT-SPEC-002 R1). The `three_ds_required` reason therefore exists in two distinct surface positions:

| Where | Surface | Why |
|---|---|---|
| **First entry to challenge** | `ActionRequired` row (b47c68f) | Server says "go redirect." Client auto-navigates within 500ms. No retry copy — this is forward motion. |
| **After failed challenge return** | `failed_retryable.three_ds_required` (this row) | Server says "the verification didn't complete." Client renders a retry-affordance with re-redirect CTA. |

The `failed_retryable.three_ds_required` row is reached when the user returns from the provider via `?resume=1` AND the resume poll resolves to `failed_retryable` with `reason: "three_ds_required"` (the bank challenge was abandoned, dismissed, timed out, or failed). It is NOT reached on first entry to 3DS — that path goes through `ActionRequired`.

### Copy (frozen)

| Field | Bytes |
|---|---|
| `data-state` | `failed_retryable` |
| `data-reason` | `three_ds_required` (full fidelity — NOT a fraud-oracle surface per SPEC-003 R2) |
| Heading | `Verify with your bank to complete payment` |
| Sub-copy | `Your bank requires extra verification. You'll be redirected to verify, then returned here to finish your order. Your card was not charged.` |
| Primary CTA | `Verify with my bank` (action: `window.location.assign(actionRequired.url)`) |
| Secondary CTA | `Get help` → `/help/payments` |
| Focus | Move to `checkout-status-heading` (h1) per focus-policy §3 — this is a `failed_retryable` terminal-error UI surface |
| Live region | `polite`, announces heading + sub-copy on mount |

### Notes on the copy

- **"Your card was not charged"** is added — even though Q1 already confirmed the no-charge invariant covers `three_ds_required` (3DS is pre-authorization), users returning from a failed bank challenge are at peak anxiety about double-charges. Explicit reassurance is worth the 4 bytes.
- Primary CTA label is action-oriented (`Verify with my bank`), NOT generic (`Try again`). The user knows what failed; the CTA should name the action that resumes the flow.
- Secondary CTA is `Get help` → `/help/payments`, identical bytes to the same CTA in `failed_terminal` and other `failed_retryable` rows (SEC-CHK-008 floor — consistency reduces oracle surface even on non-fraud rows).
- No "different payment method" copy here — same reasoning as the `insufficient_funds_terminal` collapse (SPEC-004 R3): suggesting "try a different card" leaks intent and is also bad UX when the issue is the bank's 3DS step, not the card itself.

### Telemetry

`failed_retryable.three_ds_required` emits the existing `checkout_failed_retryable` event with `reason: "three_ds_required"` — full fidelity per SPEC-003 R1. No new event.

The bank-redirect-and-back round-trip is fully observable via the existing `checkout_3ds_redirected` + `checkout_3ds_resumed` pair from b47c68f. When `checkout_3ds_resumed.resumeOutcome === "failed_retryable"` correlates with a same-correlationId `checkout_failed_retryable.reason === "three_ds_required"`, we have the funnel.

App-dev's open Q3 to me (`three_ds_abandoned` vs `three_ds_failed` mapper distinction) is **closed**: ship as a single `three_ds_required` reason value. Granularity for funnel-loss attribution moves into the `checkout_3ds_resumed.resumeOutcome` enum instead — that event has the bank-side context (redirect duration, resume latency) the mapper distinction was trying to surface anyway, and keeps the DOM `data-reason` enum tight.

### Testids (additions)

| testid | Where | Contract |
|---|---|---|
| `checkout-verify-with-bank-button` | `failed_retryable.three_ds_required` primary CTA | Label `"Verify with my bank"`. Click handler calls `window.location.assign(actionRequired.url)`. Absent on all other `failed_retryable.*` rows. |
| `checkout-get-help-link` | `failed_retryable.three_ds_required` secondary CTA | Identical bytes to the same testid on `failed_terminal` and other `failed_retryable.*` rows (SPEC-004 R2). |

`checkout-retry-button` is **absent** on `failed_retryable.three_ds_required` (this row uses the bank-specific CTA instead). QA bundle 7 grep gate should assert: in `failed_retryable` rendering, `checkout-retry-button` OR `checkout-verify-with-bank-button` is present, never both, never neither.

---

## §4 — `CONFIRM_REJECTED` cart-diff surface treatment (locked)

App-dev Q2 confirmed the cart-diff contract. `GET /api/cart/{cartId}` returns `changes[]` for 60s after a `CONFIRM_REJECTED` event:

```
changes: [
  { lineItemId, kind, reason, ...kindSpecificFields }
]
```

Where `kind ∈ { removed | price_increased | price_decreased | quantity_reduced | shipping_unavailable }` and `reason ∈ CartChangeReasons.All`.

### Surface treatment rules

1. **No focus move on `/cart` mount.** The user navigated here intentionally (was redirected by the failed Place Order). Per focus-policy.md §1 (do not move focus on user-initiated navigation), focus stays at document start.

2. **One consolidated `polite` live-region announcement on mount**, fired once per page-mount, throttled to the focus-policy.md §2 1500ms minimum:

   - `0 changes` → no announcement (no diff to surface; live region stays empty)
   - `1 change` → `"1 item in your cart changed: {kind-summary}."`
   - `N changes` (N > 1) → `"{N} items in your cart changed: {grouped-kind-summary}."`

   Where `kind-summary` follows the consolidated-summary pattern from focus-policy:

   | Group | Summary fragment |
   |---|---|
   | `removed` × N | `"N removed"` (singular `"1 removed"`) |
   | `price_increased` × N | `"N price increased"` |
   | `price_decreased` × N | `"N price decreased"` |
   | `quantity_reduced` × N | `"N quantity reduced"` |
   | `shipping_unavailable` × N | `"N shipping unavailable"` |

   Groups joined with `, ` in priority order: `removed`, `shipping_unavailable`, `price_increased`, `price_decreased`, `quantity_reduced` (most consequential first — what's no longer obtainable comes before what just costs more).

   Example: 3 changes (1 removed, 2 price_increased) → `"3 items in your cart changed: 1 removed, 2 price increased."`

3. **Per-row inline visual highlight**, NOT a banner. Each affected cart row gets:
   - A 4px left-border accent token (`--color-attention-border`).
   - An inline status chip BELOW the product title (NOT above — preserves visual hierarchy).
   - An inline explanation `<p>` BELOW the chip.

   **No** top-of-page banner summarizing all changes. The live-region announcement does that job for AT users; sighted users get the per-row context which is more actionable.

4. **`shipping_unavailable` does NOT remove the line from the cart visually.** The row stays rendered, struck-through, with the inline explanation. Visual parity with `removed` would mislead — `removed` means the item is gone from the user's cart; `shipping_unavailable` means the item is in the cart but cannot ship to the current address (user can choose to remove, change address, or leave it).

5. **Mutation-clears-changes is invisible to the user.** When the user acts (removes a flagged item, changes quantity, etc.), the per-row treatment AND the live-region announcement clear on the next render. No "changes resolved" confirmation announcement — the action itself is the resolution.

6. **Direct nav with no changes** (`/cart` opened from nav, no recent `CONFIRM_REJECTED`) → no chip, no border, no announcement. Indistinguishable from a normal cart.

### Per-kind inline copy (frozen)

Chip label is the data-bearing surface (5-9 chars, fits chip without truncation). Inline explanation is the empathetic surface.

| `kind` | Chip label | Inline explanation `<p>` |
|---|---|---|
| `removed` | `Removed` | `This item is no longer available and has been removed from your cart.` |
| `price_increased` | `Price up` | `The price increased from {oldPrice} to {newPrice}. Review and continue when ready.` |
| `price_decreased` | `Price down` | `Good news — the price dropped from {oldPrice} to {newPrice}.` |
| `quantity_reduced` | `Qty reduced` | `Only {newQty} available. Quantity reduced from {oldQty} to {newQty}.` |
| `shipping_unavailable` | `Won't ship` | `This item can't ship to your address. Remove it or change your address to continue.` |

`reason` from the envelope (`out_of_stock`, `price_changed`, `shipping_unavailable`) is exposed as `data-reason` on the chip element for telemetry/QA selectors. NOT rendered as user copy — the per-kind explanation owns the user-facing reasoning.

### Testids (additions)

| testid | Where | Contract |
|---|---|---|
| `cart-change-chip` | One per affected cart row | `data-kind={kind}`, `data-reason={reason}`. Absent on unaffected rows. |
| `cart-change-explanation` | One per affected cart row | Wraps the inline `<p>`. Byte-comparable per kind. |
| `cart-changes-live-region` | One per page mount | The consolidated `polite` announcement target. `aria-live="polite"`, `aria-atomic="true"` per focus-policy §2. Empty when no changes. |

### Telemetry (additions)

One new event:

- `cart_changes_surfaced` `{ cartId, totalChanges, kindBreakdown: { removed?: N, price_increased?: N, ... } }`

Fires once per `/cart` mount when `changes.length > 0`. The `kindBreakdown` only includes keys for non-zero kinds (smaller payload, easier funnel analysis). Does NOT fire when changes clear via user mutation — that's not a surfacing event.

No `cart_change_acknowledged` per-row event in v1. The implicit acknowledgement (user mutation) is already captured by existing `cart_item_removed` / `cart_quantity_changed` events; adding correlation IDs there would over-couple cart telemetry to the CONFIRM_REJECTED flow.

### Bundle 4 (CONFIRM_REJECTED) impact

QA's bundle 4 unhappy-matrix tests for CONFIRM_REJECTED can now scaffold against:
- The 5-kind `changes[]` shape (frozen allowlist).
- The 3 new testids above (`cart-change-chip`, `cart-change-explanation`, `cart-changes-live-region`).
- The 1 new event (`cart_changes_surfaced`).

Per-kind byte-equality tests on `cart-change-explanation` text are advised. The `{oldPrice}`/`{newPrice}`/`{oldQty}`/`{newQty}` interpolation values come from the envelope — QA tests can use the `?_force_reason=` test seam pattern (extended to seed cart changes) once app-dev confirms a cart-debug seam exists (sensible default: mirror the existing `_debug/cancel-count/{orderId}` pattern with `_debug/cart-changes/{cartId}` for seeded changes — non-blocking, only affects test ergonomics).

---

## §5 — Open Q1 / Q2 / Q3 closure summary

| Q | Status | Action |
|---|---|---|
| Q1 — no-charge invariant across 4 retryable reasons | **Closed.** App-dev confirmed TRUE via state machine + idempotency. | No copy revision. "Your card was not charged" stays in all 4 `failed_retryable` rows AND in the new `three_ds_required` row. |
| Q2 — CONFIRM_REJECTED cart-diff contract | **Closed.** App-dev approved my proposed shape + 60s TTL + 5-kind allowlist. | §4 above — surface treatment locked. Bundle 4 unblocked. |
| Q3 — 3DS surface decision | **Closed.** App-dev confirmed (b) full-page redirect, already shipped at SPEC-002 R1. | §3.x above — `failed_retryable.three_ds_required` row added. ActionRequired row (b47c68f) unchanged. |

All 3 app-dev open Qs to me also closed:
- `redirectUrl` absolute vs relative → working assumption "absolute" holds; no client-side same-origin guard in v1.
- `returnUrl` echo on resume poll → not needed; existing correlation IDs suffice.
- `three_ds_abandoned` vs `three_ds_failed` mapper distinction → closed in §3.x: ship as single `three_ds_required` reason, granularity moves to `checkout_3ds_resumed.resumeOutcome`.

---

## §6 — Aggregate contract surface for WI-CHECKOUT-1 (frontend)

Exp-design side now complete across **5 documents**:

1. `checkout-flow-ux-answers.md` (e0c1bed) — 5 routes, page-level surfaces, no modals.
2. `checkout-state-copy-and-analytics-spec.md` (31e42b1) — 7-state copy table + 8 telemetry events.
3. `checkout-state-copy-amendment-sec-chk-008.md` (99b2cc3) — fraud-oracle coarsening across 4 terminal reasons.
4. `checkout-state-copy-action-required-amendment.md` (b47c68f) — 8th state `ActionRequired` + 2 telemetry events for 3DS redirect/resume.
5. **`checkout-state-copy-final-lock.md` (this file)** — closes Q1/Q2/Q3, adds `three_ds_required` retryable row, specs CONFIRM_REJECTED cart-diff surface treatment.

Frontend WI-CHECKOUT-7 ingests all 5 documents as inputs on first author. No further open asks from exp-design to anyone. Any deviation requires a CHECKOUT-SPEC-005 from ideation.

---

## §7 — Quotes

- 🎨 **Iris:** "The bank redirected the user. The user came back. The page they land on should name the action that resumes the flow, not pretend it was a generic failure."
- ✏️ **Vela:** "`Removed`, `Price up`, `Price down`, `Qty reduced`, `Won't ship`. Five chips, no ambiguity. Save the empathy for the sentence underneath."
- ♿ **Orin:** "One announcement on cart mount. Not five. Screen readers don't need a roll call — they need a summary."
- 🗺️ **Cass:** "`shipping_unavailable` keeps the row visible because the user might want to change the address, not the cart. Different decisions need different affordances."
- 🔬 **Pell:** "`cart_changes_surfaced` with kindBreakdown is the funnel signal we'll actually use. Per-row acknowledgement events were a trap."
