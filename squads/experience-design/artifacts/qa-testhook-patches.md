# QA Test-Hook Patches — focus-and-live-region-policy.md §QA Test-Hook Contract

**Date:** 2026-06-24
**Branch:** tamir/squad-fixes
**Driver:** QA's focus-live-region-enforcement bundle (asks (a) and (b))
**Owner:** experience-design-squad

## What changed

Added the three test-hook attributes QA's `focus-live-region.spec.ts` /
`forbidden-patterns.spec.ts` / `polling-cadence.spec.ts` need to run against
checkout-confirmation + refunds modal without runtime sniffing or selector
fragility.

### `ConfirmationPage.tsx` (WI-CONFIRM-3)
1. `data-testid="live-region-status"` on the existing polite live region.
2. `data-testid="poll-state"` `<span>` mirroring the state machine. Values:
   - `pending` ← `pending_reconciliation`
   - `terminal-success` ← `confirmed`
   - `terminal-error` ← `failed_post_auth` | `not_found_or_forbidden`
   - `reconciliation_delayed` ← `reconciliation_delayed`
3. `role="alert"` mirror with `data-testid="live-region-error"` in error states.
   Mirrors the h1 text (deduped by AT). h1 keeps focus per
   focus-and-live-region-policy.md §3 (terminal failure → focus to h1).

### `RefundModal.tsx` (WI-REFUND-7)
1. `data-testid="live-region-status"` added to the existing polite live region
   (was unlabeled).
2. `data-testid="poll-state"` `<span>` mirroring `state.kind`:
   - `pending` ← `submitting | polling | inline_already_exists`
   - `terminal-success` ← `succeeded`
   - `terminal-error` ← `failed`
   - `idle` ← `idle | confirming`
3. Error div role/testid restructured: outer wrapper now `role="alert"
   data-testid="live-region-error"`, inner `<span data-testid="refund-error-message">`
   preserves the existing testid QA already consumes for error-copy assertions.

## What did NOT change

- Focus policy (still h1 on terminal failure, no-move on success).
- Cancel-as-autofocus pattern.
- Polling cadence / cap.
- DOM structure visible to sighted users.
- Any production-facing markup outside the test-hook seams.

## Bundle size impact

~12 LoC added per component, sr-only span has zero render cost.
No new dependencies. Bundle budget unchanged (≤4KB gz).

## Forbidden-patterns interlock

The 10 anti-patterns enumerated in `focus-and-live-region-policy.md` remain
in lockstep with QA's `FORBIDDEN_PATTERNS` array. No additions, no removals.

## What's still owed

- **Cancel modal (WI-CANCEL-4):** When experience-design ships the cancel
  modal, it must include the same three test hooks
  (`live-region-status` / `live-region-error` / `poll-state`) so QA can
  uncomment the `cancel-modal` entry in `pagesUnderTest`. This is now a
  contract requirement for WI-CANCEL-4 — adding to the design spec when
  cancel v1 reaches design phase (post-SPM, per dispatch order).

- **`usePollingResource.ts`:** No change needed. The hook returns `state`
  with discriminated kinds; consumers derive `data-state` from `state.kind`
  in their JSX (pattern shown above). Pushing the testid into the hook
  itself would require the hook to render, which violates its
  data-only contract.

## Apply path

EMU-blocked — maintainer applies same way as prior bundles. Three files
touched, all under `squads/experience-design/artifacts/`.
