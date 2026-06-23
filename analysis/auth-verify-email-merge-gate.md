# Merge Gate — auth verify-email reducer + wiring

**Owner:** review-deployment-squad
**Target upstream PR:** wiring layer for `verifyEmailReducer.resendAttempted` (app-dev)
**Reducer commit (already on `main`):** `3fdef48`
**UX wiring spec:** `7909ec4` (`bradygaster/squad-with-aspire@main`)

This file is the single source of truth review-deployment-squad will check
before squash-merging the upstream wiring PR. App-dev should read it before
opening the PR; QT, XD, and security should reference it when signing off.

---

## 1. Required CI checks (must be green)

| Check | Workflow | What it proves |
|---|---|---|
| `purity-tests (node 20)` | `auth-ui-contracts-gate.yml` | 7 reducer purity tests pass on node 20 |
| `purity-tests (node 22)` | `auth-ui-contracts-gate.yml` | same, on node 22 |
| `invariant-grep` | `auth-ui-contracts-gate.yml` | `resendAttempted` flag + `verifyEmailReducer` export survive |
| `wiring-contract-doc` | `auth-ui-contracts-gate.yml` | reducer edits ship with test or CHANGELOG |
| `gate` | `auth-ui-contracts-gate.yml` | aggregate ✅ |
| `CI — Build & Test` | `ci.yml` | broader repo build still green |
| `security-static` | `security-static.yml` | semgrep + CodeQL + spawn-audit clean |

`gate` is the **required status check** on `main` branch protection. The
other auth-ui-contracts jobs are surfaced through it via `needs:`.

---

## 2. Reducer-layer invariants (locked by `3fdef48`)

1. `VerifyEmailState` is intersected with `{ resendAttempted?: boolean }` so
   every state shape carries the flag without breaking the discriminated
   union for downstream consumers.
2. First `resendStart` action ⇒ `resendPending` with `resendAttempted: true`.
3. Any subsequent `resendStart` while `resendAttempted` is already truthy ⇒
   **same object reference** is returned. Referential equality is the
   contract the wiring layer relies on so `useReducer` short-circuits
   re-renders.
4. `resendResult` (202) transitions preserve `resendAttempted`.
5. Reducer never mutates input state (purity invariant).
6. StrictMode double-mount → exactly **1** POST attempt at the reducer layer.

If any of these change, the PR description must include a
`BREAKING-CHANGE:` trailer and update `tests/regression/auth-verify-email-states.test.ts`
in the same commit.

---

## 3. Wiring-layer obligations (app-dev PR scope)

The reducer is the idempotency lock. The wiring PR **must**:

- [ ] Call `dispatch({ type: 'resendStart' })` from both the auto-mount
      effect and the manual onClick handler. **Do not** add a ref-based
      lock — the reducer enforces it.
- [ ] Use `[token]` as the effect dependency array (not `[]` — token
      changes must re-arm auto-resend).
- [ ] Emit telemetry pair on auto path:
      `auth.verify.auto_resend_fired` (when effect dispatches) +
      `auth.verify.auto_resend_result` (on `resendResult` action).
- [ ] Announce `verifyPending → resendPending(auto)` transition to the
      live region (per XD §1 item 5).
- [ ] No network call made directly from the wiring layer when the
      reducer returns the same-ref no-op. Verified via mock POST counter
      in the new wiring test (≥1 case from XD §7).

**Tests app-dev must add** (DOM harness, jsdom-based — XD §7 5-case
matrix):
- [ ] manual-only, auto disabled
- [ ] auto-only, no manual click
- [ ] StrictMode mount → exactly 1 fetch
- [ ] manual-then-auto → exactly 1 fetch
- [ ] auto-then-manual → exactly 1 fetch

---

## 4. Sign-off ledger (squash gate)

Each squad signs off via PR review approval. Review-deployment-squad will
not squash until **all six** are recorded:

| Squad | Signs off on | Required |
|---|---|---|
| ideation-research-planning | scope unchanged from `7909ec4` spec | ✅ |
| experience-design | wiring matches §1 items 4–7, live-region wired | ✅ |
| application-development | PR author — wiring + DOM tests committed | ✅ |
| quality-testing | DOM harness 5/5 green, reducer tests 7/7 still green | ✅ |
| security-hardening | no new spawn/exec surface; token not logged | ✅ |
| azure-infrastructure | n/a (no infra change) | ⏭️ |

---

## 5. Merge mechanics

- **Strategy:** squash
- **Subject line:** `feat(auth-ui): wire verify-email auto-resend with reducer-level idempotency`
- **Trailer:** `Refs: 3fdef48, 7909ec4`
- **Squad-bot trailer:** standard
- **Branch protection:** required check = `gate`, plus `CI — Build & Test`
  and `security-static`.

---

## 6. Post-merge

1. Tag patch release (no separate workflow needed — rides next `cd.yml`
   trigger).
2. Update `analysis/auth-verify-email/` with merged-SHA pointer (if dir
   exists; else skip).
3. Close any related issues with merge-commit link.

---

## 7. EMU note

This gate lives in `bradygaster/squad-with-aspire`. If the wiring work
also needs to land in `bradygaster/squad` (private upstream), the same
gate spec applies — review-deployment-squad will mirror this workflow
under `upstream-transplants/auth-verify-email/` when app-dev's patchset
is ready, same pattern as `upstream-transplants/squad-1372/`.
