# WI-CONFIRM-3 — Confirmation Page Frontend Bundle

**Squad:** experience-design
**Date:** 2026-06-24
**Depends on:** application-development bundle `wi-confirm-1-order-status` (commit 1e30d04)
**Unblocks:** quality-testing WI-CONFIRM-2 (NVDA/VoiceOver scripts can now test against real DOM)

## Files

| File | Purpose |
|---|---|
| `useOrderStatus.ts` | Polling hook — 3s cadence, 10-poll cap, RFC 7232 ETag/304, IDOR-safe (404 only) |
| `ConfirmationPage.tsx` | `/checkout/confirmation/:orderId` page — 5 UI states, a11y-correct focus management |
| `confirmation-page.css` | Tokens, focus ring, reduced-motion, forced-colors, skeleton |

## Drop locations (when app-dev wires the frontend project)

```
src/components/checkout/
  useOrderStatus.ts
  ConfirmationPage.tsx
  confirmation-page.css
```

Then mount at route `/checkout/confirmation/:orderId` in the React router.

## Contract alignment with app-dev `wi-confirm-1-order-status`

| App-dev contract | Hook handling |
|---|---|
| `GET /api/checkout/orders/{orderId}/status` | ✅ called with `Bearer {authToken}` |
| 5 API states `pending\|confirmed\|payment_failed\|inventory_released\|canceled` | ✅ mapped to 5 UI states |
| 404 for IDOR (not-found ≡ forbidden) | ✅ single `not_found_or_forbidden` UI state, no inference |
| `ETag` weak (`W/`) | ✅ stored, sent as `If-None-Match` next poll |
| `Cache-Control: private, max-age=2` | ✅ honored implicitly via browser; we still poll at 3s |
| `304 Not Modified` | ✅ no re-render, pollCount still ticks |
| ETag excludes `updatedAt` (state-only) | ✅ matches a11y "announce only on state change" rule |

## A11y guarantees (matches `checkout-confirmation-a11y-spec.md`)

| Spec rule | Implementation |
|---|---|
| Do NOT move focus on pending→confirmed | `shouldMoveFocus` only true for `payment_failed` / `inventory_released` / `canceled` / `not_found_or_forbidden` |
| DO move focus on failed_post_auth | `headingRef.current.focus()` triggers on `shouldMoveFocus=true` |
| Live region announces every state transition | `role="status" aria-live="polite"` with clear-then-set NVDA workaround |
| `aria-busy="true"` on skeleton | Set on `.skeleton` `div` and on polling indicator section |
| Single h1 per state, descriptive | `COPY[uiState].h1` never reads "Order Status" |
| Color not the only signal | Icon (`✓`/`⟳`/`⏱`/`⚠`/`?`) + text + color, all three present |
| Reduced motion | `@media (prefers-reduced-motion)` disables shimmer + spinner animation |
| Forced colors (Win HCM) | `@media (forced-colors: active)` lets system tokens win |
| 4.5:1 text contrast | All copy uses gray-800 (#1f2937) or blue-700 (#1D4ED8) on white |
| Visible focus ring | 3px outline, 2px offset, blue-600 — survives forced-colors via `Highlight` |

## Deliberate omissions / open items

- **Routing:** `onNavigate` is a prop so we don't hard-pin to any router (react-router-dom v6 vs Next.js App Router). App-dev picks the integration.
- **i18n:** copy strings are inlined English. Wrap in `t()` when i18n framework lands (not in scope for confirmation MVP).
- **Order details rendering:** confirmed state shows order number + "View your trips" link. Itinerary detail rendering is out of scope — that's WI-CONFIRM-4 (account/trips view).
- **Retry button:** `retry()` exported from hook but not wired to UI. Could be a "Refresh status" button on `reconciliation_delayed` — left for product to decide.

## QA handoff (quality-testing-squad WI-CONFIRM-2)

The hook + page give you stable DOM hooks for NVDA/VoiceOver scripts:

| Test scenario | DOM hook | Expected SR output |
|---|---|---|
| Initial load | `.skeleton[aria-busy="true"]` | "Loading your order, busy" |
| pending→confirmed | live region `role="status"` polite | "Your trip is booked" announced, focus stays on body |
| pending→failed_post_auth | h1 receives focus | "We couldn't complete your booking, heading level 1" |
| Poll cap hit on pending | live region | "Still working on it" |
| 404 from API | h1 receives focus | "Order not found, heading level 1" |
| 304 between polls | (no SR change) | silence — verifies no spurious announcements |

axe-core rules to assert in CI:
- `aria-allowed-attr` (catches misused aria-busy)
- `aria-required-children` (catches malformed live region)
- `color-contrast` (catches token regressions)
- `focus-order-semantics` (catches the focus-move policy)

## EMU delivery

Squad account EMU-blocked from PR on `tamirdresher/travel-assistant`. Maintainer apply path:

1. Copy three files from `squads/experience-design/artifacts/checkout/wi-confirm-3-frontend/` into the frontend project under `src/components/checkout/`.
2. Mount `<ConfirmationPage orderId={params.orderId} authToken={auth.token} onNavigate={navigate} />` at `/checkout/confirmation/:orderId`.
3. Verify axe-core scan passes in dev build.

— experience-design-squad
