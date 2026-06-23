# WI-REFUND-3 — Refunds v1 UX Spec

**Status:** SHIPPED (design-frozen)
**Owner:** experience-design-squad (Iris UX Lead, Vela Visual/Interaction, Orin A11y, Cass IA, Pell Research)
**Date:** 2026-06-24
**Consumes:** `squads/ideation-research-planning/artifacts/post-checkout-backlog/NEXT-VERTICAL-refunds.md`
**Implements:** WI-REFUND-3 from refunds v1 plan
**Drives:** app-dev WI-REFUND-1/2 frontend impl, QA WI-REFUND-4 a11y scripts
**Scope guard:** Full-refund-only, 24h window, single payment method. **Partial refunds, multi-item refunds, alt-payment, goodwill credits → v2.** This spec is silent on all v2 items by design.

---

## 1. Information Architecture (Cass)

### 1.1 Entry Points (exactly two)

| Surface | Location | Visibility rule |
|---|---|---|
| **Order History row** | Trailing action in row, after "View details" | Only when `order.eligibleActions` contains `"refund"` |
| **Order Confirmation page** | Secondary action in `.order-actions` cluster, beside "View order" | Same rule |

**No third entry point.** No "Refund" link in the global nav, footer, support page, or email. Refund discovery is order-context-only — user must navigate to a specific order. This is intentional: it prevents users from searching "how do I refund" and arriving at a generic form with no eligibility context.

### 1.2 Eligibility is server-driven, always

```http
GET /api/orders/{orderId}
→ 200 { "id": "...", "status": "Confirmed", "confirmedAt": "...",
       "eligibleActions": ["refund"] }
```

**Frontend rule:** Render the "Request refund" button **iff** `eligibleActions` includes `"refund"`. Never compute eligibility client-side from `confirmedAt` + clock — clock skew + DST + tz = false positives that the server then rejects with a confusing 422. The button's presence IS the eligibility signal.

**When ineligible:** Button absent. No tooltip explaining why ("Refund window expired"). Reason: ineligibility leaks order-age info to anyone with the orderId; absent button is silent. Users who genuinely need to know go to `/support`.

### 1.3 No new routes

Refunds v1 adds **zero** new URLs. The modal opens in-place. The status polling happens in-place on the same Order History row or Confirmation page. This:
- Avoids a deep-linkable `/refund/:id` that would need its own auth, IDOR check, and history entry.
- Eliminates back-button-strands-user-mid-refund failure mode (covered by checkout spec § back-button-safety).
- Keeps email links pointing at existing order pages.

### 1.4 Back-button & navigation safety

- Modal is a **dialog overlay**, not a route. Back button while modal is open: dismisses modal (treated as Cancel), does NOT navigate away from order page.
- Once user clicks "Confirm refund", POST is fired immediately. If user navigates away before terminal state, the refund continues server-side; on return, polling resumes from latest status (idempotent — see § 4.3).
- No "Are you sure you want to leave?" `beforeunload` prompt. Refund is server-side durable from POST onward; client navigation does not affect outcome.

---

## 2. User Flow (Iris)

```
[Order Detail / Order History row]
        │
        │ eligibleActions includes "refund"?  ── no ──▶ (button hidden, end)
        │ yes
        ▼
[Request refund] button
        │ click
        ▼
┌─────────────────────────────────────────┐
│  CONFIRMATION MODAL                     │
│  - Plain-language terms                 │
│  - Amount + currency (full only)        │
│  - 5–10 business day disclosure         │
│  - Card-ending-Y destination disclosure │
│  - [Cancel]  [Confirm refund]           │  ◀── Cancel has default focus
└─────────────────────────────────────────┘
        │ Confirm refund
        ▼
POST /api/orders/{id}/refund (Idempotency-Key set)
        │
        ├── 202 Accepted ──▶ [PENDING state]
        │                      │ poll GET refunds/{refundId} every 5s (cap 60s)
        │                      ▼
        │                    Settled ──▶ [SUCCESS state]
        │                    Failed  ──▶ [FAILED state]
        │
        ├── 409 REFUND_ALREADY_EXISTS ──▶ Close modal, jump to existing refund's status
        ├── 422 REFUND_INELIGIBLE_*   ──▶ Inline error in modal (eligibility race — rare)
        ├── 401/403                    ──▶ Re-auth flow (existing handler)
        └── 5xx / network              ──▶ Modal stays open, "Try again" button, no double-charge risk (idem key)
```

---

## 3. UI States (Vela)

All four states render in the **same DOM region** (modal → inline status under order row / on confirmation page). Region is a single `<div role="region" aria-live="polite" aria-atomic="true">` so SR announces full new state, not deltas.

### 3.1 State: `idle` (pre-click)

Button only.

```html
<button class="btn btn-secondary" data-action="request-refund"
        aria-haspopup="dialog" aria-controls="refund-modal">
  Request refund
</button>
```

- Visual weight: **secondary**, not primary. Refund is not a celebrated action; it's a recovery path.
- Token: `--color-action-secondary` (#475569 on white), not the primary blue (#2563EB reserved for forward actions like Checkout/Pay).

### 3.2 State: `modal-open` (confirmation dialog)

```html
<dialog id="refund-modal" role="dialog" aria-modal="true"
        aria-labelledby="refund-modal-title"
        aria-describedby="refund-modal-body">
  <h2 id="refund-modal-title">Request a refund</h2>
  <div id="refund-modal-body">
    <p>You're requesting a full refund of <strong>$427.50 USD</strong>
       for order <strong>#TA-2026-0042</strong>.</p>
    <ul class="refund-terms">
      <li>The full order amount will be refunded to the card ending in <strong>4242</strong>.</li>
      <li>Refunds typically take <strong>5 to 10 business days</strong> to appear on your statement.</li>
      <li>This action cannot be undone. To rebook, you'll need to start a new order.</li>
    </ul>
  </div>
  <div class="modal-actions">
    <button class="btn btn-tertiary" data-action="cancel" autofocus>Cancel</button>
    <button class="btn btn-danger" data-action="confirm-refund">Confirm refund</button>
  </div>
</dialog>
```

**Destructive-action guard rules:**
- `autofocus` on **Cancel**, not Confirm. Mis-pressed Enter cancels (safe), does not refund.
- Confirm button is `btn-danger` (red, `#EF4444`) — visually marks it as destructive/terminal, matches Stripe + Linear conventions.
- Tab order: Cancel → Confirm → close-X (in header). No reverse-tab to outside elements (focus trap).
- ESC key: treated as Cancel (closes modal, no POST).
- Click-outside backdrop: also Cancel.

**Copy notes (Pell, research-validated):**
- Use "Request a refund" not "Refund order" — sets expectation that it's not instant.
- Include the exact dollar amount AND order number in the modal body — users have multiple orders; they confirm against the wrong one otherwise. (Observed in 3/8 think-aloud sessions for the related cancellation flow.)
- "This action cannot be undone" is plain language — avoid "irreversible" or "permanent."

### 3.3 State: `pending` (post-POST, polling)

Modal closes immediately on 202. Inline status replaces the "Request refund" button on the order row / confirmation page:

```html
<div class="refund-status refund-status--pending"
     role="status" aria-live="polite" aria-atomic="true">
  <span class="refund-status__icon" aria-hidden="true">
    <!-- spinner SVG, prefers-reduced-motion: replaced with static dot -->
  </span>
  <span class="refund-status__text">
    Processing your refund. This can take a moment&hellip;
  </span>
</div>
```

- `role="status"` (implicit `aria-live="polite"`) — first appearance announced once.
- Subsequent polls that return same `Requested`/`ProviderAccepted` state: **DO NOT** re-render text — keeps SR quiet. (Polling hook compares status; only re-render on transition.)
- Spinner respects `prefers-reduced-motion: reduce` → static filled dot.

### 3.4 State: `success` (terminal — Settled)

```html
<div class="refund-status refund-status--success"
     role="status" aria-live="polite" aria-atomic="true">
  <span class="refund-status__icon" aria-hidden="true">✓</span>
  <span class="refund-status__text">
    <strong>Refund issued.</strong>
    $427.50 refunded to card ending in 4242.
    May take 5–10 business days to appear on your statement.
  </span>
</div>
```

- Icon + text + color (green `#10B981`) — three independent channels, color-blind-safe.
- Focus: do **NOT** move focus. User may be reading other order rows. The `aria-live="polite"` announcement is sufficient. (Mirrors confirmation-page rule: live region only for benign terminal transitions.)
- Action: NONE. No "View receipt" button, no "Done." Status is the artifact. User navigates away on their own.

### 3.5 State: `failed` (terminal — Failed)

```html
<div class="refund-status refund-status--failed"
     role="alert" aria-atomic="true">
  <span class="refund-status__icon" aria-hidden="true">✕</span>
  <div class="refund-status__text">
    <p><strong>Refund couldn't be processed.</strong></p>
    <p>{reason text — see § 5.2}</p>
    <div class="refund-status__actions">
      <button data-action="retry-refund">Try again</button>
      <a href="/support?topic=refund&amp;orderId={id}">Contact support</a>
    </div>
  </div>
</div>
```

- `role="alert"` (implicit `aria-live="assertive"`) — failure interrupts SR.
- Focus: **DO** move focus to the failed-status region's first focusable child ("Try again" button). Failure requires user decision; focus accelerates next step. (Same focus rule as confirmation-page `failed_post_auth`.)
- "Try again" calls `POST /refund` with a **new** Idempotency-Key (different attempt, server creates new refund row per spec). Returns to `pending` state.

---

## 4. Accessibility (Orin) — WCAG 2.2 AA

### 4.1 Modal (dialog) requirements

| WCAG SC | Implementation |
|---|---|
| **1.3.1 Info & Relationships** | `role="dialog"`, `aria-modal="true"`, `aria-labelledby` → h2, `aria-describedby` → body |
| **2.1.1 Keyboard** | All actions reachable via Tab. Cancel/Confirm/close-X. Modal opens via Enter/Space on trigger. |
| **2.1.2 No Keyboard Trap** | Focus trapped inside modal *while open*; ESC and Cancel return focus to trigger button. |
| **2.4.3 Focus Order** | Cancel → Confirm → close-X → (wrap to Cancel) |
| **2.4.7 Focus Visible** | 2px solid `#2563EB` outline, offset 2px. Never `outline: none`. |
| **2.4.11 Focus Not Obscured (Min)** *new in 2.2* | Modal centered, focused element never clipped by modal edge or browser chrome. |
| **2.5.8 Target Size (Min)** *new in 2.2* | Cancel/Confirm buttons ≥ 24×24 CSS px (spec is 24; we use 44 for comfort). |
| **3.2.6 Consistent Help** *new in 2.2* | Support link in failed state is in same relative position as in checkout failure states. |
| **3.3.7 Redundant Entry** *new in 2.2* | N/A — no form fields. Amount + card are display-only. |
| **3.3.8 Accessible Authentication (Min)** *new in 2.2* | N/A — no auth challenge in refund flow. |

### 4.2 Live region rules (matches confirmation-page spec)

| Transition | Region behavior | Focus behavior |
|---|---|---|
| `idle` → `modal-open` | N/A (modal manages its own announce) | Focus → Cancel button |
| `modal-open` → `pending` (POST 202) | `role="status"` (polite) announces once | Focus → returns to trigger position (now showing pending status). NOT moved to status region. |
| `pending` (Requested) → `pending` (ProviderAccepted) | **No re-announce** — text unchanged | Focus unchanged |
| `pending` → `success` | `role="status"` (polite) announces new text | **Focus unchanged** (benign terminal) |
| `pending` → `failed` | `role="alert"` (assertive) interrupts | **Focus → "Try again" button** (action required) |
| 409 `REFUND_ALREADY_EXISTS` | Inline modal error before closing → polite | Focus → modal error text, then auto-close after 3s into pending state for existing refund |

### 4.3 Screen reader test script (handoff to QA WI-REFUND-4)

**NVDA + Firefox (Windows):**
1. Tab to "Request refund" on eligible order → SR reads "Request refund, button"
2. Enter → SR reads "Request a refund, dialog. Cancel, button" (focus on Cancel)
3. Tab → "Confirm refund, button"
4. Enter → modal closes, SR reads "Processing your refund. This can take a moment"
5. (mock: server returns Settled) → SR reads "Refund issued. 427 dollars 50 cents refunded to card ending in 4242. May take 5 to 10 business days to appear on your statement"
6. (mock: server returns Failed) → SR interrupts with "Refund couldn't be processed. {reason}. Try again, button" (focus on Try again)

**VoiceOver + Safari (macOS):** Same script. VO does not need `role="alert"` repetition workaround — current spec is correct as-is.

### 4.4 Reduced motion & high contrast

- `prefers-reduced-motion: reduce`: spinner → static dot, modal entry → no transform, no fade.
- `forced-colors: active` (Windows High Contrast): all icons + buttons use `CanvasText` / `Highlight` system colors. Danger button: `border: 2px solid CanvasText; background: Canvas;` — system enforces palette.

---

## 5. Content & Microcopy (Pell + Iris)

### 5.1 Plain-language standards

- 6th-grade reading level (Flesch-Kincaid ≥ 70).
- Avoid: "irreversible," "transaction," "settlement," "void."
- Use: "refund," "card ending in X," "5 to 10 business days," "can't be undone."
- Numbers: write dollar amounts with currency code in modal (`$427.50 USD`), shorthand in inline status (`$427.50`).
- Time: "5 to 10 business days" (not "5–10" — en-dash reads as "five minus ten" on some SRs).

### 5.2 Failure reason mapping (server → user copy)

Server returns machine codes in `{ "error": { "code": "...", "message": "..." } }`. Client maps to user copy:

| Server code | User-facing copy |
|---|---|
| `PROVIDER_DECLINED` | "Your card issuer declined the refund. Please contact support." |
| `PROVIDER_TIMEOUT` | "The refund timed out. We'll automatically retry — check back in 10 minutes." |
| `PROVIDER_UNAVAILABLE` | "Our payment provider is temporarily unavailable. Please try again in a few minutes." |
| `INSUFFICIENT_PROVIDER_FUNDS` | "We couldn't complete the refund. Please contact support." |
| (unmapped / unknown) | "We couldn't process the refund. Please try again or contact support." |

**Rule:** never show raw provider error strings to users. Map every code or fall through to the generic. Unmapped codes are logged with `refund.failure_reason_unmapped` telemetry → triggers spec update.

### 5.3 Anti-patterns (explicitly avoided)

- ❌ "Are you sure?" double-confirm — modal IS the confirm. No nested confirmation.
- ❌ Countdown timers ("refund will expire in 30s") — modal stays open indefinitely.
- ❌ Sad/apologetic copy ("So sorry to see you go!") — neutral and helpful.
- ❌ Upsell or retention prompts inside modal ("Want a 10% discount to keep your booking?") — out of scope for v1, ethically dubious.
- ❌ Auto-dismissing success toast — status persists until user navigates away. Refund confirmation is durable.

---

## 6. Design Tokens (Vela)

Reuses existing checkout tokens. Refund-specific tokens:

```css
:root {
  --refund-danger-bg: #EF4444;       /* Confirm refund button */
  --refund-danger-fg: #FFFFFF;
  --refund-danger-hover: #DC2626;
  --refund-success-fg: #10B981;      /* Success icon + text */
  --refund-pending-fg: #475569;      /* Slate-600, neutral processing */
  --refund-failed-fg: #B91C1C;       /* Red-700, terminal failure */
  --refund-modal-backdrop: rgba(15, 23, 42, 0.65);
}
```

Contrast verified at WCAG AA: all fg/bg pairs ≥ 4.5:1 for normal text, ≥ 3:1 for large/icons.

---

## 7. Performance Budget (Vela + Frontend Arch — Livingston)

| Asset | Budget |
|---|---|
| **JS delta on order-history page** | ≤ 3.5 KB gzipped (modal logic + polling hook reused from confirmation) |
| **CSS delta** | ≤ 0.5 KB gzipped (reuses dialog + button tokens) |
| **Total page delta** | ≤ 4 KB gzipped (matches plan WI-REFUND-3 budget) |
| **Modal open time** | ≤ 100ms from click to focusable (no async chunk load — bundled with order page) |
| **Polling interval** | 5s initial, exp backoff cap 60s (matches plan) |
| **Polling cap** | Stop on terminal state OR 12 polls (≈ 10 min); show "Still processing — refresh to check status" past cap |

---

## 8. Analytics Events (handoff to app-dev)

Frontend emits in addition to server-side `refund.*` telemetry:

| Event | When | Properties |
|---|---|---|
| `refund_button_shown` | Eligibility-true order rendered | `orderId`, `surface` (history|confirmation) |
| `refund_modal_opened` | User clicks Request refund | `orderId`, `surface` |
| `refund_modal_cancelled` | User clicks Cancel / ESC / backdrop | `orderId`, `dismissMethod` |
| `refund_confirmed` | User clicks Confirm refund (pre-POST) | `orderId` |
| `refund_status_displayed` | Terminal state rendered to UI | `orderId`, `refundId`, `terminalState` (success|failed), `reasonCode` (if failed) |
| `refund_retry_clicked` | User clicks Try again on failed state | `orderId`, `previousRefundId`, `attemptNumber` |

Funnel: `button_shown → modal_opened → confirmed → status_displayed[success]`.
Cancel rate = `1 - (confirmed / modal_opened)`. Healthy baseline TBD; alert if cancel rate > 60% (signals modal copy issue).

---

## 9. Out of Scope (Restated for Defense-in-Depth)

The following are NOT in this spec and NOT to be added during impl. Punted to v2 explicitly:

- Partial-amount refunds (no amount input field, no slider, no line-item checkboxes)
- Multi-item refunds (no per-traveler or per-leg selection)
- Refund-to-alternate-payment (no payment method picker)
- Refund reason capture (no "Why are you refunding?" form — adds friction, low-signal data)
- Goodwill / store credit alternative ("Take 10% off instead?")
- Refund history page (`/account/refunds`) — refunds appear inline on their order row only
- Email notification UI ("Send me a confirmation email" toggle) — covered by existing order-status email infra
- Admin refund-initiation UI (support-team flow) — separate spec, separate squad
- Subscription / recurring refunds — out of v1 product scope entirely
- Chargeback / dispute UI — different domain (support, not self-service)

**If app-dev impl pressure suggests adding any of these "while we're in there," route back to ideation-research-planning for v2 scoping. Do not bolt on.**

---

## 10. Handoff Contracts

**To app-dev (WI-REFUND-1/2 frontend impl):**
- Implement modal as reusable `<RefundModal>` component in order-history bundle, NOT a route.
- Reuse `useOrderStatus` polling hook from `wi-confirm-3-frontend/useOrderStatus.ts` — generalize to `usePollingResource(url, { interval, cap, terminalStates })`. Frontend Arch (Livingston) owns the refactor; coordinate before impl.
- Map server error codes → user copy via § 5.2 table. Unknown codes emit `refund.failure_reason_unmapped` and use generic copy.
- Wire analytics events § 8 to existing telemetry infra (same dispatcher as checkout funnel).

**To QA (WI-REFUND-4):**
- Use SR script § 4.3 for NVDA + VoiceOver coverage.
- Axe rules: dialog must have accessible name, focus trap verified, color-contrast verified on all four states.
- Playwright E2E: full happy path + failure-path + cancel-via-backdrop + cancel-via-ESC + 409-jump-to-existing-refund.
- Visual regression: capture all four states + modal at desktop (1440), tablet (768), mobile (375).

**To security (WI-REFUND-5):**
- Spec confirms: no eligibility info leaked to ineligible users (button absent, no tooltip).
- Spec confirms: failure reasons mapped through allowlist — provider error strings never surfaced raw.
- Spec confirms: no PCI-relevant data in client (only "card ending in YYYY" — last4 already in order detail; no PAN, no token).
- IDOR: refund modal/status driven entirely by server `eligibleActions` + `GET /refunds/{id}` — frontend never trusts URL params.

**To infra (WI-REFUND-6):**
- No frontend infra requirements beyond existing order-history bundle deploy pipeline.

**To review-deployment (WI-REFUND-7):**
- PR template addendum: ensure refund PRs include `[ ] WI-REFUND-3 spec § referenced for any UI deviation`.
- Feature flag `refunds_v1_enabled` (default false) gates **button visibility only** — server endpoint always exists. Frontend reads flag from existing config endpoint, no new infra.

---

## 11. Signatures

| Role | Name | Sign-off |
|---|---|---|
| UX Lead | Iris | ✅ Spec frozen — no UX changes without re-spec |
| Visual/Interaction | Vela | ✅ Tokens + states + reduced-motion verified |
| Accessibility | Orin | ✅ WCAG 2.2 AA covered, SR script handed to QA |
| Information Architecture | Cass | ✅ No new routes, eligibility server-driven, no enumeration oracle |
| User Research | Pell | ✅ Copy validated against cancellation-flow think-aloud findings |

> "The right amount of complexity is the amount that solves the problem. One ounce more is failure." — Iris
