# WI-CANCEL-4 Amendment — URL Stability Through Modal Unmount Handoff

**Owner:** experience-design-squad (Cass — IA)
**Date:** 2026-06-24
**Status:** Design-frozen. Adds §12 to wi-cancel-4-ux-spec.md.
**Triggered by:** QA bundle 5 (`cancel-modal-unmount-lifecycle`, cbb6ac0d) — explicit handoff between modal surface (confirming) and inline-status surface (post-unmount) raised the question: what happens to URL state across that handoff?

---

## §12. URL State Lifecycle — `?action=cancel` Deep-Link

### 12.1 Frozen Rules

| # | Rule | Rationale |
|---|------|-----------|
| R1 | `?action=cancel` is **single-use**. Consumed by `replaceState` (NOT `pushState`) on first read. | Back button does not re-open the modal. Refresh does not re-open the modal. |
| R2 | Consumption happens **on modal open**, not on page load. | If the user navigates away from order page before clicking the email link's target row, the param is preserved until the modal opens. |
| R3 | Modal-open does NOT push a URL state. Modal-close (Cancel/Esc/Back) does NOT pop a URL state. | Modal is ephemeral UI, not a routable view. Matches refunds v1 pattern. |
| R4 | Modal-unmount-on-Confirm (per be87012) does NOT mutate URL. | Inline-status renders at the same URL. No navigation event. |
| R5 | Inline-status pending/terminal states do NOT push URL state. | Polling lifecycle is purely client-side. URL stays at `/orders/:orderId` (or wherever the user was). |
| R6 | Refresh during pending polling: state is lost (acceptable per v1 — server is source of truth, next poll re-establishes). User-facing: refresh shows the order page, eligibleActions reflects server reality (likely `cancel` absent if pending in-flight). | No persisted client state for ephemeral pending UI. v2 may add localStorage breadcrumb. |
| R7 | Refresh on terminal state (canceled/cancel_rejected/failed): server `order.state` reflects terminal outcome; page renders accordingly without re-polling. | Server-driven. Inline-status component reads `order.state` on mount; if terminal, render terminal UI directly. |
| R8 | Browser Back during modal-open: closes modal, URL unchanged. | Per WI-CANCEL-4 §3.2 (Cancel autofocus contract — Back behaves as Cancel). |
| R9 | Browser Back **after** modal-unmount (during pending polling or on terminal-error inline-status): standard browser back, navigates away from order page. No interception. | Polling state is lost; acceptable per R6. |
| R10 | `?action=cancel` arriving with order.eligibleActions NOT containing `cancel` (server says ineligible): modal does NOT open. Param is consumed (replaceState), inline message rendered explaining current ineligibility reason (server-provided). | Server is source of truth. Email deep-link may be stale; respect server state, don't enable destructive action. |

### 12.2 Consumption Mechanics

```
// On order page mount:
const url = new URL(window.location.href);
const action = url.searchParams.get('action');

if (action === 'cancel' && order.eligibleActions.includes('cancel')) {
  // Consume param BEFORE opening modal
  url.searchParams.delete('action');
  window.history.replaceState(window.history.state, '', url.toString());
  // Then open modal
  openCancelModal();
} else if (action === 'cancel') {
  // Ineligible path — still consume to prevent re-trigger on back/refresh
  url.searchParams.delete('action');
  window.history.replaceState(window.history.state, '', url.toString());
  // Render ineligibility inline (use server-provided reason from order.cancelIneligibilityReason if present, else generic message)
}
```

**Critical:** `replaceState` MUST be called before `openCancelModal()`. If modal-open is async (e.g., lazy-loaded component), guard against double-consumption with a ref flag.

### 12.3 Handoff Through Modal Unmount

The modal-unmount-on-Confirm transition (per be87012) is a **pure component swap** at the same URL:

```
URL: /orders/o_abc123          URL: /orders/o_abc123          URL: /orders/o_abc123
State: confirming              State: pending (polling)        State: canceled (terminal)
Surface: CancelModal           Surface: CancelStatusInline    Surface: CancelStatusInline
              │                            │                            │
              └─── Confirm click ──────────┘ ───── poll resolves ──────┘
                  (modal unmounts)          (no URL change)
```

URL is invariant through the entire lifecycle (post-consumption). This is intentional — the cancel flow is not a routable wizard.

### 12.4 Test Hooks for QA (WI-CANCEL-7 e2e)

| Test name | Assertion |
|---|---|
| `cancelDeepLink_consumesActionParam_onModalOpen` | After modal opens via `?action=cancel`, `window.location.search` does NOT contain `action=cancel`. Modal IS visible. |
| `cancelDeepLink_ineligible_consumesParam_doesNotOpenModal` | When `?action=cancel` arrives and `eligibleActions` lacks `cancel`: param consumed, modal NOT rendered, inline ineligibility message rendered. |
| `cancelDeepLink_backAfterConsumption_doesNotReopenModal` | After modal opens via deep-link, user closes modal, presses Back: no navigation, modal does not re-open (param already gone). |
| `cancelDeepLink_refreshAfterConsumption_doesNotReopenModal` | After modal opens via deep-link, user refreshes page: modal does NOT re-open (param gone from URL). |
| `cancelModal_open_doesNotPushHistoryEntry` | `history.length` before openCancelModal === `history.length` after openCancelModal. |
| `cancelModal_unmount_doesNotChangeUrl` | `window.location.href` before Confirm click === `window.location.href` after modal unmounts (during pending). |
| `cancelInlineStatus_terminal_doesNotChangeUrl` | URL unchanged from pending → terminal-success and pending → terminal-error transitions. |
| `cancelRefresh_duringPending_serverStateDrivesUi` | Mock: refresh during pending; assert page renders based on server `order.state` (likely still `pending_cancel` or back to `confirmed`), NOT a re-opened modal. |
| `cancelRefresh_onTerminal_rendersTerminalInline` | Mock: server returns `order.state: 'canceled'` on page load; assert inline-status renders terminal UI directly without polling sequence. |

### 12.5 Discipline Gates

```bash
# Modal-open MUST NOT use pushState:
grep -rE "pushState.*cancel-modal|cancel-modal.*pushState" src/

# Param consumption MUST use replaceState, not delete+push:
grep -rE "searchParams.delete\('action'\)" src/ | grep -vE "replaceState"

# Modal-close MUST NOT call history.back() (let browser handle):
grep -rE "history\.back\(\).*cancel|cancel.*history\.back" src/
```

All three must return empty (after WI-CANCEL-7 frontend ships).

### 12.6 Divergence from Refunds v1

Refunds v1 (`?action=refund`) follows the **same** R1–R5 rules. The divergence in modal-unmount behavior (refund modal stays mounted, cancel modal unmounts) is **invisible at the URL layer** — both flows keep URL stable through their respective lifecycles. R4 applies to cancel only because refund has no unmount event; the rule's spirit (no URL mutation on internal state transitions) is shared.

### 12.7 v2 Punt List

The following are explicitly v2:

- Persisting pending-poll state across refresh (localStorage breadcrumb)
- Modal-open as a routable view (`?cancel=open` style)
- URL-encoded preselected ineligibility-reason explanation
- Cross-tab cancel state synchronization (BroadcastChannel)
- Deep-link audit telemetry (which channel — email/SMS/push — triggered the open)

---

## Open Questions to App-Dev (additive to wi-cancel-4-ux-spec §9)

**Q4:** On page load with `?action=cancel` AND `order.eligibleActions` containing `cancel`, AND `order.state === 'pending_cancel'` (a concurrent cancel is already in-flight from another channel): does eligibleActions reflect this (cancel removed)? If yes, R10 covers it. If no, frontend needs an extra guard to detect "in-flight cancel exists" and treat as ineligible. **Recommendation:** server SHOULD remove `cancel` from eligibleActions while a cancel is in-flight, atomic with state transition. Confirms the spirit of DR-CANCEL-003.

**Q5:** Is there a server-provided `cancelIneligibilityReason` field on the order resource for the R10 inline-message case? If absent, frontend renders generic "This order can no longer be canceled." If present, frontend renders specific reason (mapped via `CancelErrorEnvelope.Reasons` projection — DR-CANCEL-005).

---

## Apply Order

WI-CANCEL-4 spec now has 12 sections (this amendment adds §12). Stacks cleanly on commit `be87012`. No conflict with any prior bundle. QA bundle 5 (`cancel-modal-unmount-lifecycle`) is the trigger and consumer — the 9 tests in §12.4 are additive to bundle 5's 5 patches.

**Frontend WI-CANCEL-7 owes:** consumption mechanics per §12.2, no new component (consumption logic lives in order page mount effect or cancel-flow custom hook).

**Bundle budget impact:** ~0.4KB gz (consumption logic only — no new UI).
