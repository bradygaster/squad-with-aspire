# PATH-Hijack Risk Analysis: Copilot CLI Resolution

**Scope**: `resolveCopilotCmd()` and any callers that resolve the `copilot` binary by short name through `shell:true` or PATH lookup.

---

## 1. Threat

On Windows, when `child_process.execFile('copilot', args, { shell: true })` runs, `cmd.exe` performs the following lookup in order for *each* directory on `PATH`:

1. `copilot` (no extension) — exact match
2. `copilot.com`, `copilot.exe`, `copilot.bat`, `copilot.cmd`, … in the order listed in `PATHEXT`

The **first match wins**, scanning PATH left-to-right. Any directory writable by the current user that appears **earlier** in PATH than the genuine Copilot install directory can host a malicious `copilot.cmd` (or `.exe`, `.bat`, `.com`) that intercepts every invocation by the squad runtime.

Common user-writable PATH entries on Windows:

- `%USERPROFILE%\AppData\Local\Microsoft\WindowsApps` (always writable, usually high in PATH)
- `%USERPROFILE%\.dotnet\tools`
- `%USERPROFILE%\AppData\Roaming\npm`
- `%USERPROFILE%\AppData\Local\Programs\Python\Python3xx\Scripts`
- Any per-user scoop/chocolatey shims
- Repo-local `node_modules\.bin` when added via shell init scripts

A malicious dependency, postinstall script, or compromised dev workstation can plant `%USERPROFILE%\AppData\Local\Microsoft\WindowsApps\copilot.cmd` and silently intercept every squad-cli invocation that uses bare-name resolution.

---

## 2. Impact (cross-references to STRIDE)

- **S1**: Spoofs Copilot CLI identity — all prompts, flags, tokens flow through attacker code.
- **I1/I2**: Captures the agent prompt (which embeds external Teams/email content **and** any context the agent has assembled — including secrets, file paths, repo contents).
- **E1**: Runs as the squad runtime principal — same privileges as the legitimate Copilot CLI would have, including any auth tokens persisted by Copilot itself.
- **T1/T2 amplifier**: A hijacked shim doesn't even need shell injection — it owns the entire argv directly.

The hijacked shim is invisible to source-level regex tests and to the `shell:IS_WINDOWS` audit (the call is structurally clean; the binary itself is hostile).

---

## 3. Mitigations

### Tier 1 — Absolute-path resolution (RECOMMENDED, ship now)

Replace bare-name `copilot` with the resolved absolute path discovered during preflight. Use `where.exe copilot` once at startup, cache the result, and pass the absolute path to all subsequent `execFile` calls. With an absolute path:

- `shell: true` becomes unnecessary (the `.cmd` extension is explicit).
- PATHEXT lookup is skipped — only the file actually pointed at runs.
- Hijack requires write access to the *resolved* directory, not any PATH entry.

```ts
// Already partially landed in resolveCopilotCmd() — needs to expose absolute path.
async function resolveCopilotCmd(): Promise<{ cmd: string; absolute: boolean }> {
  if (cache) return cache;
  if (IS_WINDOWS) {
    const { stdout } = await execFileAsync('where.exe', ['copilot'], { timeout: 5_000 });
    const lines = stdout.split(/\r?\n/).filter(Boolean);
    if (lines.length === 0) throw new CopilotCliMissingError({ tried: ['where.exe copilot'] });
    cache = { cmd: lines[0].trim(), absolute: true };
  } else {
    cache = { cmd: 'copilot', absolute: false }; // POSIX `which` equivalent if desired
  }
  return cache;
}
```

Then in spawn sites: `execFile(resolved.cmd, args, { shell: false, timeout: 30_000 })`.

### Tier 2 — Install-location allow-list (DEFENSE IN DEPTH)

After resolving, verify the resolved path matches an expected install root:

- `%ProgramFiles%\GitHub CLI\` (when copilot ships as a gh extension wrapper)
- `%LOCALAPPDATA%\GitHubCLI\` 
- `%USERPROFILE%\.gh\extensions\gh-copilot\` (gh extension install)
- npm global: `%APPDATA%\npm\` (only if explicitly opted in; warn loudly)

If the resolved path is **outside** the allow-list, emit a `SECURITY_WARN_COPILOT_LOCATION` warning to the operator with the path so they can review. Do not fail closed by default (false-positive risk on dev machines is high); fail closed when `SQUAD_STRICT_PATH=1` is set.

### Tier 3 — Authenticode/signature check (BEST EFFORT)

On Windows, optionally invoke `Get-AuthenticodeSignature` on the resolved binary and verify the signer is GitHub / Microsoft. Skip silently if PowerShell is unavailable. This is **not** a primary control (signature checks have well-known bypasses via signed-but-vulnerable wrappers), but raises the bar.

```ts
async function verifySignature(path: string): Promise<'github'|'microsoft'|'unknown'|'invalid'|'unavailable'> { /* … */ }
```

### Tier 4 — Hash pinning (FLEET DEPLOYMENTS ONLY)

For managed fleet rollouts, ship a SHA-256 of the expected `copilot.exe` per release and verify on each startup. Not appropriate for OSS distribution (Copilot CLI auto-updates).

### Tier 5 — Operator hardening guidance (DOCS)

Add a `SECURITY.md` section "Trust Boundaries → PATH" advising operators to:

- Run squad-cli under a dedicated low-privilege account.
- Audit `%PATH%` for user-writable directories earlier than the Copilot install dir.
- Prefer `gh copilot` invocation (resolves via the `gh` binary, which itself is typically installed under `%ProgramFiles%`).

---

## 4. Recommended action set

| Priority | Mitigation | Owner | Effort |
|---|---|---|---|
| P0 | Tier 1 — absolute-path resolution + drop `shell:true` from spawn sites | application-development-squad | S |
| P1 | Tier 2 — install-location allow-list with `SECURITY_WARN_COPILOT_LOCATION` | security-hardening-squad | S |
| P1 | Tier 5 — `SECURITY.md` trust-boundary section for PATH | security-hardening-squad | XS |
| P2 | Tier 3 — Authenticode best-effort check behind `SQUAD_VERIFY_SIGNATURE=1` flag | security-hardening-squad | M |
| P3 | Tier 4 — hash pinning (only if/when fleet management is in scope) | review-deployment-squad | M |

Tiers 1+2+5 close the realistic attack surface and are mandatory for sign-off on the spawn-side hardening PR.

— security-hardening-squad, supply-chain subagent
