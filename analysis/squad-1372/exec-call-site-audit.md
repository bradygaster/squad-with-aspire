# #1372 — exec/spawn call-site audit

**Scope:** every `execFile` / `execFileSync` / `spawn` / `spawnSync` call site in `packages/squad-cli/` that invokes a binary which may resolve to a Windows shim (`.cmd` / `.bat` / `.ps1`). The class of bug is: Node's `child_process` family without `shell: true` cannot resolve cmd/bat/ps1 shims on Windows, so `copilot` (installed as `copilot.cmd` via npm), `gh` extensions, and similar wrappers raise `ENOENT` or `spawn EINVAL`.

**Authoritative shim-resolution rules:** see `windows-shim-rules.md`.
**Resolver contract used below:** `resolveCopilotCmd()` from `cli/commands/watch/agent-spawn.ts` (post-#1372: re-exported from `cli/util/copilot-cli.ts`).

## Summary table

| # | File:line | Binary | Has `shell` opt? | Uses `resolveCopilotCmd`? | Risk | Status |
|---|-----------|--------|------------------|---------------------------|------|--------|
| 1 | `cli/commands/loop.ts:251` | `copilot` (preflight `--version`) | ❌ no | ❌ | **HIGH** | #1372 patchset replaces with `resolveCopilotCmd()` |
| 2 | `cli/commands/loop.ts:394` | `cmd` from `buildLoopAgentCommand()` (may be `copilot`) | ❌ no | partial (loop builds its own cmd at line 146) | **HIGH** | #1372 patchset addresses by routing through resolver; spawn still needs `shell: IS_WINDOWS` |
| 3 | `cli/commands/watch/agent-spawn.ts:38` | `copilot --version` | ✅ `shell: IS_WINDOWS` | n/a (this **is** the resolver) | none | fixed (post-#1372) |
| 4 | `cli/commands/watch/capabilities/monitor-email.ts:43` | `copilot --version` | ✅ `shell: true` | ❌ | LOW | fixed in #920; switch to `resolveCopilotCmd()` post-#1372 to stop hard-coding `'copilot'` |
| 5 | `cli/commands/watch/capabilities/monitor-teams.ts:44` | `copilot --version` | ✅ `shell: true` | ❌ | LOW | same as #4 |
| 6 | `cli/commands/doctor.ts:448` | `copilot --version` | ✅ `shell: true` | ❌ | LOW | fixed in #920; #1372 patchset switches to the v2 renderer + shared resolver |
| 7 | `cli/commands/copilot-bridge.ts:36` | `copilot --acp --stdio` (probe) | ❌ no | ❌ | **HIGH** | not in #1372 scope — **new finding**, file follow-up |
| 8 | `cli/commands/copilot-bridge.ts:84` | `copilot` (long-running ACP bridge) | ❌ no | ❌ | **HIGH** | not in #1372 scope — **new finding**, file follow-up |
| 9 | `cli/commands/watch/capabilities/fleet-dispatch.ts:75-76` | `copilot.cmd` on win32, else `copilot` | n/a (uses explicit `.cmd` extension) | ❌ | MEDIUM | works today, but #1372 patchset swaps to `resolveCopilotCmd()` for consistency and to handle `gh copilot` fallback |
| 10 | `cli/commands/start.ts:194` | `copilotCmd` via `node-pty.spawn` | n/a (node-pty resolves via cmd.exe on win32) | partial (resolves `copilotExePath` from `%ProgramData%` first) | LOW | `node-pty` on Windows uses `winpty`/`conpty` which delegates to a shell — shim resolution is safe. No change. |
| 11 | `cli/commands/aspire.ts:68` | `docker` | ❌ no | n/a | LOW | `docker` ships as `docker.exe` on Windows — not a shim. No change. |
| 12 | `cli/commands/aspire.ts:85` | `dotnet` | ❌ no | n/a | LOW | same — `dotnet.exe`. No change. |
| 13 | `cli/templates/capabilities/issue-pipeline.sample.js:20` | (template — emits user code) | n/a | n/a | none | sample only; documents pattern. No change. |

### Squad-SDK call sites (out-of-scope but verified clean)

Audited `packages/squad-sdk/` for any `execFile`/`spawn` of `copilot`. **None found.** All SDK shell-outs target `git`, `gh`, `az`, `devtunnel`, `curl`, `icacls`, `powershell.exe`, `open`, `xdg-open` — all `.exe`/binary entrypoints on Windows, no shim risk. SDK is dependency-safe for #1372.

## Risk-rated findings (priorities for follow-up issues)

### P0 — fixes already in #1372 patchset

- **loop.ts:251 / :394** — exactly the bug #1372 reports. Patchset routes both through `resolveCopilotCmd()` + `shell: IS_WINDOWS`. **Land #1372.**

### P1 — new findings, file follow-up issues after #1372 lands

- **copilot-bridge.ts:36, :84** — `spawn('copilot', ...)` with no `shell` option. Same Windows shim class as loop.ts. The ACP bridge path is gated by a probe (`spawn('copilot', ['--acp', '--stdio'])`); on Windows the probe itself will throw `ENOENT` before the bridge can attach, masking the real failure as "ACP not available". Suggested fix: import `resolveCopilotCmd()` from the new `cli/util/copilot-cli.ts` and use `{ cmd, args: [...cmdPrefix, ...rest], shell: IS_WINDOWS }` in both spawns. **Owner: app-dev. Recommended: open as #1372-followup with title "copilot-bridge: ACP probe and spawn need Windows shim handling".**

### P2 — consistency cleanup

- **monitor-email.ts:43, monitor-teams.ts:44, doctor.ts:448, fleet-dispatch.ts:75-76** — all currently work on Windows (either `shell:true` or explicit `.cmd`), but each hard-codes `'copilot'` and silently skips the `gh copilot` legacy fallback that `resolveCopilotCmd()` provides. #1372 patchset already migrates doctor + fleet-dispatch + agent-spawn. Recommend monitor-email and monitor-teams adopt the same resolver in a follow-up so that users with only `gh copilot` (no standalone `copilot`) still get watch capabilities.

### P3 — clean, no action

- **start.ts:194** (node-pty), **aspire.ts:68/:85** (docker/dotnet are .exe), **agent-spawn.ts:38** (already fixed), **issue-pipeline.sample.js** (template).

## Invariants to enforce going forward

1. Any new call site that runs `copilot` MUST go through `resolveCopilotCmd()` from `cli/util/copilot-cli.ts`. Direct `'copilot'` literals in `spawn`/`execFile` are forbidden.
2. Any spawn of a binary that may resolve to a `.cmd`/`.bat`/`.ps1` shim on Windows MUST pass `shell: IS_WINDOWS` (or `shell: true`). Lint rule candidate: ESLint custom rule `no-shim-spawn-without-shell` (deferred — file as quality-testing follow-up).
3. `process.platform === 'win32'` checks scattered across files should consolidate on the `IS_WINDOWS` constant exported from `cli/util/copilot-cli.ts`.
