# RM-006 Smoke Matrix — APPLY

**Status:** ⏳ AWAITING SOURCE — QT commit `4e9bd46` on `qa/remember-me-contract` is not yet visible on `origin/qa/remember-me-contract` (top is `66f4e98`). EMU blocks QT's direct push from their session. This APPLY doc reserves the slot; populate `smoke-matrix.spec.ts` once QT (or a non-EMU keyring) pushes 4e9bd46.

## Target
- **Repo:** `tamirdresher/travel-assistant`
- **Branch:** `feature/remember-me` (PR #30)
- **Path:** `tests/e2e/remember-me/smoke-matrix.spec.ts` (~10.6 KB, single file)
- **Source commit:** `bradygaster/squad-with-aspire@4e9bd46` on `qa/remember-me-contract` (NOT YET PUSHED)
- **Parent in stack:** sits on top of QT contract package commit `656a208`

## Maintainer one-shot (once 4e9bd46 lands on remote)

```powershell
# From travel-assistant clone, on feature/remember-me
$tok = gh auth token --user tamirdresher
git fetch https://x-access-token:$tok@github.com/bradygaster/squad-with-aspire.git qa/remember-me-contract
git checkout FETCH_HEAD -- tests/e2e/remember-me/smoke-matrix.spec.ts
git add tests/e2e/remember-me/smoke-matrix.spec.ts
git commit -m "test(auth): RM-006 smoke matrix R1-R10 (cherry-pick of squad-with-aspire@4e9bd46)" `
           -m "Co-authored-by: quality-testing-squad <qa@squad>"
git push "https://x-access-token:$tok@github.com/tamirdresher/travel-assistant.git" feature/remember-me
```

## Contract assertions (from QT message)
Each row is one test (or parameterized) so a PR-checks failure pinpoints the wireframe row violated. Env-gated on `RM_E2E_BASE_URL`. Network-stubs `**/api/auth/login` (no backend dependency).

| Row | Asserts |
|-----|---------|
| R1-R4 | Selector `[data-testid="login-remember-me"]`, default unchecked, label "Keep me signed in", microcopy via `aria-describedby` |
| R5 | Submit aria-busy floor 300ms (auth-503 contract — intentional minimum) |
| R6 | POST `/api/auth/login` body carries `rememberMe: boolean` |
| R7 | **Negative:** ONLY `auth.login.submitted` event fires with rememberMe in payload — no `auth.rememberme.toggled` or any other `/remember.?me/i` event |
| R8 | Choice key `ta:auth:remember:v1` persists (cross-ref `login.spec.ts`) |
| R9 | Cookie `ta_rt` carries `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/api/auth` (asserted via `Set-Cookie` header, NOT `document.cookie` — HttpOnly invariant) |
| R10 | Logout clears `ta_rt` cookie but preserves `ta:auth:remember:v1` localStorage |

## Gate wiring
- **No `remember-me-gate.yml` edit needed.** The `build-and-test` job already globs `tests/**/*.spec.ts` via Playwright config.
- File lands in scope of existing `node 20+22` matrix.
- Picked up automatically once committed to `feature/remember-me`.

## Note on R7 negative assertion (forward-looks)
If any future squad ships an `auth.rememberme.toggled` (or similar) telemetry event, this test catches it. Aligns with XD/sec-hard "derived metric only" position — single source of truth = `auth.login.submitted.payload.rememberMe`. Approved follow-up #1 (browser-OTel wiring) MUST keep this contract.

## MERGE-RUNBOOK update
Add to integration ledger after sec-hard `5acbdd8` row (also pending push):

```
| RM-006 smoke matrix | squad-with-aspire@4e9bd46 | qa/remember-me-contract | tests/e2e/remember-me/smoke-matrix.spec.ts | ⏳ awaiting EMU-bypass push |
```
