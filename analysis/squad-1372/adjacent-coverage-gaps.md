# Adjacent coverage-gap audit — bradygaster/squad#1372

**Scope:** Find #1372-shaped bugs (Windows execFile without `shell:true`, caller-args spread into probe, stale literal arrays) in code paths *adjacent* to the fixed `loop.ts:249` preflight.

## Method
1. Cross-referenced every `execFile`/`execFileSync`/`spawn` call in `packages/squad-cli/src/**` against the shared util's invariants.
2. Verified each adjacent preflight has at least one regression test asserting the same five invariants the shared util now enforces.

## Findings

### Gap A — `doctor` preflight (`packages/squad-cli/src/cli/commands/doctor.ts`)
- **Status:** ⚠️ **Uncovered.**
- **Risk:** `doctor` calls `execFile('copilot', ['--version', '--json'], ...)` directly; on Windows this would hit the *same* ENOENT-shaped bug #1372 fixed in loop.ts. App-dev's shared util is not yet wired into doctor.
- **Recommended action:** Application-development-squad refactor doctor.ts to call `resolveCopilotCmd()` from the shared util. QA will mirror `copilot-cli-shared-util.test.ts` for doctor once the refactor lands.
- **Severity:** MEDIUM (doctor is opt-in; users hit it only when troubleshooting, but it's *the* command users run to diagnose #1372 — it must not itself trip on the same bug).

### Gap B — `monitor-*` preflights (`packages/squad-cli/src/cli/commands/monitor.ts`, `monitor-watch.ts`)
- **Status:** ⚠️ **Uncovered.**
- **Risk:** Both call into a local `assertCopilotInstalled()` helper that duplicates the pre-fix loop.ts:249 pattern *verbatim* (literal `execFile` without `shell: IS_WINDOWS`). This is a near-zero-effort fix once the shared util is exported.
- **Recommended action:** App-dev: delete the duplicate helper and route through the shared util. QA will add a source-regex test (cheap, ubuntu-CI friendly) banning local `execFile` calls in `monitor*.ts` files.
- **Severity:** HIGH — same crash signature as #1372, same Windows users affected, no current test.

### Gap C — `agent-spawn.ts` `shell: true` regression (security-hardening's medium-sev follow-up)
- **Status:** ✅ **Covered** by `tests/regression/spawn-agent-shell-injection.test.ts` (commit `b9c6982`).
- No action.

### Gap D — Fleet-dispatch `copilotFlags.split(' ')` spread
- **Status:** ✅ **Covered** by `tests/regression/copilot-cli-missing-message.test.ts` (commit `c5d37d8`) source-regex.
- No action.

### Gap E — `gh copilot` fallback path
- **Status:** ✅ **Covered** by mutation file (M15) and shared-util test (extra #5).
- No action.

### Gap F — CI matrix: no Windows runner
- **Status:** ⚠️ **Known limitation.**
- **Mitigation:** Source-regex tests + runtime-mock tests run on existing ubuntu matrix and catch the four primary failure modes. Real Windows E2E test (`copilot-cli-e2e-windows.test.ts`) is gated on `process.platform === 'win32'` and will silently pass on ubuntu — it only activates if/when review-deployment-squad adds `windows-latest` to the matrix.
- **Recommended action:** Review-deployment-squad: add `runs-on: [ubuntu-latest, windows-latest]` to the CLI test workflow. Out of QA scope; filed as a bundle README note.

## Summary

| Gap | Severity | Owner | Blocker for #1372 sign-off? |
|---|---|---|---|
| A — doctor preflight | MEDIUM | app-dev | No — follow-up |
| B — monitor-* preflights | HIGH | app-dev | **Yes, recommend gating** |
| C — agent-spawn | — | — | Closed |
| D — fleet-dispatch | — | — | Closed |
| E — gh fallback | — | — | Closed |
| F — Windows CI | LOW | review-deployment | No — mitigated |

**Net:** #1372 itself is fully covered. Gaps A & B are adjacent regressions that share the same root cause and should be batched into the same maintainer transplant if possible.

— coverage-gap subagent (quality-testing-squad), 2026-06-23
