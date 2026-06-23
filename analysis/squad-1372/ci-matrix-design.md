# CI Matrix Design — bradygaster/squad #1372

**Owner:** azure-infrastructure-squad / CI-matrix subagent
**Status:** Ready to merge
**Scope:** Lock in regression coverage for the Windows `shell:true` shim across all OS/shell/arch combinations that ship `pwsh` or `powershell`.

## Goal

The literal #1372 regression (`spawn copilot ENOENT`) only reproduces on Windows when `child_process.execFile` is invoked without `shell: true`. Today's CI exercises `ubuntu-latest` only, so a future refactor could drop `shell: true` and pass every existing test. This matrix closes that gap with the minimum number of jobs needed to catch:

1. `cmd.exe`-based resolution (default on `windows-2019` / `windows-2022`)
2. Windows PowerShell 5.1 (ships in-box, still default on `windows-2019`)
3. PowerShell 7.x (`pwsh`, default on `windows-2022`, what most contributors run locally)
4. Windows on ARM64 (`windows-11-arm`, GA on hosted runners as of 2025-04) — different `pwsh` binary path; catches hard-coded `C:\Program Files\PowerShell\7\pwsh.exe`-style assumptions.

## Matrix

| Job key | runner | shell | PowerShell | Node | Why |
|---|---|---|---|---|---|
| `win2019-ps51` | `windows-2019` | `powershell` | 5.1 (in-box) | 20 | Legacy estate; default shell still WPS |
| `win2019-pwsh7` | `windows-2019` | `pwsh` | 7.4.x | 20 | Mixed estate; pwsh installed alongside |
| `win2022-pwsh7` | `windows-2022` | `pwsh` | 7.4.x | 20 | Default modern Windows runner |
| `win2022-cmd` | `windows-2022` | `cmd` | n/a | 20 | Catches `comspec` / `cmd.exe` path resolution |
| `winarm-pwsh7` | `windows-11-arm` | `pwsh` | 7.4.x (arm64) | 20 | ARM64-specific binary paths |
| `ubuntu-latest` | `ubuntu-latest` | `bash` | n/a | 20 | Baseline; ensures `shell: true` is **not** set on non-Windows |

Total: 6 jobs. fail-fast disabled — we want all matrix cells to report independently so a single-cell failure doesn't mask others.

## What each job runs

1. **Install `gh` + `gh-copilot` extension** (skipped on `winarm` until extension publishes arm64; tracked as known-fail allowed-failure cell).
2. **Provision a stub `copilot` shim** — a no-op script that prints `copilot 0.0.0-test` so detection succeeds without a real Copilot license. Lives at `tests/fixtures/copilot-shim/` and is added to `PATH` by the workflow.
3. **Run the four #1372 test files**:
   - `tests/regression/copilot-cli-windows-shim.test.ts`
   - `tests/regression/spawn-agent-shell-injection.test.ts`
   - `tests/regression/copilot-cli-missing-message.test.ts`
   - `tests/regression/copilot-cli-shared-util.test.ts`
4. **Smoke-run `copilot --version`** end-to-end through the actual squad CLI entry-point. This is the canary that would have caught the original ENOENT.

## Non-goals

- We do **not** install a real Copilot license in CI. The shim covers code-path coverage; license-gated paths are out of scope for regression CI.
- We do **not** matrix on Node versions. The shim bug is platform/shell-bound, not Node-bound. A separate `node-versions.yml` already covers Node matrix for unit tests.

## Cost / runtime budget

- Estimated wall-clock: 6-8 min per Windows cell (cold runner, `npm ci`), 3 min for ubuntu.
- Estimated GitHub Actions minutes per push to `main`: ~40 Windows-min + ~3 Linux-min. Windows minutes bill 2× → ~83 billable-min. Acceptable for a release-blocking regression.
- Triggers: `push` to `main`, `pull_request` touching `packages/squad-cli/**` or `tests/regression/**`, plus weekly `schedule` to catch runner-image drift.

## Rollout

1. Land workflow as **non-required** check first (one full green run on `main`).
2. After 7 days of green, promote to required status check on PRs touching the paths above.
3. Add the workflow badge to README under "CI Status".

## Coordination

- **quality-testing-squad** owns the four test files; this workflow only wires them up.
- **application-development-squad** owns the shim resolver; CI just exercises it.
- **review-deployment-squad** owns the EMU/upstream-transplant bundle; this workflow needs to be transplanted alongside the test files.
