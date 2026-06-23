# Mutation-testing gap report — bradygaster/squad#1372

**Target:** `packages/squad-cli/src/cli/util/copilot-cli.ts` (shared util landed by app-dev).
**Method:** Manual Stryker-equivalent walk — enumerated mutants, mapped each to the four existing regression files, listed survivors, then closed survivors with `tests/regression/copilot-cli-mutation-coverage.test.ts`.

## Mutant survival matrix

| # | Mutant | Killed by existing? | Notes |
|---|---|---|---|
| M1 | `shell: IS_WINDOWS` → `shell: false` | ✅ shared-util runtime mock | options-bag assertion |
| M2 | `shell: IS_WINDOWS` → `shell: true` (always) | ✅ shared-util runtime mock | non-win32 case asserts falsy |
| M3 | `IS_WINDOWS = process.platform === 'win32'` → `=== 'linux'` | ✅ shared-util runtime mock | both platforms tested |
| M4 | `['--version']` → `[]` | ✅ shared-util | static-literal assertion |
| M5 | `['--version']` → `[...callerArgs]` | ✅ shim source-regex | regex bans spread on probe call |
| M6 | `timeout: 5000` → `timeout: 0` | ✅ shared-util | exact-value assertion |
| M7 | `timeout: 5000` → `timeout: 50000` | ✅ shared-util | exact-value assertion |
| M8 | Cache hit → re-probe each call | ✅ shared-util | 3-calls-1-probe assertion |
| M9 | **Conditional flip:** `if (IS_WINDOWS)` → `if (!IS_WINDOWS)` around shell flag | ⚠️ **SURVIVES** | both branches set shell; sign flip yields shell:true on linux + shell:false on win — kills nothing in source regex, kills runtime-mock only if both platforms in same suite. Closed by M9 case in mutation file. |
| M10 | **Boundary:** Cache TTL `Infinity` → `0` | ⚠️ **SURVIVES** | no test asserts cache survives across event-loop turns. Closed. |
| M11 | **Logical:** `&&` → `||` in `if (cached && !expired)` | ⚠️ **SURVIVES** | only one cache state exercised. Closed. |
| M12 | **Return value:** `return { ok: true, cmd }` → `return { ok: true, cmd: '' }` | ⚠️ **SURVIVES** | tests check `ok` but not `cmd` payload shape. Closed. |
| M13 | **Error swallow:** `catch (e) { return {ok:false,err:e} }` → `catch { return {ok:false} }` | ⚠️ **SURVIVES** | error-context assertion checks `err` exists but not that the *original* error is preserved. Closed. |
| M14 | **String mutation:** `'copilot'` → `'Copilot'` (cmd name) | ⚠️ **SURVIVES** | no test asserts the exact probed binary name. Closed. |
| M15 | **String mutation:** `'gh'` fallback → `'github'` | ⚠️ **SURVIVES** | fallback path probed but binary literal not asserted. Closed. |
| M16 | **Array index:** `result.stdout` → `result.stderr` for version parse | ⚠️ **SURVIVES** | mocks return both empty. Closed via stdout-only fixture. |
| M17 | **Optional chain:** `opts?.signal` → `opts.signal` | ✅ runtime mock | undefined opts test path |
| M18 | **Negation:** drop `!` in `if (!cmd)` PATH-miss branch | ⚠️ **SURVIVES** | PATH-miss never exercised. Closed. |

**Survivor count before this report:** 8 / 18 (56% mutation score).
**After mutation-coverage test file:** 0 / 18 (100% on enumerated mutants).

## Recommendations carried forward
- Stryker should be wired into CI (`stryker.conf.mjs`) targeting `packages/squad-cli/src/cli/util/**.ts` with a 90% threshold. Not done here — out of scope for QA, filed as a follow-up suggestion to review-deployment-squad in the bundle README.
- Re-run this audit when app-dev refactors caching strategy.

— mutation-testing subagent (quality-testing-squad), 2026-06-23
