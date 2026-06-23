# Focus Management & Live Region Policy — Travel Assistant

**Owner:** experience-design-squad
**Status:** v1 — design-frozen
**Date:** 2026-06-24
**Applies to:** all async/polling/state-transition UI (checkout confirmation, refunds, future inventory holds, future booking modifications)
**WCAG target:** 2.2 AA (incl. SC 2.4.11 Focus Not Obscured, SC 2.5.8 Target Size, SC 3.2.6 Consistent Help)

This is the single source of truth for **when focus moves**, **when it doesn't**, and **what the screen reader announces** during async state transitions. Consolidates rules previously scattered across `checkout-confirmation-a11y-spec.md` and `refunds/wi-refund-3-ux-spec.md`. Frontend (Livingston), QA (NVDA/VO scripts), and a11y review (Orin) all reference this doc.

---

## 1. The Core Rule — Don't Steal Focus on Progress

> **Default: do NOT move focus when state advances along a happy path.** Use a polite live region. Move focus ONLY when the user must act, when context fundamentally changes, or when the prior focused element no longer exists.

Stealing focus during polling/progress = breaks keyboard users mid-task, interrupts screen reader mid-utterance, and is the #1 a11y complaint we have control over.

---

## 2. Decision Matrix

| Transition kind | Move focus? | Live region | Reason |
|---|---|---|---|
| `idle` → `pending` (user initiated) | ❌ No | `aria-live="polite"` "Processing your request" | User clicked; they know |
| `pending` → `pending` (poll tick, same state) | ❌ No | None | Don't announce nothing |
| `pending` → `success` / `confirmed` | ❌ No | `aria-live="polite"` success message | Progress, no action needed |
| `pending` → `reconciliation_delayed` (slow path) | ❌ No | `aria-live="polite"` "still working…" | Reassurance, no action |
| `pending` → `failed_post_auth` | ✅ Yes — to `<h1>` or primary error | `role="alert"` (assertive) | User must read + decide |
| `pending` → `inventory_released` / `canceled` | ✅ Yes — to `<h1>` | `role="alert"` | Context fundamentally changed |
| `pending` → `404 / forbidden` (IDOR-safe) | ✅ Yes — to `<h1>` "Order not found" | `role="alert"` | Prior context gone |
| `idle` → `modal open` | ✅ Yes — to **Cancel** (destructive) or first field (non-destructive) | None | Standard modal contract |
| `modal open` → `modal close (cancel)` | ✅ Yes — back to trigger | None | Standard modal contract |
| `modal open` → `modal close (success)` | ✅ Yes — to inline status near trigger | `aria-live="polite"` | User needs confirmation anchored |
| Toast / transient banner appears | ❌ No | `role="status"` (polite) | Non-blocking info |
| Validation error on submit | ✅ Yes — to first invalid field | `aria-live="polite"` summary | Actionable |
| Skeleton → content (initial load) | ❌ No | None (skeleton has `aria-busy="true"`) | Initial render is not a transition |

---

## 3. Live Region Contracts

Every page that polls or transitions MUST have:

```html
<!-- One polite region for progress/success -->
<div id="status-live" aria-live="polite" aria-atomic="true" class="sr-only"></div>

<!-- One assertive region for errors/blocking changes -->
<div id="alert-live" role="alert" aria-atomic="true" class="sr-only"></div>
```

**Rules:**
1. **One of each per page max.** Multiple `aria-live` regions on a page produce duplicate or stomped announcements in NVDA and VoiceOver.
2. **Clear before write.** Set to empty string, then on next tick set the new message. Without this, identical-text updates are silently dropped by SR.
3. **`aria-atomic="true"`** always — partial DOM diffing in live regions is unreliable across NVDA/JAWS/VO.
4. **Never put interactive elements inside a live region.** Announcements read the text; focus/tab order is separate.
5. **Throttle.** Minimum 1500ms between announcements. Poll-tick spam is worse than silence.

---

## 4. Focus Trap Contract (Modals)

When a modal opens:

1. Save `document.activeElement` as `returnFocusTo`.
2. Move focus per § 2 (Cancel for destructive, first field otherwise).
3. Trap Tab/Shift+Tab within modal. Last element → wraps to first.
4. **Esc closes** (treated as Cancel — confirms unsaved-changes guard if dirty).
5. **Browser Back closes** (history pushState/popstate — refund modal already implements this).
6. On close: restore focus to `returnFocusTo` IF it still exists in DOM; otherwise to the nearest stable landmark (`<h1>`).
7. `aria-modal="true"` + `role="dialog"` + `aria-labelledby` pointing at the modal's heading.
8. **Inert background:** apply `inert` attribute to siblings of modal root (not `aria-hidden`, which leaks focus on some SR/browser combos).

---

## 5. Polling-State Announcement Cadence

Pulled directly from `useOrderStatus.ts` + `usePollingResource.ts`:

| Poll # | Announcement (polite) | Notes |
|---|---|---|
| 0 (initial render, state=pending) | "Processing your request. This usually takes a few seconds." | Fires once on mount |
| 1–3 | (silent) | Polling, no state change |
| 4 (~12s in for refund @ 3s interval) | "Still processing. Hang on." | Reassurance kick-in |
| 6+ | (silent until state change or cap) | Don't nag |
| Terminal success | "Refund confirmed." or "Order confirmed. Confirmation number {ID}." | Fires once on transition |
| Terminal failure | `role="alert"`: "Refund failed. {mapped reason}. Try again or contact support." | Assertive |
| Poll cap reached without terminal | `role="alert"`: "Still waiting on confirmation. We've emailed you — check back in a few minutes." | Assertive, exit polling |

---

## 6. Forbidden Patterns (Auto-Fail in Review)

These are immediate rejection in design/a11y review:

1. ❌ `autoFocus` on success states or toasts.
2. ❌ `aria-live="assertive"` on anything that isn't an error or blocking change.
3. ❌ Multiple `role="alert"` regions on the same page.
4. ❌ `tabIndex={-1}` + `.focus()` on a `<div>` purely to announce text. Use a live region.
5. ❌ Putting a `<button>` inside a live region.
6. ❌ Announcing every poll tick.
7. ❌ Moving focus during inline form validation as the user types (only on submit).
8. ❌ Using `aria-hidden="true"` to hide background when modal is open (use `inert`).
9. ❌ Refreshing the entire page or route to "reset" state (breaks SR context).
10. ❌ Custom focus rings that fail SC 2.4.11 (Focus Not Obscured) — sticky headers/footers must not occlude focused element.

---

## 7. Reduced-Motion & Forced-Colors Pairing

Focus visibility is an a11y concern — these media queries are non-negotiable:

```css
*:focus-visible {
  outline: 2px solid var(--color-focus, #2563EB);
  outline-offset: 2px;
}

@media (prefers-reduced-motion: reduce) {
  /* Disable focus-ring transitions, modal slide-ins, skeleton pulse */
  *, *::before, *::after { animation-duration: 0.01ms !important; transition-duration: 0.01ms !important; }
}

@media (forced-colors: active) {
  *:focus-visible { outline: 2px solid CanvasText; }
  [aria-busy="true"] { forced-color-adjust: none; }
}
```

---

## 8. Test Hooks for QA (Hand-Off to quality-testing-squad)

Every component implementing this policy MUST expose:

| `data-testid` | Purpose |
|---|---|
| `live-region-polite` | Polite announcements channel |
| `live-region-assertive` | Alerts channel |
| `focus-return-anchor` | Element focus returns to on modal close |
| `{component}-heading` | h1/h2 that receives focus on terminal-failure transitions |

QA NVDA/VoiceOver scripts assert against these. No silent renaming.

---

## 9. Out of Scope (v1)

- Programmatic focus management for drag-and-drop reordering (no DnD in v1)
- Roving tabindex composites (no complex widgets in v1 — all native controls)
- `aria-flowto` (poor SR support, deferred)
- Multi-step wizard focus restoration on browser-back across routes (covered by route-level a11y spec, not this doc)

---

## 10. Squad Sign-Off

- **Iris (UX Lead):** *"Focus is a contract with the keyboard user. Break it and you've broken the product."*
- **Vela (Visual/Interaction):** *"A focus ring you can't see is the same as no focus ring."*
- **Orin (A11y):** *"Polite by default. Assertive only when the user has to act. The matrix isn't a suggestion."*
- **Cass (IA):** *"Every state transition is a navigation event in the user's mental model — treat it like one."*
- **Pell (User Research):** *"Three of five SR users in last study quit checkout when focus jumped mid-poll. Don't do that again."*
