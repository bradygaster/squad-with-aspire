# #1372 — Windows shim resolution rules

Reference doc for every author touching `child_process` spawns of binaries that may resolve to a Windows shim. The contract here is what `resolveCopilotCmd()` and every caller in #1372 patchset relies on.

## The core rule

> On Windows, Node's `child_process` functions (`spawn`, `execFile`, and their `*Sync` siblings) **without `shell: true`** invoke `CreateProcess` directly. `CreateProcess` only executes binaries Windows recognizes as executable images — namely `.exe`, `.com`, and a small set of bundled types. It does **not** consult `PATHEXT`, does **not** run `.cmd`/`.bat`/`.ps1` scripts, and does **not** follow npm's wrapper scripts.

This is why `spawn('copilot', [...])` raises `ENOENT` on a Windows machine where `copilot` is installed as `C:\Users\<u>\AppData\Roaming\npm\copilot.cmd` — the wrapper is invisible to `CreateProcess`.

Setting `shell: true` makes Node route the spawn through `%ComSpec%` (usually `cmd.exe /d /s /c`), which **does** honor `PATHEXT` and run shims. The cost: arguments are concatenated into a single command line and re-parsed by the shell, so any unsanitized argument becomes a shell injection vector.

## PATHEXT order — what wins when multiple shims share a name

Windows resolves an unqualified command name by walking `PATH` entries left-to-right; within each entry, it walks `PATHEXT` left-to-right. The default `PATHEXT` is:

```
.COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC
```

(On modern systems `.PY` and `.PYW` may be appended by Python installers; `.PS1` is **not** in the default and PowerShell scripts are not executed by `CreateProcess` or `cmd.exe` via name resolution alone — see §"PowerShell scripts" below.)

**Consequences for `copilot`:**

- npm global install on Windows lays down `copilot` (a shell script for WSL/Git Bash), `copilot.cmd` (the cmd.exe wrapper), and `copilot.ps1` (PowerShell wrapper) in `%AppData%\npm\`.
- Under `cmd.exe` shell resolution, `copilot.cmd` wins (`.CMD` precedes `.PS1` in PATHEXT, and `.PS1` is absent by default anyway).
- The extensionless `copilot` script is **only** consumed by Git Bash / MSYS / WSL — `cmd.exe` and PowerShell ignore it entirely.

## Precedence table — what `resolveCopilotCmd()` expects

| Installed artifacts | `spawn('copilot', …, { shell: IS_WINDOWS })` resolves to | `spawn('copilot', …)` (no shell) resolves to |
|---|---|---|
| Only `copilot.exe` on PATH | `copilot.exe` | `copilot.exe` ✓ |
| Only `copilot.cmd` on PATH (typical npm install) | `copilot.cmd` ✓ | `ENOENT` ✗ |
| Only `copilot.ps1` on PATH | `ENOENT` (PowerShell scripts not in default PATHEXT) | `ENOENT` ✗ |
| `copilot.exe` + `copilot.cmd` in same dir | `copilot.exe` (`.EXE` before `.CMD`) | `copilot.exe` ✓ |
| `copilot.exe` in dir A, `copilot.cmd` in dir A (A first on PATH) | `copilot.exe` | `copilot.exe` |
| `copilot.cmd` in dir A, `copilot.exe` in dir B (A before B on PATH) | `copilot.cmd` (PATH wins before PATHEXT) | `copilot.exe` (CreateProcess skips dir A entirely — only sees `.exe`) |

The last row is the subtle one and the reason this audit exists: PATH order is honored before PATHEXT, so two machines with the same `PATHEXT` but different `PATH` ordering can disagree on which `copilot` they run. **Any code that needs reproducible behavior must either pin via `--agent-cmd` (absolute path) or accept whatever `cmd.exe` picks.**

## PowerShell scripts (`.ps1`)

- `.PS1` is **not** in default `PATHEXT`. Users who add it can still spawn PowerShell scripts via `cmd.exe`, but Windows refuses to execute them as a process image — `cmd.exe` recognizes the extension and invokes `pwsh.exe` / `powershell.exe -File` under the hood.
- Node's `spawn` with `shell: true` therefore handles `.ps1` only if the user opted in by adding it to `PATHEXT`. Treat `.ps1` resolution as **non-portable**. Do not rely on it.
- Node with `shell: 'pwsh.exe'` or `shell: 'powershell.exe'` will run `.ps1` reliably but changes argument quoting rules entirely — out of scope for #1372.

## WSL and Git Bash edge cases

- **WSL:** running `wsl.exe copilot` will execute the Linux `copilot` binary from the WSL distro's PATH, not the Windows install. Authors invoking `copilot` from a Node process **running in Windows** never cross into WSL automatically.
- **Git Bash / MSYS2:** their shell is bash, so `shell: true` from Node resolves to `cmd.exe`, **not** Git Bash. The extensionless `copilot` script that npm installs alongside `copilot.cmd` is never consulted by Node — that script is only used when a human runs `copilot` in a Git Bash prompt.
- **Conclusion:** Node + Windows is *always* a `cmd.exe` shell regardless of which terminal the user launched from. `IS_WINDOWS` is the only branch needed.

## `node-pty` is a special case

`node-pty.spawn(cmd, args, opts)` on Windows uses `winpty-agent.exe` or ConPTY, which delegate to `cmd.exe` for command resolution. Shims work without an explicit `shell` flag. This is why `cli/commands/start.ts:194` does **not** need a Windows-specific code path (verified — see `exec-call-site-audit.md` row 10).

## Argument-quoting hazard with `shell: true`

When `shell: IS_WINDOWS` is set, Node passes the full command line as a single string to `cmd.exe`. `cmd.exe` argument parsing differs from CreateProcess:

- Backslashes are NOT escape chars
- `^` is the escape char
- `&`, `|`, `<`, `>`, `(`, `)`, `%`, `!` all need escaping if literal
- Double quotes group args, single quotes do **not**
- `%VAR%` expands at parse time — user-controlled args containing `%` open env-var-leak attacks

**Mitigation in #1372 patchset:** `resolveCopilotCmd()` is only used for `--version` probes (no user input) and for the agent invocation where the `prompt` is passed via stdin or a temp file, never as an argv element on a `shell: true` invocation. The fleet-dispatch.ts site uses `execFileSync` with explicit `.cmd` rather than `shell: true`, sidestepping the quoting problem entirely.

**Rule of thumb:** if you set `shell: true`, every element of the `args` array must come from a fixed allowlist or be path-validated by `path.resolve` + existence check. Never interpolate raw user strings.

## What `resolveCopilotCmd()` does and does NOT promise

- ✓ Promises: returns a `{cmd, cmdPrefix}` (v1) or `CopilotCliDetection` (v2) that, when invoked with `shell: IS_WINDOWS` on Windows or `shell: false` elsewhere, will reach the Copilot CLI when it is installed under the supported channels (standalone `copilot` on PATH; `gh copilot` extension).
- ✗ Does NOT promise: any specific version, any specific install location, that the binary is digitally signed, or that the resolution survives a `PATH` change made after first call (the result is cached for the process lifetime).
- ✗ Does NOT auto-elevate, install, or modify the user's PATH.

## Test fixtures (handed to quality-testing-squad)

Required cases in `copilot-cli.test.ts`:

1. PATH contains dir with `copilot.cmd` only, `IS_WINDOWS=true` → `kind:'copilot'`, `cmd:'copilot'`.
2. PATH contains dir with `gh.exe` only, no standalone → `kind:'gh-copilot'`.
3. Empty PATH → `kind:'missing'`, `tried.length === 2`.
4. PATH contains `copilot.exe` AND `copilot.cmd` in same dir → resolves to `.exe` per PATHEXT order.
5. PATH contains `copilot.cmd` in dir A, `copilot.exe` in dir B with A first → on Windows with `shell:true`, `.cmd` wins; without shell, `.exe` wins (CreateProcess skips A).
6. Caching: two consecutive calls produce only the probes from call 1 (use mock counter).
7. `_resetCopilotDetection()` clears cache.

Cases 1–4, 6, 7 are covered by the #1372 patchset's `copilot-cli.test.ts`. Case 5 needs a Windows-only harness (skip on non-win32) and is recommended for the v2 follow-up PR.
