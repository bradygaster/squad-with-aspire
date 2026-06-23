# Copilot CLI Missing — Voice & Tone Matrix

**Owner:** experience-design-squad · content-strategy subagent
**Scope:** Every user-facing string emitted when `copilot` / `gh copilot` is absent, broken, or shadowed across `loop`, `doctor`, `fleet-dispatch`, `monitor-email`, `monitor-teams` in `bradygaster/squad` packages/squad-cli.
**Status:** Locked. Bind to PR for #1372.

## 1. Audit — current call sites

| # | File:line (upstream `bradygaster/squad`) | Mode | Current string (paraphrased) | Issues |
|---|---|---|---|---|
| 1 | `commands/loop.ts:~322` | fatal | `Copilot CLI not found. Install: ...` | terse; no Windows verify; no `squad doctor` hint |
| 2 | `commands/doctor.ts:~454` | warn | `Copilot CLI not detected on PATH` | inconsistent with loop; no install paths |
| 3 | `commands/watch/agent-spawn.ts:~34` | fatal-via-throw | `resolveCopilotCmd: copilot not found` | dev-string leak; user-hostile |
| 4 | `commands/watch/fleet-dispatch.ts:~88` | crash | unhandled `ENOENT` from bare `execSync` | no message at all — stack trace |
| 5 | `commands/monitor-email.ts` (preflight) | warn | `[monitor-email] skipping: copilot missing` | no remediation |
| 6 | `commands/monitor-teams.ts` (preflight) | warn | `[monitor-teams] skipping: copilot missing` | duplicates #5 |

Inconsistencies: 3 distinct phrasings for the same condition, 2 sites with no remediation, 1 site that throws raw `Error`.

## 2. Voice principles

1. **Plain, calm, factual.** No emoji in payload (`cli-entry.ts:1132` owns the `✗`). No exclamation marks. No "Oops".
2. **Diagnose → remediate → verify.** Every message names the condition, then names exactly what to type next.
3. **Two install paths, always.** Standalone `copilot` first (preferred), `gh copilot` second.
4. **Platform-aware.** Windows callers get `Get-Command copilot`; POSIX callers get `command -v copilot`.
5. **Same string everywhere.** All six call sites route through `copilotCliMissingMessage(detection, mode)`.
6. **No promises.** Don't say "this will fix it" — say "try this".

## 3. Tone matrix by surface

| Surface | Mode | Severity prefix | Body length | Includes "What we tried"? | Includes `squad doctor`? |
|---|---|---|---|---|---|
| `loop` (interactive run) | **fatal** | (none — handler prepends `✗`) | full | yes if `detection.probes` populated | yes (Path C) |
| `doctor` | **warn** | `⚠` (doctor owns its own prefix) | full | yes | n/a (we are doctor) |
| `agent-spawn` (watch loop) | **fatal-via-throw** | none | short (1 line + URL) | no (round will retry) | yes |
| `fleet-dispatch` | **fatal** | none | full | yes | yes |
| `monitor-*` preflight | **warn-skip** | `[monitor-email] skipping:` | short (1 line + URL) | no | yes |

## 4. Canonical strings (verbatim — drop in)

### 4a. Fatal (loop, fleet-dispatch)
```
Copilot CLI required

This command spawns a Copilot CLI session and that binary was not
found on PATH.

Install one of:

  Standalone (recommended):
    https://github.com/github/copilot

  GitHub CLI extension:
    gh extension install github/gh-copilot

{{TRIED}}

Verify install:
  Windows:  Get-Command copilot
  POSIX:    command -v copilot

Then re-run, or run:  squad doctor
```

### 4b. Warn (doctor)
```
Copilot CLI not detected.

Install one of:
  - Standalone:  https://github.com/github/copilot
  - GH ext:      gh extension install github/gh-copilot

Override if installed under a different name:
  --agent-cmd <path-to-binary>
```

### 4c. Warn-skip (monitor-email, monitor-teams)
```
[monitor-email] skipping round: Copilot CLI not on PATH.
  Install:  https://github.com/github/copilot
  Verify:   squad doctor
```

### 4d. Throw (agent-spawn — short, internal-ish)
```
Copilot CLI not available for spawn. Round skipped.
Run `squad doctor` for diagnosis.
```

## 5. Token rules

- `{{TRIED}}` block is rendered iff `detection.probes` has ≥1 entry with a `reason`. Otherwise the entire block (including the surrounding blank lines) is omitted — no empty "What we tried:" header.
- URLs always on their own line, no surrounding punctuation.
- ≤ 80 columns. Hard wrap at 78.
- NO_COLOR-safe: no ANSI in payload. Caller may colorize the prefix only.

## 6. Anti-patterns (do not ship)

- ❌ "Whoops!" / "Sorry, ..." / smileys
- ❌ "Please install Copilot CLI" (drop "please")
- ❌ "Run `squad doctor` to fix" (we don't promise fix)
- ❌ Inline `Error:` prefix (handler owns it)
- ❌ Backticks around shell commands in payload (renders as literal in NO_COLOR terminals — use indent instead)
- ❌ Different phrasings of "not found" across sites
