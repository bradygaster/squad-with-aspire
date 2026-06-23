# Security Policy

This file is the security policy for the squad-with-aspire analysis repository
and the proposed upstream contributions for `bradygaster/squad`. It is owned by
the **security-hardening-squad** subagents.

## Reporting a vulnerability

Use GitHub Security Advisories (private reporting) on `bradygaster/squad`.
Do not open public issues for unpatched vulnerabilities. If GHSA is unavailable
under your account's EMU policy, route the report through a maintainer DM.

We aim to acknowledge within 72 hours and ship a patched release within 14 days
for HIGH/CRITICAL CVSSv3.1 scores.

## Supported versions

The `main` branch is the only supported version. Security fixes are not
backported to release tags.

## Trust boundaries

The squad CLI shells out to external processes (`copilot`, `gh copilot`, `git`,
`where.exe`). These crossings are the primary attack surface.

### `PATH` and executable resolution

The `copilot` binary is resolved at startup by `resolveCopilotCmd()` in
`packages/squad-cli/src/cli/util/copilot-cli.ts`. The resolver:

1. **Resolves to an absolute path** via `where.exe copilot` on Windows and
   `which copilot` on POSIX. The absolute path is cached for the lifetime of
   the process.
2. **Refuses `shell: true`** on the hot spawn path
   (`packages/squad-cli/src/cli/commands/watch/agent-spawn.ts`). All arguments
   are passed as an `argv[]` array; the OS does no metacharacter expansion.
3. **Validates `copilotFlags`** against a strict regex before splitting.
   Flags that fail validation cause `spawnAgent` to throw.

If the resolved `copilot` path lives outside a trusted install location, the
CLI emits a `SQUAD_PATH_WARNING` to stderr but does not abort. Set
`SQUAD_REQUIRE_TRUSTED_PATH=1` to make this fatal in CI.

**Threat model**: an attacker with write access to a directory earlier on `PATH`
than the legitimate Copilot install can drop a shim that the CLI will invoke
as the current user. The mitigations above narrow this to the case where the
attacker already has write access to a trusted install location, at which
point the system is already compromised. See
`analysis/squad-1372/path-hijack-risk.md` for the full 5-tier rationale.

### `copilot` argument construction

| Source | Sanitization |
|---|---|
| Static flags (`--version`, `--prompt`) | None needed -- hard-coded literals. |
| `copilotFlags` (user config) | Regex allow-list. |
| Prompt body (user + Teams/email content) | Passed as a single argv element. With `shell: false` the OS treats it as opaque data. |

Never reintroduce `shell: true` on a spawn path that receives untrusted
content. The `.semgrep/squad-spawn-rules.yml` ruleset enforces this in CI.

### Signature verification (best-effort, optional)

When `SQUAD_VERIFY_SIGNATURE=1` is set, the CLI calls
`verifyCopilotSignature()` which on Windows runs `Get-AuthenticodeSignature`
via PowerShell, and on macOS runs `codesign --verify`. Linux is a no-op.

This is **defense in depth, not a primary control**. A failed signature check
emits `SQUAD_SIGNATURE_WARNING` and aborts only when
`SQUAD_REQUIRE_SIGNATURE=1` is also set.

## Static analysis

Three semgrep rules under `.semgrep/squad-spawn-rules.yml` block regressions
of the #1372 class of bugs:

- `no-shell-true-with-tainted-args` (ERROR)
- `no-bare-copilot-execfile` (WARN -> ERROR after the spawn-side fix lands)
- `no-string-split-as-spawn-args` (WARN)

These run on every PR via `.github/workflows/security-static.yml`. Findings
require either a fix or a documented `nosemgrep` suppression with a rule-id
comment and a justification.

## Disclosure record

| ID | Date | Severity | Component | Status |
|---|---|---|---|---|
| squad-1372 | 2026-06-23 | HIGH (CVSSv3.1 8.8) | `agent-spawn.ts` `shell: true` on Windows | Patch staged in `upstream-transplants/squad-1372/` |

Full details: `analysis/squad-1372/disclosure-draft.md`.
