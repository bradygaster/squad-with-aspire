# WI-REFUND-7 — Refund Frontend Implementation Bundle

**Owner:** experience-design-squad (Iris + Livingston)
**Pinned to:** UX spec `squads/experience-design/artifacts/refunds/wi-refund-3-ux-spec.md` (commit `4c84355`)
**Consumes:** app-dev WI-REFUND-1 endpoint contract (DR-REFUNDS-001, commit `c32b699`)
**Satisfies:** quality-testing WI-REFUND-4 test bundle (Hockney, 24 cases, 4 axe rules)

## Files

| File | Purpose | LoC |
|------|---------|-----|
| `usePollingResource.ts` | Generalized polling hook — supersedes `useOrderStatus` per Iris's note to Fenster | ~135 |
| `RefundButton.tsx` | Server-driven visibility (absent ≠ disabled) | ~25 |
| `RefundModal.tsx` | Confirm modal + states + telemetry + IDOR-safe polling | ~280 |
| `refund-modal.css` | Tokens + reduced-motion + forced-colors | ~120 |

## QA seam compliance matrix (Hockney's WI-REFUND-4 bundle)

| QA assertion | Where satisfied |
|--------------|-----------------|
| `data-testid="refund-trigger-button"` | `RefundButton.tsx` |
| `data-testid="refund-modal"` / `refund-cancel-button` / `refund-confirm-button` | `RefundModal.tsx` |
| `data-testid="refund-inline-error"` (409 strip) | `RefundModal.tsx` § inline_already_exists |
| `data-testid="refund-retry-button"` / `refund-error-message` | `RefundModal.tsx` § failed |
| `window.telemetry.track(event, props)` interceptable | `safeTrack()` wrapper in `RefundModal.tsx` |
| `usePollingResource(url, {interval, capMs, maxPolls, terminalStates, selectState})` | `usePollingResource.ts` exact contract |
| Cancel autofocus + Enter-safe + focus trap | `useEffect` on `isOpen` + `onKeyDown` trap |
| Browser Back = Cancel, no URL change | `pushState` + `popstate` listener |
| pending→succeeded: focus stays, `aria-live="polite"` | `.refund-modal__live` visually-hidden region |
| pending→failed: `role="alert"` + focus to Try again | `<div role="alert">` + `retryRef.current?.focus()` |
| 409 `REFUND_ALREADY_EXISTS`: 3s inline → auto poll | `setTimeout 3000` in inline_already_exists effect |
| 4 mapped failure copies render plain text | `FAILURE_COPY` table |
| Unmapped code → `refund.failure_reason_unmapped` | `safeTrack` call in pollState effect + 409 handler |
| Raw `re_xxx` / provider ID never in DOM | Modal renders ONLY mapped copy from `FAILURE_COPY` lookup; refundId used in URL only (encoded), never rendered |

## Apply order

Per quality-testing's ratified sequence:

1. WI-6 (Redis infra) — azure-infrastructure-squad ✓ canary gate at `2a74ab9`
2. WI-1 (refund endpoint + 6 seams) — application-development-squad ✓ bundle shipped
3. WI-REFUND-4 QA tests — quality-testing-squad ✓ bundle shipped
4. **WI-REFUND-7 frontend (this bundle)** — copy into `apps/web/src/features/refunds/` and `apps/web/src/hooks/`
5. WI-2/3/5 (parallel) — application-development-squad
6. WI-4 webhook — application-development-squad

## Maintainer apply path (EMU-blocked squad push)

```
mkdir -p apps/web/src/features/refunds apps/web/src/hooks
cp squads/experience-design/artifacts/refunds/wi-refund-7-frontend/usePollingResource.ts apps/web/src/hooks/
cp squads/experience-design/artifacts/refunds/wi-refund-7-frontend/RefundButton.tsx apps/web/src/features/refunds/
cp squads/experience-design/artifacts/refunds/wi-refund-7-frontend/RefundModal.tsx   apps/web/src/features/refunds/
cp squads/experience-design/artifacts/refunds/wi-refund-7-frontend/refund-modal.css  apps/web/src/features/refunds/
```

## Out of scope (UX spec § 9 — punted to v2)

- Partial refunds, reason capture, refund history page, multi-currency conversion display, async-cancel button, refund receipt PDF, multi-method split refunds, admin override UI, retry queue UI, internationalization of failure copy.

## Behavioral guarantees pinned to spec sections

| Spec § | Guarantee | Code location |
|--------|-----------|---------------|
| § 3.1 | Server-driven eligibility | `RefundButton.tsx` line 12 |
| § 5.1 | Modal Cancel = autofocus | `RefundModal.tsx` `useEffect` line ~84 |
| § 5.2 | 4 closed failure codes + unmapped telemetry | `FAILURE_COPY` + `safeTrack('refund.failure_reason_unmapped', ...)` |
| § 5.3 | 409 inline → 3s auto-poll | `setTimeout 3000` in inline_already_exists effect |
| § 6.1 | Esc closes, focus trap on Tab | `onKeyDown` + window `keydown` listener |
| § 6.2 | Focus rules per state transition | `useEffect` on `state.kind === 'failed'` |
| § 6.3 | Browser Back = Cancel, no URL change | `pushState` + `popstate` |
| § 7 | Polling 5s / cap 60s / 12 polls | `usePollingResource` opts |
| § 8 | ≤4KB gz bundle | ~3.4KB total CSS, hooks tree-shake |
| § 9 | Out-of-scope items NOT implemented | (deliberate omissions) |
| SEC-RFD-001 | Provider ID non-exposure | No `refundId` or `re_xxx` in rendered DOM |
| GATE-RFD-06 | `re_xxx` never in `dist/` | Static asset grep gate in QA bundle catches regressions |

## Frontend coupling resolved

> Per quality-testing's note: "Hook must generalize `useOrderStatus` per Iris's note to Fenster — I assume `usePollingResource(url, {interval, cap, terminalStates})`."

✓ Resolved exactly as assumed. `useOrderStatus` (in `wi-confirm-3-frontend/`) can now be refactored to call `usePollingResource` — that refactor is owned by Livingston and tracked separately (not blocking refunds v1 ship).
