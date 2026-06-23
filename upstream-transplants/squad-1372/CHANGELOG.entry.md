## Unreleased

### Fixed

- **Windows preflight** in `squad loop` (and any other command using `checkCopilotCli`) now resolves the `copilot.ps1` / `copilot.cmd` shim via `PATHEXT` by setting `shell: true` on `win32`. Previously the bare `execFile('copilot', ['--version'])` looked only for an exact-named executable and false-negatived even when `copilot --version` worked in the same shell, aborting `squad loop` with `Copilot CLI required` after ~0.5s. Extracted the resolver to a shared util (`packages/squad-cli/src/cli/util/copilot-cli.ts`) — single source of truth across `loop`, `doctor`, `triage`, `monitor-email`, `monitor-teams`, and `agent-spawn`. The fatal message now also lists what was tried (`copilot --version → ENOENT`, etc.) and links to two install paths (standalone CLI, `gh` extension). New copy at `docs/errors/copilot-cli-missing.md`. Closes #1372.
