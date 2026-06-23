# PATH trust boundary -- implementation design

Subagent: **path-trust-boundary** (security-hardening-squad).
Companion to `path-hijack-risk.md` (the 5-tier strategy doc) and the SECURITY.md
"Trust boundaries -> PATH" section. This file pins the concrete API and the
trusted-install allow-list.

## Resolver contract

`resolveCopilotCmd()` returns `{ cmd: string; trusted: boolean; via: 'cache' | 'where' | 'which' }`.

- `cmd` is **always an absolute path**. Never `"copilot"` and never `"copilot.cmd"`.
- `trusted` is `true` iff `cmd` matches one of the allow-list prefixes below.
- Result is cached in module scope. Cache is invalidated only by process restart.

## Trusted-install allow-list

| Platform | Prefix | Source |
|---|---|---|
| Windows | `%ProgramFiles%\GitHub CLI\` | `gh` MSI installer |
| Windows | `%ProgramFiles%\GitHub Copilot\` | future first-party installer |
| Windows | `%LOCALAPPDATA%\Programs\GitHub Copilot\` | per-user installer |
| Windows | `%APPDATA%\npm\` | `npm i -g` default |
| macOS | `/opt/homebrew/bin/` | brew (arm64) |
| macOS | `/usr/local/bin/` | brew (x64) + npm |
| Linux | `/usr/local/bin/` | npm + manual |
| Linux | `/usr/bin/` | distro packages |
| Linux | `~/.npm-global/bin/`, `~/.local/bin/` | per-user npm |

`%PATH%` entries under the user's writable temp/download dirs are **not** in
the allow-list. A resolver hit outside the allow-list yields `trusted: false`.

## Enforcement matrix

| Caller | `trusted: false` action |
|---|---|
| `loop.ts` preflight | Emit `SQUAD_PATH_WARNING` to stderr. Continue. |
| `doctor` command | Print warning prominently. Exit 0. |
| `agent-spawn.ts` (hot path) | Continue. The actual security control is `shell: false` + argv array, not the allow-list. |
| CI | If `SQUAD_REQUIRE_TRUSTED_PATH=1`, abort with exit 78. |

The allow-list is **detection, not prevention**. A malicious shim earlier on
PATH still wins the resolution race; the allow-list only tells the user that
something is unusual. Real prevention is the absolute-path + `shell:false` pair.

## Why not abort by default

1. The npm install location varies by user setup (nvm, volta, fnm, pnpm).
2. Corporate fleet management installs Copilot to non-standard prefixes.
3. False positives on a security control teach users to disable it.

`SQUAD_REQUIRE_TRUSTED_PATH=1` is opt-in for hardened environments (CI, locked-down
fleets) where every install location is known.

## Test obligations

- Unit: allow-list matcher honors `%ENV%` expansion on Windows.
- Unit: `~` expansion on POSIX.
- Integration: drop a fake `copilot.exe` in `%TEMP%`, prepend to `PATH`, assert
  resolver returns `trusted: false` with `via: 'where'`.
- Integration: with the legitimate npm install, assert `trusted: true`.

QT-squad owns wiring these into the existing `copilot-cli-shared-util.test.ts`
suite (see `mutation-gap-report.md` M14).
