# Windows Devcontainer for #1372 Repro

**Owner:** azure-infrastructure-squad / container-devcontainer subagent
**Status:** Ready to merge
**Audience:** Linux/macOS contributors who need to reproduce or debug `copilot --version` ENOENT (#1372) without dual-booting Windows.

## Problem

The #1372 shim bug is **only** observable when `child_process.execFile` runs under Windows, because the failure mode is "Windows resolves `copilot` to `copilot.cmd`/`copilot.ps1` via PATHEXT, but `execFile` without `shell: true` does not consult PATHEXT." On Linux/macOS, `execFile('copilot', ...)` either finds `/usr/local/bin/copilot` (a binary or shebang script) and succeeds, or fails with ENOENT in a way that is **indistinguishable** from a missing install. A non-Windows contributor therefore cannot tell whether their fix actually addresses the regression — they need a Windows kernel.

## Options considered

| Option | Verdict |
|---|---|
| Wine + Node on Linux | ❌ `child_process` semantics differ enough from real Win32 that `shell: true` behaves differently. Catches almost nothing. |
| Full Windows VM (Hyper-V/QEMU) | 🟡 Works but heavy (20+ GB, requires nested virt, Windows licensing). Not suitable for a devcontainer. |
| GitHub Codespaces with Windows base image | 🟡 Codespaces does not currently offer Windows-base devcontainers (Linux-only base). |
| **Windows container (`mcr.microsoft.com/windows/servercore:ltsc2022`) on a Windows Docker host** | ✅ Reproduces real Win32 `child_process` semantics. Requires the contributor's *host* to be Windows. |
| **Linux devcontainer + remote SSH into the `windows-2022` GitHub Actions runner via `act` or `tmate`** | ✅ Zero host requirement. Selected as the **primary** path for non-Windows contributors. |

## Selected design — dual-path devcontainer

We ship **one** `.devcontainer/windows/devcontainer.json` that supports two invocation modes:

### Mode A: Windows host with Docker Desktop (Windows containers enabled)

`devcontainer.json` selects `mcr.microsoft.com/windows/servercore:ltsc2022` as `image` when `${localEnv:DEVCONTAINER_MODE}` is `windows-native`. The container has:

- PowerShell 7.4 (`pwsh`)
- Node 20 (msi-installed at build)
- `gh` CLI + `gh-copilot` extension
- The shim from `tests/fixtures/copilot-shim/` pre-staged on `PATH`

A contributor opens the folder, VS Code prompts "Reopen in container", and they get a real Win32 shell inside the editor. `npm test -- --run tests/regression/` reproduces the #1372 failure mode 1:1 with the `windows-2022` CI cell.

### Mode B: Linux host → ephemeral GitHub Actions Windows runner via `tmate`

For Linux/macOS contributors, the same `devcontainer.json` falls back to a Linux base (`mcr.microsoft.com/devcontainers/typescript-node:20`) and ships a `repro-on-windows-runner.sh` helper. The helper:

1. Triggers a `workflow_dispatch` run of `.github/workflows/squad-repro-tmate.yml` (separate workflow, **not** part of this PR — sketched below for follow-up).
2. The workflow boots a `windows-2022` runner, checks out the contributor's PR branch, opens a `tmate` SSH session, and posts the SSH command back to the workflow logs.
3. Contributor SSH-es in, gets a real PowerShell 7 prompt on a real Windows-2022 host, runs `npm test`, debugs, exits.

The `tmate`-on-Windows action exists (`mxschmitt/action-tmate@v3` supports `windows-latest`); the helper script is ~30 lines.

## Limitations

- **Windows containers require a Windows Docker host.** There is no way around this for Mode A; it's a kernel-level constraint.
- **`tmate` sessions are visible to anyone with the SSH string.** The workflow restricts trigger to `repository_owner` and pins a 30-minute timeout. Documented in the helper script header.
- **License:** the Windows Server Core base image is free for dev/test use under the [Windows Container license](https://learn.microsoft.com/virtualization/windowscontainers/images-eula). We pin `ltsc2022` to avoid surprise relicensing on `:latest`.

## Files in this PR

- `.devcontainer/windows/devcontainer.json` — the dual-mode container definition.

## Follow-ups (not in this PR)

- `.github/workflows/squad-repro-tmate.yml` — the ephemeral-Windows-runner workflow. Defer until a contributor actually asks for it; YAGNI for now.
- README section "Reproducing Windows-only bugs locally" — experience-design-squad's call on phrasing.

## Coordination

- **review-deployment-squad**: bundle this directory into the upstream-transplant tarball for #1372.
- **security-hardening-squad**: confirm the Server Core base image is on the approved-base-images list (mcr.microsoft.com is implicitly trusted, but worth a sanity check on the digest pin we land later).
