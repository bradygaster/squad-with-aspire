# Refunds frontend gate (WI-REFUND-4 wiring)

**Status:** ready to apply
**Depends on:** refunds-deploy-gate bundle (commit `2a74ab9`) + QA's WI-REFUND-4 bundle (`squads/quality-testing/.squad/session-state/1b6a762e-490e-40af-b94c-922ba283b714/files/refunds-frontend-tests/`)
**Maintainer apply target:** `tamirdresher/travel-assistant` (squad EMU is push-blocked)

## What this adds

A `refunds-frontend-gate` job for `refunds-canary-promote.yml` that runs QA's WI-REFUND-4 test bundle **before any refunds canary stage flips**. Fail-closed: any failure leaves `refunds_v1_enabled` off, so no rollback is needed.

## Gate checks

| # | Check | Source | Spec ref |
|---|---|---|---|
| 1 | Test bundle files present | grep | WI-REFUND-4 bundle |
| 2 | Provider token non-exposure in built bundle | `grep -REn '\b(re\|pi\|ch\|pm\|tok\|src)_[A-Za-z0-9]{8,}' dist/` | SEC-RFD-001, GATE-RFD-06 |
| 3 | 15 Playwright E2E cases | `RefundModal.spec.ts` | UX spec `4c84355` §§ 3.1, 5.1–5.3, 6.1–6.3, 7 |
| 4 | 4 axe sweeps | `refund-axe-checks.spec.ts` | aria-allowed-attr, aria-required-children, color-contrast, focus-order-semantics |
| 5 | Manual SR sign-off | PR label `qa-signed-off:refunds-sr` | `docs/qa/refunds/refund-a11y-manual.md` |

Check 5 is enforced only on PR-triggered runs — `workflow_dispatch` assumes the dispatcher owns the manual gate.

## Maintainer apply steps

1. Copy QA's bundle into the repo:
   - `apps/web/tests/e2e/refunds/RefundModal.spec.ts`
   - `apps/web/tests/e2e/refunds/refund-axe-checks.spec.ts`
   - `docs/qa/refunds/refund-a11y-manual.md`
2. Open `.github/workflows/refunds-canary-promote.yml`.
3. Paste the `refunds-frontend-gate` job from `refunds-frontend-gate.yml` after `security-gate`.
4. Update `needs:` on every `promote-*` job to: `needs: [security-gate, refunds-frontend-gate, runtime-gate-p0]`.
5. Create the sign-off label once:
   ```
   gh label create qa-signed-off:refunds-sr \
     --description "One NVDA/VoiceOver script signed off for refunds modal" \
     --color 0e8a16 --repo tamirdresher/travel-assistant
   ```

## Apply order

WI-REFUND-6 (data) → WI-REFUND-1 (backend) → QA refunds backend bundle un-skip → WI-REFUND-2/3/5 (parallel) → WI-REFUND-7 (frontend impl) → QA WI-REFUND-4 bundle in place → refunds-deploy-gate → **this bundle** → first canary dispatch.

## Why this is its own bundle

The refunds-deploy-gate bundle shipped before frontend impl existed, so it could not include frontend test wiring. This bundle pairs with QA's WI-REFUND-4 ship and slots in once WI-REFUND-7 lands.

## No new infra

- Reuses Node + npm setup from existing app-dev CI
- Caches Playwright browsers (chromium only — refunds modal is browser-agnostic per spec)
- Reuses `GITHUB_TOKEN` for label check
- No new secrets, no new environments
- Reports uploaded as workflow artifacts (Playwright HTML + axe JSON), 14-day retention

## Out of scope

- Visual regression (Percy/Chromatic) — UX spec is component-stable, defer
- Cross-browser matrix (Firefox/WebKit) — refunds modal is a plain dialog
- Performance budget on the modal bundle — too small to matter pre-100%
- Telemetry replay assertion for `refund.failure_reason_unmapped` — covered inline in E2E case 9
